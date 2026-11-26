using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Bridge;

public enum TypedField
{
    Size,
    Background,
    Gap,
    LabelSize,
    ValueSize,
}

/// <summary>What a typed number comes to: the edit that keeps it, or none, and what Narrator hears of it, or nothing when it was kept as typed.</summary>
public sealed record TypedAnswer(Func<ThermiConfig, EditResult>? Edit, string? Said);

/// <summary>
/// A number typed in a slider's box or a text size's, read in the window's number format with or without its unit, and
/// kept as its field keeps it: a whole percent, a whole pixel or a half, held to the field's range.
/// </summary>
public static class TypedNumbers
{
    public static TypedAnswer Read(ThermiConfig config, TypedEdit typed, ValueFormat format)
    {
        if (config.Overlays.ElementAtOrDefault(typed.Overlay) is not { } overlay)
        {
            return new(null, format.Texts.Of(Refusal.NotFound));
        }

        var rule = RuleOf(typed.Field);
        var label = rule.Label(format.Texts);
        var text = typed.Typed.Trim();
        if (text.EndsWith(rule.Range.Unit, StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^rule.Range.Unit.Length].TrimEnd();
        }

        if (!double.TryParse(text, NumberStyles.Float, format.Numbers, out var number) || !double.IsFinite(number))
        {
            return new(null, format.Texts.Format(format.Numbers, OverlayPageText.TypedNotANumber, label, Shown(rule, rule.Held(overlay), format)));
        }

        var kept = Stepped(rule, number) / rule.Range.PerShown;
        var held = Math.Clamp(kept, rule.Range.Min, rule.Range.Max);
        var said = kept >= rule.Range.Min && kept <= rule.Range.Max ? null
            : format.Texts.Format(
                format.Numbers,
                OverlayPageText.TypedOutOfRange,
                label,
                Shown(rule, rule.Range.Min, format),
                Shown(rule, rule.Range.Max, format),
                Shown(rule, held, format));
        return new(config => rule.Edit(config, typed.Overlay, held), said);
    }

    // The background keeps the whole overlay's opacity, which only hand edits and look files set; a text size keeps its
    // weight and makes the look custom, the other text kept as it shows.
    private static Rule RuleOf(TypedField field) => field switch
    {
        TypedField.Size => new(
            new(OverlayConfig.MinSize, OverlayConfig.MaxSize, 100, 1, "%"),
            texts => texts.Of(UiText.SizeField),
            overlay => overlay.Size,
            OverlayEdits.SetSize),
        TypedField.Background => new(
            new(0, 1, 100, 1, "%"),
            texts => texts.Of(UiText.BackgroundField),
            overlay => overlay.Style.Opacity.Background,
            (config, at, held) => StyleEdits.SetOpacity(config, at, config.Overlays[at].Style.Opacity with { Background = held })),
        TypedField.Gap => new(
            new(0, StyleConfig.MaxGap, 1, 1, "px"),
            texts => texts.Of(OverlayPageText.GapField),
            overlay => overlay.Style.Gap,
            (config, at, held) => StyleEdits.Restyle(config, at, style => style with { Gap = (int)held })),
        TypedField.LabelSize => TextRule(TextPart.Label, UiText.LabelsField),
        TypedField.ValueSize => TextRule(TextPart.Value, UiText.ValuesField),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No box takes it."),
    };

    private static Rule TextRule(TextPart part, UiText name) => new(
        new(TextStyle.MinSize, TextStyle.MaxSize, 1, 0.5, "px"),
        texts => texts.Of(name),
        overlay => TextOf(overlay.Style, part).Size,
        (config, at, held) => StyleEdits.SetText(config, at, part, TextOf(config.Overlays[at].Style, part) with { Size = held }));

    private static TextStyle TextOf(StyleConfig style, TextPart part) =>
        StylePresets.Text(style) is var (label, value) && part == TextPart.Label ? label : value;

    private static double Stepped(Rule rule, double shown) => Math.Round(shown / rule.Range.Step, MidpointRounding.AwayFromZero) * rule.Range.Step;

    private static string Shown(Rule rule, double value, ValueFormat format) =>
        string.Format(format.Numbers, "{0} {1}", Stepped(rule, value * rule.Range.PerShown), rule.Range.Unit);

    // PerShown is how many of what the box shows make one of what the file keeps, as 100 percent make a size of 1, and
    // Step is the least the box takes between two numbers.
    private sealed record Range(double Min, double Max, double PerShown, double Step, string Unit);

    private sealed record Rule(Range Range, Func<Texts, string> Label, Func<OverlayConfig, double> Held, Func<ThermiConfig, int, double, EditResult> Edit);
}
