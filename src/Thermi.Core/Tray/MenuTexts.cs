using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>Where a menu shows: thermi's own icon's, or an overlay's, from the overlay or its items' icons.</summary>
public sealed record MenuPlace(ThermiConfig Config, Texts Texts, IFormatProvider Numbers, int? Overlay)
{
    /// <summary>The undo entry's text, with the last desktop gesture's name, when the overlay offers one.</summary>
    public string? UndoText { get; init; }

    /// <summary>The last snapshot read, which names an item the file leaves nameless.</summary>
    public Snapshot? Known { get; init; }
}

/// <summary>What each row of <see cref="Menus"/> reads, as every OS's menus show it.</summary>
public static class MenuTexts
{
    /// <summary>A row's text: the translation of its entry, or a variant that depends on where the menu is shown.</summary>
    public static string TextOf(MenuRow row, MenuPlace place, bool submenu)
    {
        var (config, texts, entry) = (place.Config, place.Texts, row.Entry);
        var several = place.Overlay is null && !submenu && config.Overlays.Count > 1;
        HostText? local = (entry, submenu, several) switch
        {
            (MenuEntry.ShowOverlay, true, _) => HostText.Show,
            (MenuEntry.ShowOverlay, false, _) when config.Overlays.Count == 1 => HostText.ShowTheOverlay,
            (MenuEntry.HideThisOverlay, true, _) => HostText.Hide,
            (MenuEntry.ClickThrough, _, true) => HostText.ClickThroughAll,
            (MenuEntry.Lock, _, true) => HostText.LockAll,
            _ => null,
        };
        if (local is { } text)
        {
            return texts.Of(text);
        }

        if (entry is MenuEntry.ShowOverlay or MenuEntry.Overlay)
        {
            return texts.Format(place.Numbers, entry, NameOf(config, row.Overlay ?? 0, texts));
        }

        if (entry is MenuEntry.EditItem or MenuEntry.Separate)
        {
            return texts.Format(place.Numbers, entry, Menus.NameOf(config, row, place.Known, texts, place.Numbers));
        }

        return ChoiceOf(row, place) ?? texts.Of(entry);
    }

    /// <summary>What shows beside a row, as a shortcut does: its hotkey, or why it is grayed; null for neither.</summary>
    public static string? Beside(MenuRow row, Texts texts) => row.Hotkey ?? (row.Note is { } note ? texts.Of(note) : null);

    /// <summary>An overlay's name, as the window's list says it.</summary>
    public static string NameOf(ThermiConfig config, int overlay, Texts texts) =>
        overlay >= config.Overlays.Count ? "" : OverlayNames.Of(config, overlay, texts);

    private static string? ChoiceOf(MenuRow row, MenuPlace place) =>
        row.Graph is { } graph ? place.Texts.Of(graph)
        : row is { Entry: MenuEntry.Size, Size: { } size } ? place.Texts.Format(place.Numbers, HostText.SizePercent, Math.Round(size * 100))
        : row.Entry == MenuEntry.Look ? row.Look
        : row.Entry == MenuEntry.UndoGesture ? place.UndoText
        : null;
}
