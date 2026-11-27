using Thermi.Core;
using Thermi.Linux.Hotkeys;

namespace Thermi.Linux.Gnome;

/// <summary>
/// thermi's hotkeys on GNOME, which lets no program grab keys of its own: its extension takes each as GTK's accelerator,
/// as the Shell takes its own, and one the Shell or another program holds is refused.
/// </summary>
internal sealed class ShellHotkeys : IHotkeyGrab
{
    private readonly ShellDesktop _desktop;

    public ShellHotkeys(ShellDesktop desktop)
    {
        _desktop = desktop;
        desktop.Hotkey = index => Pressed((int)index);
    }

    public Action<int> Pressed { get; set; } = _ => { };

    public Task<IReadOnlyList<bool>> Take(IReadOnlyList<Hotkey?> hotkeys) =>
        _desktop.Hotkeys([.. hotkeys.Select(hotkey => hotkey is { } taken ? KeyNames.Accelerator(taken) : "")]);

    public void Dispose()
    {
        // The desktop's link is the host's, which ends it.
    }
}
