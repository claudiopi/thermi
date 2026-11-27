using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// An overlay's menu off GNOME as GIO's model, which GTK draws: a row's access key, marked as Windows marks it, as GTK's
/// mnemonic; the default row bold; a radio as a radio, chosen or not; a check as a check.
/// </summary>
public sealed class PictureMenuTests
{
    public enum Row
    {
        RadioChosen,
        RadioNot,
        Check,
        Default,
    }

    [Theory]
    [InlineData(Row.RadioChosen, "_Size|on|on|")]
    [InlineData(Row.RadioNot, "_Size||on|")]
    [InlineData(Row.Check, "_Fade|true||")]
    [InlineData(Row.Default, "<b>_Show &amp; hide__all</b>|||true")]
    public void AMenuRowIsWhatGtkDrawsFromIt(Row row, string drawn)
    {
        Gio.Module.Initialize();
        var shown = row switch
        {
            Row.RadioChosen => new ShellRow(-1, "&Size", null, Checked: true, Grayed: false, Starts: true) { Radio = true },
            Row.RadioNot => new ShellRow(-1, "&Size", null, Checked: false, Grayed: false, Starts: true) { Radio = true },
            Row.Check => new ShellRow(-1, "&Fade", null, Checked: true, Grayed: false, Starts: true),
            Row.Default => new ShellRow(-1, "&Show && hide_all", null, Checked: false, Grayed: false, Starts: true) { Default = true },
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case."),
        };

        var menu = PictureMenu.Of([shown], _ => { });

        var section = menu.Model.GetItemLink(0, "section")!;
        string Read(string attribute)
        {
            return section.GetItemAttributeValue(0, attribute, null) is { } value ? Text(value) : "";
        }

        var state = menu.Actions.GetActionState("row0") is { } now ? Text(now) : "";
        Assert.Equal(drawn, $"{Read("label")}|{state}|{Read("target")}|{Read("use-markup")}");
    }

    private static string Text(GLib.Variant value) => value.GetTypeString() == "s" ? value.GetString(out _) : value.Print(false);
}
