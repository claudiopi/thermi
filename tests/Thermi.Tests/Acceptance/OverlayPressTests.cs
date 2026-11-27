using System.Drawing;
using Microsoft.Extensions.Time.Testing;
using Thermi.App.Gestures;
using Thermi.Core.Editing;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What a press on an overlay shows and how it ends on every OS, as App's <see cref="OverlayPress"/> decides it and each
/// desktop only shows it, and where its menu opens.
/// </summary>
public sealed class OverlayPressTests : IDisposable
{
    private static readonly Position Corner = new(100, 100);
    private static readonly Size Drawn = new(300, 40);

    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // The pointer as Windows shows it: the resize arrows on a floating overlay's corner, and while that corner is dragged;
    // the desktop's own elsewhere and on a snapped or locked overlay; no entry once a hold finds no room for another overlay.
    [Fact]
    public async Task ThePointerSaysWhatAPressWouldDo()
    {
        var config = OverlayEdits.Add(OverlayEdits.Add(OverlayEdits.Add(new ThermiConfig()).Config).Config).Config;
        var floating = config.Overlays[0] with { Position = Corner };
        await using var settings = await _folder.LoadedAsync(config with { Overlays = [floating, .. config.Overlays.Skip(1)] });
        using var host = new Host(_folder.Clock);
        using var press = new OverlayPress(0, settings, host);
        PointerShape At(OverlayConfig overlay, int x, int y)
        {
            host.At = new Position(x, y);
            return press.Shape(overlay, Drawn, 1, Corner);
        }

        var locked = floating with { Locked = true };
        PointerShape[] hovered = [At(floating, 395, 135), At(floating, 150, 110), At(locked, 395, 135), At(config.Overlays[0], 395, 135)];
        press.Press(floating, Drawn, 1, [SnapTests.Main], default, Corner, _ => host);
        var resizing = press.Shape(floating, Drawn, 1, Corner);
        press.Let();
        host.At = new Position(150, 110);
        press.Press(floating, Drawn, 1, [SnapTests.Main], new PressedOn(floating.Items[0].Id, default, default, default, false), Corner, _ => host);
        var waiting = press.Shape(floating, Drawn, 1, Corner);
        _folder.Clock.Advance(DesktopGestures.Hold);
        press.Hold();

        Assert.Equal([PointerShape.Resize, PointerShape.Default, PointerShape.Default, PointerShape.Default], hovered);
        Assert.Equal((PointerShape.Resize, PointerShape.Default), (resizing, waiting));
        Assert.Equal(PointerShape.NotAllowed, press.Shape(floating, Drawn, 1, Corner));
        Assert.True(press.Drag?.Refused);
    }

    // Another grab taking the pointer from a press, as the overview or a modal dialog does, ends it as Esc does: the overlay
    // goes back where it was, and nothing is written.
    [Fact]
    public async Task ALostGrabPutsTheOverlayBackAsEscDoes()
    {
        await using var settings = await _folder.LoadedAsync(new ThermiConfig());
        var before = settings.Config;
        using var host = new Host(_folder.Clock);
        using var press = new OverlayPress(0, settings, host);

        press.Press(before.Overlays[0], Drawn, 1, [SnapTests.Main], default, Corner, _ => host);
        press.Move(new Position(600, 500), new Position(600, 500));
        var cancelled = press.Cancel();

        Assert.Equal((true, false), (cancelled, press.Cancel()));
        Assert.Equal([new Position(600, 500), Corner], host.Moves);
        Assert.Null(press.Drag);
        Assert.Same(before, settings.Config);
    }

    // An overlay's menu opens at the pointer's x, beside the overlay so it covers none of its pills: below it where it fits,
    // as from a top bar, else above, as from a taskbar at the bottom, else on the side with more room; a desktop that knows
    // no menu's height before it shows gets below, and moves it itself.
    [Fact]
    public void AMenuOpensBesideItsOverlay()
    {
        var screen = new PixelRect(0, 0, 2560, 1440);
        var onTop = new PixelRect(1000, 0, 1500, 38);
        var onBottom = new PixelRect(1000, 1400, 1500, 1438);
        var cursor = new Position(1200, 20);

        var spots = new[]
        {
            MenuSpots.Beside(onTop, cursor, screen, 300),
            MenuSpots.Beside(onBottom, cursor with { Y = 1410 }, screen, 300),
            MenuSpots.Beside(onBottom, cursor, screen),
        };

        Assert.Equal([new Position(1200, 38), new Position(1200, 1400), new Position(1200, 1438)], spots.Select(spot => spot.At));
        Assert.Equal(new MenuSpot(new PixelRect(1200, 1400, 1201, 1438), MenuSide.Above), spots[1]);
        Assert.Equal(MenuSide.Below, MenuSpots.Beside(new PixelRect(0, 150, 100, 250), cursor, new PixelRect(0, 0, 100, 400), 200).Side);
        Assert.Equal(MenuSide.Above, MenuSpots.Beside(new PixelRect(0, 180, 100, 250), cursor, new PixelRect(0, 0, 100, 400), 200).Side);
        Assert.Equal(1499, MenuSpots.Beside(onTop, cursor with { X = 5000 }, screen, 300).Anchor.Left);
    }

    // A desktop that shows a drag as Windows does, its pointer where the test puts it.
    private sealed class Host(FakeTimeProvider time) : IPressHost, IDragShow, IDragMarks
    {
        public Position At { get; set; } = new(0, 0);

        public List<Position> Moves { get; } = [];

        public TimeProvider Time => time;

        public DropPreview Preview { get; } = new();

        public GestureUndo? Gestures => null;

        public Position Cursor() => At;

        public void RequestPaint()
        {
            // Nothing is painted here.
        }

        public IReadOnlyList<PixelRect?> Shown() => [];

        public DropTarget JoinAt(int onto, Position cursor, string? alone) => new(onto, null);

        public EditResult Settle(ThermiConfig before, ThermiConfig after) => new(after);

        public void Wake(TimeSpan after)
        {
            // The test calls the hold itself.
        }

        public void Sleep()
        {
            // No wake is kept.
        }

        public bool HoldEscape() => true;

        public void LetEscape()
        {
            // Esc was never taken.
        }

        public void MoveTo(Position at) => Moves.Add(at);

        public Size Lift(Position at) => new(100, 38);

        public void Carry(Position at)
        {
            // No picture is carried.
        }

        public IDragMarks Marks(IReadOnlyList<Screen> screens, Orientation orientation, Size size, bool lifted) => this;

        public void Light(SnapConfig? snap)
        {
            // No mark shows.
        }

        public void Refuse(bool refused)
        {
            // The pointer's shape is asked of the press.
        }

        public bool AltHeld() => false;

        public void Dispose()
        {
            // Nothing is held.
        }
    }
}
