using System.Drawing;
using System.Drawing.Text;
using Thermi.Core.Settings;

namespace Thermi.Windows.Painting;

/// <summary>
/// The labels', values' and tags' fonts of one look at one scale. The painter makes a set only when the style's font, sizes,
/// weights or the scale change, and releases the old one then.
/// </summary>
internal sealed class PainterFonts : IDisposable
{
    public PainterFonts(StyleFont font, TextStyle label, TextStyle value, float scale)
    {
        Key = (font, label, value, scale);
        Label = Make(font, label, scale);
        Font? made = null;
        try
        {
            made = Make(font, value, scale);
            Tag = Make(font, new TextStyle(label.Size * Core.Painting.PaintSizes.TagShare, TextWeight.Regular), scale);
            Value = made;
        }
        catch
        {
            Label.Dispose();
            made?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The layout of text every painter on this thread shares, kept for the thread's life: each read of
    /// <see cref="StringFormat.GenericTypographic"/> makes a new one that only its finalizer frees. One per thread, since
    /// GDI+ objects are not to be shared between threads, and tests paint on several.
    /// </summary>
    [field: ThreadStatic]
    public static StringFormat Typographic => field ??= StringFormat.GenericTypographic;

    public (StyleFont Font, TextStyle Label, TextStyle Value, float Scale) Key { get; }

    public Font Label { get; }

    public Font Value { get; }

    public Font Tag { get; }

    public void Dispose()
    {
        Label.Dispose();
        Value.Dispose();
        Tag.Dispose();
    }

    private static Font Make(StyleFont font, TextStyle text, float scale)
    {
        var (families, style) = Face(font, text.Weight);
        return new Font(Family(families), (float)text.Size * scale, style, GraphicsUnit.Pixel);
    }

    // Segoe UI Variable is the Windows 11 face, and Windows 10 has only Segoe UI. Semibold is a face of its own, and bold
    // the regular or display face drawn bold. Consolas has no semibold, so labels stay lighter than bold values in it too.
    private static (string[] Families, FontStyle Style) Face(StyleFont font, TextWeight weight) => (font, weight) switch
    {
        (StyleFont.Segoe, TextWeight.Regular) => (["Segoe UI Variable Text", "Segoe UI"], FontStyle.Regular),
        (StyleFont.Segoe, TextWeight.Semibold) => (["Segoe UI Variable Text Semibold", "Segoe UI Semibold"], FontStyle.Regular),
        (StyleFont.Segoe, TextWeight.Bold) => (["Segoe UI Variable Display", "Segoe UI"], FontStyle.Bold),
        (StyleFont.Bahnschrift, TextWeight.Semibold) => (["Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold"], FontStyle.Regular),
        (StyleFont.Bahnschrift, _) => (["Bahnschrift", "Segoe UI"], Style(weight)),
        (StyleFont.Consolas, _) => (["Consolas", "Segoe UI"], Style(weight)),
        _ => throw new ArgumentOutOfRangeException(nameof(font), font, "Unknown font."),
    };

    private static FontStyle Style(TextWeight weight) => weight == TextWeight.Bold ? FontStyle.Bold : FontStyle.Regular;

    /// <summary>The first of the families that is installed, or the last: GDI+ silently swaps a missing one for Microsoft Sans Serif.</summary>
    internal static string Family(string[] families)
    {
        using var installed = new InstalledFontCollection();
        var present = installed.Families;
        try
        {
            return families.FirstOrDefault(name => present.Any(family => family.Name == name)) ?? families[^1];
        }
        finally
        {
            foreach (var family in present)
            {
                family.Dispose();
            }
        }
    }
}
