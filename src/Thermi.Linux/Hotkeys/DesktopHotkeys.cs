using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Hotkeys;

/// <summary>
/// How a desktop lets a program take combinations anywhere: the GNOME Shell through thermi's extension, the GlobalShortcuts
/// portal, or X11's own grab. A press says the hotkey's place in those last taken, on the GLib main loop's thread.
/// </summary>
internal interface IHotkeyGrab : IDisposable
{
    Action<int> Pressed { get; set; }

    /// <summary>Whether taking them asks the person, as a portal's dialog does, so nothing on screen waits for it.</summary>
    bool AsksThePerson => false;

    /// <summary>Lets the hotkeys taken before go and takes these, a null for none: which of them are thermi's now.</summary>
    Task<IReadOnlyList<bool>> Take(IReadOnlyList<Hotkey?> hotkeys);
}

/// <summary>What <see cref="DesktopHotkeys"/> reaches in its host: the settings, the overlays' visibility and the values' history.</summary>
internal sealed record HotkeyHost(Settings Settings, OverlayVisibility Visibility, ItemHistory History, TraceEvents Trace, ILogger Log)
{
    public required Action RequestPaint { get; init; }
}

/// <summary>
/// The four hotkeys of the settings, taken on the desktop each time they change, and what each does when pressed, as on
/// Windows (<see cref="HotkeyPresses"/>). One that another program holds is refused: thermi logs it, and under
/// <c>--trace</c> says <c>hotkey-refused</c> with its role as the settings name it.
/// </summary>
internal sealed class DesktopHotkeys : IDisposable
{
    private readonly IHotkeyGrab _grab;
    private readonly HotkeyHost _host;
    private HotkeysConfig? _tried;
    private bool _taken;

    public DesktopHotkeys(IHotkeyGrab grab, HotkeyHost host)
    {
        (_grab, _host) = (grab, host);
        grab.Pressed = Pressed;
    }

    /// <summary>Which of the hotkeys last tried could not be taken, as the window shows it.</summary>
    public HotkeyFailures Failures { get; private set; }

    /// <summary>
    /// Takes the hotkeys of <paramref name="config"/> when they are not the ones taken: done once the desktop took them, or
    /// at once where it asks the person.
    /// </summary>
    public Task Update(ThermiConfig config)
    {
        if (_taken && config.Hotkeys == _tried)
        {
            return Task.CompletedTask;
        }

        (_tried, _taken) = (config.Hotkeys, true);
        var taking = TakeAsync(config.Hotkeys);
        return _grab.AsksThePerson ? Task.CompletedTask : taking;
    }

    /// <summary>The desktop let every hotkey go, as the Shell's extension does as it stops: the next update takes them again.</summary>
    public void Forget() => _taken = false;

    public void Dispose() => _grab.Dispose();

    private async Task TakeAsync(HotkeysConfig? hotkeys)
    {
        var parsed = Core.Hotkeys.Roles.Select(role => Core.Hotkeys.Parse(Core.Hotkeys.Of(hotkeys, role))).ToList();
        IReadOnlyList<bool> taken;
        try
        {
            taken = await _grab.Take(parsed);
        }
        catch (GLib.GException ex)
        {
            // The desktop may not know thermi yet: the next paint asks again.
            _host.Log.HotkeysNotTaken(ex);
            _taken = hotkeys != _tried;
            taken = [.. parsed.Select(_ => false)];
        }

        if (hotkeys != _tried)
        {
            return;
        }

        var failures = HotkeyFailures.None;
        for (var at = 0; at < Core.Hotkeys.Roles.Count; at++)
        {
            var role = Core.Hotkeys.Roles[at];
            if (Core.Hotkeys.Of(hotkeys, role) is { } written && (parsed[at] is null || at >= taken.Count || !taken[at]))
            {
                failures |= (HotkeyFailures)(1 << at);
                _host.Log.HotkeyRefused(role, written);
                _host.Trace.Write(TraceEvents.HotkeyRefused, KeyNames.RoleOf(role));
            }
        }

        Failures = failures;
    }

    private void Pressed(int index)
    {
        if (index >= 0 && index < Core.Hotkeys.Roles.Count)
        {
            HotkeyPresses.Pressed(Core.Hotkeys.Roles[index], _host.Settings, _host.Visibility, _host.History);
            _host.RequestPaint();
        }
    }
}
