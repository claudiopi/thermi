using System.Globalization;
using System.Text.Json;
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
}

/// <summary>What a typed number comes to: the edit that keeps it, or none, and what Narrator hears of it, or nothing when it was kept as typed.</summary>
public sealed record TypedAnswer(PageEdit? Edit, string? Said);

/// <summary>
/// A number typed in a slider's box, read in the window's number format with or without its unit, and kept as its field
/// keeps it: a whole percent or a whole pixel, held to the slider's range.
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
        if (text.EndsWith(rule.Unit, StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^rule.Unit.Length].TrimEnd();
        }

        if (!double.TryParse(text, NumberStyles.Float, format.Numbers, out var number) || !double.IsFinite(number))
        {
            return new(null, format.Texts.Format(format.Numbers, OverlayPageText.TypedNotANumber, label, Shown(rule, rule.Held(overlay), format)));
        }

        var kept = Math.Round(number, MidpointRounding.AwayFromZero) / rule.PerShown;
        var held = Math.Clamp(kept, rule.Min, rule.Max);
        var said = kept >= rule.Min && kept <= rule.Max ? null
            : format.Texts.Format(
                format.Numbers,
                OverlayPageText.TypedOutOfRange,
                label,
                Shown(rule, rule.Min, format),
                Shown(rule, rule.Max, format),
                Shown(rule, held, format));
        return new(rule.Edit(typed.Overlay, overlay, held), said);
    }

    // The background keeps the whole overlay's opacity, which only hand edits and look files set.
    private static Rule RuleOf(TypedField field) => field switch
    {
        TypedField.Size => new(
            OverlayConfig.MinSize,
            OverlayConfig.MaxSize,
            100,
            "%",
            texts => texts.Of(UiText.SizeField),
            overlay => overlay.Size,
            (at, _, held) => new OverlayFieldEdit(at, "size", JsonSerializer.SerializeToElement(held, BridgeCalls.Options))),
        TypedField.Background => new(
            0,
            1,
            100,
            "%",
            texts => texts.Of(UiText.BackgroundField),
            overlay => overlay.Style.Opacity.Background,
            (at, overlay, held) =>
                new StyleFieldEdit(at, "opacity", JsonSerializer.SerializeToElement(overlay.Style.Opacity with { Background = held }, BridgeCalls.Options))),
        TypedField.Gap => new(
            0,
            StyleConfig.MaxGap,
            1,
            "px",
            texts => texts.Of(OverlayPageText.GapField),
            overlay => overlay.Style.Gap,
            (at, _, held) => new StyleFieldEdit(at, "gap", JsonSerializer.SerializeToElement((int)held, BridgeCalls.Options))),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No slider takes it."),
    };

    private static string Shown(Rule rule, double value, ValueFormat format) =>
        string.Format(format.Numbers, "{0} {1}", Math.Round(value * rule.PerShown, MidpointRounding.AwayFromZero), rule.Unit);

    // PerShown is how many of what the box shows make one of what the file keeps: 100 percent make a size of 1.
    private sealed record Rule(
        double Min,
        double Max,
        double PerShown,
        string Unit,
        Func<Texts, string> Label,
        Func<OverlayConfig, double> Held,
        Func<int, OverlayConfig, double, PageEdit> Edit);
}
