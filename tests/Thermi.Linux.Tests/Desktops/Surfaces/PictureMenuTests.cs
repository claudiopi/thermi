using System.Globalization;
using System.Net;
using System.Text;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;
using Thermi.Linux.Tray;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// An overlay's menu off GNOME as GIO's model, which GTK draws: a row's access key, marked as Windows marks it, as GTK's
/// mnemonic; the default row bold; a radio as a radio, chosen or not; a check as a check. Its rows read Core's texts, as a
/// tray's menu does, which marks no access key.
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

    public enum Shows
    {
        Overlay,
        Tray,
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

    // An item named with an "&" and a "_" of its own, which its rows show as they read, as every other row's text.
    [Theory]
    [InlineData(Shows.Overlay)]
    [InlineData(Shows.Tray)]
    public void AMenuReadsCoresTextsWithAccessKeysOnlyOnTheOverlay(Shows row)
    {
        Gio.Module.Initialize();
        var first = OverlayConfig.First;
        var config = ThermiConfig.New with { Overlays = [first with { Items = [first.Items[0] with { Name = "R&D_1" }, .. first.Items.Skip(1)] }] };
        var place = new MenuPlace(config, Texts.For(config.Language, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, 0);
        var state = new MenuState(false, false, ServiceState.Running, null, [], false);
        var menu = Menus.OfOverlay(config, 0, state, first.Items[0].Id, fromTray: row == Shows.Tray);

        var (all, rows) = OverlayMenu.Flattened(menu, place, keys: row == Shows.Overlay);

        var top = Enumerable.Range(0, rows.Count).Where(at => rows[at].Parent == -1).ToList();
        List<string> labels = row == Shows.Overlay
            ? LabelsOf(PictureMenu.Of(rows, _ => { }).Model)
            : [.. top.Select(at => TrayMenu.LabelOf(rows[at]).Split('\t')[0])];
        var read = labels.Select(Mnemonic).ToList();
        Assert.Equal([.. top.Select(at => MenuTexts.TextOf(all[at], place, submenu: false))], read.Select(each => each.Text));
        Assert.Equal(row == Shows.Overlay, read.Any(each => each.Keyed));
    }

    // Each label of the menu's own rows, in its sections' order, as it reads with no markup.
    private static List<string> LabelsOf(Gio.MenuModel model)
    {
        var labels = new List<string>();
        for (var at = 0; at < model.GetNItems(); at++)
        {
            var section = model.GetItemLink(at, "section")!;
            for (var each = 0; each < section.GetNItems(); each++)
            {
                var label = Text(section.GetItemAttributeValue(each, "label", null)!);
                var bold = section.GetItemAttributeValue(each, "use-markup", null) is not null;
                var unbold = label.Replace("<b>", "", StringComparison.Ordinal).Replace("</b>", "", StringComparison.Ordinal);
                labels.Add(bold ? WebUtility.HtmlDecode(unbold) : label);
            }
        }

        return labels;
    }

    // A label as GTK reads its underscores: one before the access key, two for one of the text's own.
    private static (string Text, bool Keyed) Mnemonic(string label)
    {
        var text = new StringBuilder(label.Length);
        var keyed = false;
        var underscore = false;
        foreach (var each in label)
        {
            if (each == '_' && !underscore)
            {
                underscore = true;
                continue;
            }

            keyed |= underscore && each != '_';
            underscore = false;
            text.Append(each);
        }

        return (text.ToString(), keyed);
    }

    private static string Text(GLib.Variant value) => value.GetTypeString() == "s" ? value.GetString(out _) : value.Print(false);
}
