using Thermi.Core.Look;

namespace Thermi.Linux.Painting;

/// <summary>
/// A picture the painter drew: Cairo's image, premultiplied ARGB in the machine's byte order, as GNOME Shell and GTK take
/// it; its owner disposes it.
/// </summary>
internal sealed class Picture(int width, int height) : IDisposable
{
    /// <summary>The image the painter draws on.</summary>
    public Cairo.ImageSurface Surface { get; } = new(Cairo.Format.Argb32, width, height);

    public int Width => width;

    public int Height => height;

    public int Stride => Surface.Stride;

    /// <summary>The pixels, row by row, <see cref="Stride"/> bytes apart.</summary>
    public ReadOnlySpan<byte> Pixels
    {
        get
        {
            Surface.Flush();
            return Surface.GetData();
        }
    }

    /// <summary>The alpha of the pixel at (<paramref name="x"/>, <paramref name="y"/>), of 255.</summary>
    public byte AlphaAt(int x, int y) => Pixels[(y * Stride) + (x * 4) + (BitConverter.IsLittleEndian ? 3 : 0)];

    /// <summary>
    /// Where the picture draws anything, as boxes of its pixels: each row's runs of pixels not fully clear, those of rows
    /// alike made one box. A click takes the overlay only there, and passes through to what is under it elsewhere.
    /// </summary>
    public IReadOnlyList<PillBox> Drawn()
    {
        var pixels = Pixels;
        var alpha = BitConverter.IsLittleEndian ? 3 : 0;
        var boxes = new List<PillBox>();
        List<(int Left, int Right, int Box)> above = [];
        for (var y = 0; y < Height; y++)
        {
            List<(int Left, int Right, int Box)> row = [];
            var x = 0;
            while (x < Width)
            {
                var start = x;
                while (x < Width && pixels[(y * Stride) + (x * 4) + alpha] != 0)
                {
                    x++;
                }

                if (x > start)
                {
                    row.Add((start, x, -1));
                }

                x++;
            }

            row = [.. row.Select(run => above.FirstOrDefault(each => each.Left == run.Left && each.Right == run.Right, run) is { Box: >= 0 } same
                ? Grown(boxes, same)
                : Added(boxes, run, y))];
            above = row;
        }

        return boxes;
    }

    /// <summary>The picture as a PNG, encoded by GTK's own, as <c>thermi preview</c> and the window's copies write it.</summary>
    public byte[] Png()
    {
        var format = BitConverter.IsLittleEndian ? Gdk.MemoryFormat.B8g8r8a8Premultiplied : Gdk.MemoryFormat.A8r8g8b8Premultiplied;
        using var pixels = GLib.Bytes.New(Pixels.ToArray());
        using var texture = Gdk.MemoryTexture.New(Width, Height, format, pixels, (nuint)Stride);
        using var png = texture.SaveToPngBytes();
        return png.GetRegionSpan<byte>(0, png.GetSize()).ToArray();
    }

    public void Dispose() => Surface.Dispose();

    private static (int Left, int Right, int Box) Grown(List<PillBox> boxes, (int Left, int Right, int Box) run)
    {
        boxes[run.Box] = boxes[run.Box] with { Height = boxes[run.Box].Height + 1 };
        return run;
    }

    private static (int Left, int Right, int Box) Added(List<PillBox> boxes, (int Left, int Right, int Box) run, int y)
    {
        boxes.Add(new PillBox(run.Left, y, run.Right - run.Left, 1));
        return run with { Box = boxes.Count - 1 };
    }
}
