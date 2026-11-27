namespace Thermi.Linux.Painting;

/// <summary>
/// A picture the painter drew: Cairo's image, premultiplied ARGB in the machine's byte order, as GNOME Shell and GTK take
/// it; its owner disposes it.
/// </summary>
internal sealed class Picture(int width, int height) : IDisposable
{
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

    /// <summary>A copy of the pixels in GLib's memory, which a call on the bus carries as they are; its owner disposes it.</summary>
    public GLib.Bytes Bytes()
    {
        Surface.Flush();
        return GLib.Bytes.New(Surface.GetData());
    }

    /// <summary>Every pixel transparent, as a new picture's.</summary>
    public void Clear()
    {
        using var cr = new Cairo.Context(Surface);
        cr.Operator = Cairo.Operator.Clear;
        cr.Paint();
    }

    /// <summary>The alpha of the pixel at (<paramref name="x"/>, <paramref name="y"/>), of 255.</summary>
    public byte AlphaAt(int x, int y) => Pixels[(y * Stride) + (x * 4) + (BitConverter.IsLittleEndian ? 3 : 0)];

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
}
