using Thermi.App.Gestures;
using Thermi.Linux.Desktop;
using Thermi.Linux.Tray;

namespace Thermi.Linux.Gnome;

/// <summary>
/// thermi's icons in GNOME's top bar, through its extension: the first button's press is a click or a double-click, and
/// the third button's opens the menu, whose rows thermi sends. <c>call</c> logs a call the Shell did not take.
/// </summary>
internal sealed class ShellIcons : ITrayIcons
{
    private const uint First = 1;
    private const uint Third = 3;

    private readonly ShellDesktop _desktop;
    private readonly TrayClicks _clicks;
    private readonly Func<Task, Task> _call;

    public ShellIcons(ShellDesktop desktop, PointerSettings pointer, Func<Task, Task> call)
    {
        (_desktop, _clicks, _call) = (desktop, new TrayClicks(pointer), call);
        desktop.IconPressed = Press;
        desktop.IconChosen = (name, row) => Chosen(name, row);
    }

    public Action<string, TrayPress> Pressed { get; set; } = (_, _) => { };

    public Func<string, IReadOnlyList<ShellRow>> MenuOf { get; set; } = _ => [];

    public Action<string, int> Chosen { get; set; } = (_, _) => { };

    public Task Show(IReadOnlyList<TrayPicture> icons) => _call(_desktop.Icons(icons));

    public void OpenMenu(string key) => _ = _call(_desktop.IconMenu(key, MenuOf(key)));

    public void Dispose()
    {
        // The desktop's link is the host's, which ends it.
    }

    private void Press(string name, uint button, uint time)
    {
        if (button == Third)
        {
            Pressed(name, TrayPress.Menu);
            return;
        }

        if (button != First)
        {
            return;
        }

        Pressed(name, _clicks.Click(name, time));
    }
}
