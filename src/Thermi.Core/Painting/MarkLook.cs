using Thermi.Core.Look;
using Thermi.Core.Tray;

namespace Thermi.Core.Painting;

/// <summary>The mark's eyes: closed asleep, open awake, wide alarmed.</summary>
public enum MarkEyes
{
    Open,
    Wide,
    Closed,
}

/// <summary>How a state of the mark looks: its gradient left to right and its eyes, or no gradient for the one-color glyph, whose eyes are open.</summary>
public sealed record MarkLook(IReadOnlyList<(float At, Rgb Color)>? Stops, MarkEyes Eyes)
{
    public static readonly Rgb EyeInk = Rgb.Parse("#15151A");

    private static readonly Rgb Cool = Rgb.Parse("#3D5BFF");
    private static readonly Rgb Violet = Rgb.Parse("#8A4DFF");
    private static readonly Rgb Amber = Rgb.Parse("#FF9F1A");
    private static readonly Rgb Orange = Rgb.Parse("#FF7A1A");
    private static readonly Rgb Red = Rgb.Parse("#FF3B2F");

    public static MarkLook Of(MarkState state) => state switch
    {
        MarkState.Calm => new([(0, Cool), (1, Violet)], MarkEyes.Closed),
        MarkState.Warm => new([(0, Amber), (1, Orange)], MarkEyes.Open),
        MarkState.Hot => new([(0, Orange), (1, Red)], MarkEyes.Wide),
        MarkState.App => new([(0, Cool), (0.6f, Violet), (1, Orange)], MarkEyes.Open),
        MarkState.Stopped or MarkState.HighContrast or _ => new(null, MarkEyes.Open),
    };

    /// <summary>The gradient's color at <paramref name="at"/>, from 0 at its left to 1 at its right, held at its ends beyond them.</summary>
    public Rgb At(double at)
    {
        var stops = Stops!;
        var t = Math.Clamp(at, 0, 1);
        var next = Math.Max(1, stops.ToList().FindIndex(stop => stop.At >= t));
        var (from, to) = (stops[next - 1], stops[next]);
        var part = (t - from.At) / (to.At - from.At);
        return new Rgb(Mix(from.Color.R, to.Color.R, part), Mix(from.Color.G, to.Color.G, part), Mix(from.Color.B, to.Color.B, part));
    }

    private static byte Mix(byte from, byte to, double part) => (byte)Math.Round(from + ((to - from) * part));
}

/// <summary>
/// thermi's mark as a vector, in its own units, a 48 square, which every OS's painter scales to its box: a capsule body 36
/// by 23 from (6, 10), with a flat bottom on the line's side, sides that rise straight and round shoulders of radius 12;
/// two eyes; and the line under it, the taskbar's edge, shorter and thinner.
/// </summary>
public static class MarkShape
{
    public const float Units = 48;

    /// <summary>The width of the line and of a closed eye's lid.</summary>
    public const float Stroke = 2.4f;

    /// <summary>The body's box, which its gradient spans left to right.</summary>
    public static (float X, float Y, float Width, float Height) Body { get; } = (6, 10, 36, 23);

    /// <summary>The shoulders' radius: the body's top is two quarter circles of it.</summary>
    public const float Shoulder = 12;

    /// <summary>Where the body's straight sides meet its shoulders.</summary>
    public const float SidesTop = 22;

    /// <summary>The line under the body, its two ends on one height.</summary>
    public static (float FromX, float ToX, float Y) Line { get; } = (10, 38, 37.5f);

    /// <summary>The two eyes' middles, left to right.</summary>
    public static IReadOnlyList<float> EyesAt { get; } = [25, 33];

    /// <summary>The height an open eye leans about, 8° to the right.</summary>
    public const float LeanAt = 20;

    public const float Lean = 8;

    /// <summary>An open eye's middle height and radii, an upright oval, larger and a little higher when wide.</summary>
    public static (float Y, float RadiusX, float RadiusY) Eye(MarkEyes eyes) => eyes == MarkEyes.Wide ? (18.5f, 3.1f, 4.4f) : (19f, 2.6f, 3.7f);

    /// <summary>A closed eye: a smile turned down, the cubic curve of the eye at <paramref name="x"/>, from its start to its end.</summary>
    public static (float X, float Y)[] Lid(float x) => [(x - 3, 21), (x - 1, 23), (x + 1, 23), (x + 3, 21)];
}
