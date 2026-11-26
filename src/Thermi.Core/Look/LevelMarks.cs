using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>How a value shows its level: nothing at normal, an underline at warning, reversed at critical.</summary>
public enum MarkKind
{
    None,
    Underline,
    Reversed,
}

/// <summary>A level's mark: its kind, the color of the underline or of the reversed value's background, and the reversed value's text.</summary>
public readonly record struct LevelMark(MarkKind Kind, Rgb Color, Rgb Text);

/// <summary>The marks a look gives each level, so a level never rests on color alone.</summary>
public static class LevelMarks
{
    private static readonly Rgb Black = new(0, 0, 0);

    private static readonly Rgb White = new(255, 255, 255);

    /// <summary>
    /// The level's mark in the look's warning and critical colors, the critical value black on dark pills and white on
    /// light ones; under high contrast, in the system's window-text color with the reversed value in its window color. A
    /// <paramref name="metered"/> value's meter or graph shows a warning in its color, so the value is not underlined too.
    /// </summary>
    public static LevelMark Of(OverlayLook look, Level level, bool metered = false) => level switch
    {
        Level.Warning when metered => new LevelMark(MarkKind.None, default, default),
        Level.Warning => new LevelMark(MarkKind.Underline, look.HighContrast ? look.Pills.Value : look.Warning, default),
        Level.Critical when look.HighContrast => new LevelMark(MarkKind.Reversed, look.Pills.Value, look.Pills.Outer),
        Level.Critical => new LevelMark(MarkKind.Reversed, look.Critical, look.Pills.IsLight ? White : Black),
        Level.Normal => new LevelMark(MarkKind.None, default, default),
        _ => new LevelMark(MarkKind.None, default, default),
    };

    /// <summary>An overlay item's mark, as <see cref="Of(OverlayLook, Level, bool)"/> gives it for its level and whether it draws a meter or a graph.</summary>
    public static LevelMark Of(OverlayLook look, OverlayItem item) => Of(look, item.Level, item.Graph is not null || item.Lit is not null);
}
