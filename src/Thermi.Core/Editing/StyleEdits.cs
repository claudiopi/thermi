using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

public enum TextPart
{
    Label,
    Value,
}

/// <summary>A color of the style: the meter's three levels, and the labels' and values' text.</summary>
[Translated]
public enum StyleColor
{
    Normal,
    Warning,
    Critical,
    Label,
    Value,
}

/// <summary>
/// The window's edits of an overlay's look, each found by the overlay's place in the file. Every one that changes the
/// look forgets the saved style's name, since the look is no longer that style.
/// </summary>
public static class StyleEdits
{
    /// <summary>
    /// Any change of the style, such as its pills, units, layout or orientation. One that leaves the look as it was, as a
    /// change of layout or orientation does, keeps the name.
    /// </summary>
    public static EditResult Restyle(ThermiConfig config, int overlay, Func<StyleConfig, StyleConfig> change) =>
        ConfigEdits.OnOverlay(config, overlay, () => ConfigEdits.Apply(config, current =>
            ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Style = Restyled(current.Overlays[overlay].Style, change) })));

    /// <summary>
    /// Takes a saved or imported look whole, its name included, keeping the overlay's layout and orientation: one step,
    /// which undo takes back.
    /// </summary>
    public static EditResult Choose(ThermiConfig config, int overlay, StyleConfig style) =>
        ConfigEdits.OnOverlay(config, overlay, () => ConfigEdits.Apply(config, current =>
            ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Style = style.WornOn(current.Overlays[overlay].Style) })));

    /// <summary>Applies a style file's text, dropped or pasted, at once; one that is broken, newer or out of range changes nothing and says why.</summary>
    public static EditResult Import(ThermiConfig config, int overlay, string text)
    {
        var read = StyleFile.Read(text);
        return read.Style is { } style ? Choose(config, overlay, style) : new EditResult(config, read.Refused, read.Problem);
    }

    /// <summary>The colorblind-safe set as the meter's three colors, leaving the text colors as they are.</summary>
    public static EditResult UseColorblindSafe(ThermiConfig config, int overlay) =>
        Restyle(config, overlay, style => style with
        {
            Colors = (style.Colors ?? new StyleColors()) with
            {
                Normal = StyleColors.ColorblindSafe.Normal,
                Warning = StyleColors.ColorblindSafe.Warning,
                Critical = StyleColors.ColorblindSafe.Critical,
            },
        });

    /// <summary>A preset sets the text, so the sizes it ignores leave the file; custom starts from the text shown now.</summary>
    public static EditResult SetPreset(ThermiConfig config, int overlay, StylePreset preset) =>
        Restyle(config, overlay, style => preset == StylePreset.Custom ? Customized(style) : style with { Preset = preset, Label = null, Value = null });

    /// <summary>Sets the labels' or the values' size and weight, which makes the style custom and keeps the other text as it was.</summary>
    public static EditResult SetText(ThermiConfig config, int overlay, TextPart part, TextStyle text) =>
        Restyle(config, overlay, style => part == TextPart.Label ? Customized(style) with { Label = text } : Customized(style) with { Value = text });

    /// <summary>Sets the pills' background and the whole overlay's opacity, which leaves the preset as it is.</summary>
    public static EditResult SetOpacity(ThermiConfig config, int overlay, OpacityConfig opacity) =>
        Restyle(config, overlay, style => style with { Opacity = opacity });

    /// <summary>Sets a color as #RRGGBB, or with null gives it back its default: the meter's own, or Auto for text.</summary>
    public static EditResult SetColor(ThermiConfig config, int overlay, StyleColor which, string? color) =>
        Restyle(config, overlay, style => style with { Colors = WithColor(style.Colors ?? new StyleColors(), which, color) });

    private static StyleConfig Restyled(StyleConfig style, Func<StyleConfig, StyleConfig> change) =>
        change(style) is var changed && style.WornOn(changed) == changed ? changed : changed with { Name = null };

    private static StyleConfig Customized(StyleConfig style)
    {
        var (label, value) = StylePresets.Text(style);
        return style with { Preset = StylePreset.Custom, Label = label, Value = value };
    }

    // Colors that are all defaults leave the file, as a style that never changed one has none.
    private static StyleColors? WithColor(StyleColors colors, StyleColor which, string? color)
    {
        var changed = which switch
        {
            StyleColor.Normal => colors with { Normal = color },
            StyleColor.Warning => colors with { Warning = color },
            StyleColor.Critical => colors with { Critical = color },
            StyleColor.Label => colors with { Label = color },
            StyleColor.Value => colors with { Value = color },
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "Unknown style color."),
        };
        return changed == new StyleColors() ? null : changed;
    }
}
