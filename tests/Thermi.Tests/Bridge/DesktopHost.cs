using System.Drawing;
using Microsoft.Extensions.Logging;
using Thermi.App.Painting;
using Thermi.App.Window;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Updates;

namespace Thermi.Tests.Bridge;

/// <summary>
/// A window's host that is only its desktop, so a test asks the window's bridge what the page reads on any OS from any OS;
/// it has no monitors, no overlay and no hotkeys, and refuses what reaches the desktop itself.
/// </summary>
internal sealed class DesktopHost(HostDesktop desktop) : IWindowHost
{
    public HostDesktop Desktop => desktop;

    public bool ForEveryone => false;

    public bool StartsAtSignIn { get; set; }

    public IReadOnlyList<Screen> Screens() => [];

    public bool LightTaskbar() => false;

    public HighContrastColors? HighContrast() => null;

    public IOverlayPainter Painter() => new Measuring();

    public double? BaseClock() => null;

    public IReadOnlySet<int> AltGrKeys() => new HashSet<int>();

    public bool HotkeyHeld(string combination, HotkeysConfig? own) => false;

    public long? HotkeyStatus() => null;

    public bool OverlayRunning() => false;

    public void RestHotkeys(bool rest)
    {
        // No overlay holds hotkeys here.
    }

    public void Editing(int? overlay, string? selected, string? pointed)
    {
        // No overlay shows what is edited here.
    }

    public void ExitThermi() => throw Refused();

    public void StartOverlay() => throw Refused();

    public void Caption(CaptionAction action) => throw Refused();

    public void Open(string target, ILogger log) => throw Refused();

    public bool Copy(string text) => throw Refused();

    public Task<string?> Paste() => throw Refused();

    public Task<string?> SaveFile(string kind, string extension, string name) => throw Refused();

    public Task<string?> OpenFile(string kind, string extension) => throw Refused();

    public Task<DriverInstall> InstallDriver() => throw Refused();

    public Task<bool> StartService() => throw Refused();

    public Task<bool> InstallExtension() => throw Refused();

    public UpdateLine Updates() => new(UpdateLineKind.NotUpdating, Beta: false);

    public void CheckUpdates() => throw Refused();

    public void RestartToUpdate() => throw Refused();

    public AgentsView Agents(ValueFormat format, TimeZoneInfo zone) => new("", "", []);

    private static NotSupportedException Refused() => new("A test's desktop host reaches no desktop.");

    /// <summary>A painter that only measures, an item 40 px wide and 20 high, for an overlay no monitor shows.</summary>
    private sealed class Measuring : IOverlayPainter
    {
        public ((float Scale, (int Width, int Height)? Most, OverlayLook Look, string Items) Measured, float Scale)? Fitted { get; set; }

        public Size Measure(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale) =>
            new((int)(items.Count * 40 * scale), (int)(20 * scale));

        public PaintedPicture Picture(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale) => throw Refused();

        public PaintedPicture Stopped(string text, OverlayLook look, float scale, IReadOnlyList<OverlayItem> beside) => throw Refused();

        public void Dispose()
        {
            // It holds nothing.
        }
    }
}
