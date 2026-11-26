using System.Drawing;
using System.Drawing.Imaging;
using Thermi.Core.Look;
using Thermi.Core.Tray;
using Thermi.Windows.Tray;

namespace Thermi.Windows.Tests;

/// <summary>thermi's mark as the program's icon and the README's logo, each file drawn by the tray's painter.</summary>
public sealed class ProgramIconTests
{
    // Set to 1, the test draws the icon and the logos into their files first: scripts/mark-icon.ps1.
    private const string IconWriting = "THERMI_WRITE_ICON";

    // Every size Windows shows a program's icon at, from 100 % to 250 % and Explorer's largest view.
    private static readonly int[] ProgramIconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    // The line under the mark sits on light and dark alike, in Explorer, Start and the taskbar, so it is a middle gray.
    private static readonly Rgb ProgramIconInk = Rgb.Parse("#8A8A8A");

    // The README's logo, with its line in GitHub's text color for each theme.
    private static readonly (string File, Rgb Ink)[] Logos = [("thermi-light.png", Rgb.Parse("#1F2328")), ("thermi-dark.png", Rgb.Parse("#F0F6FC"))];

    // The program's icon is the mark in its app form, drawn by the tray's painter at each size Windows asks of an icon,
    // hand-placed pixels included, and so is the README's logo; `pwsh scripts/mark-icon.ps1` draws them again after the
    // mark changes. Another Windows may smooth the vector's edges a little differently, so a channel may differ by a little.
    [Fact]
    public void TheProgramsIconIsTheMark()
    {
        var file = Path.Combine(TestRecordings.RepoRoot(), "src", "Thermi.Windows", "thermi.ico");
        var painted = ProgramIconSizes.Select(size => Mark(ProgramIconInk, size)).ToList();
        var logos = Logos.Select(logo => (Path: Path.Combine(TestRecordings.RepoRoot(), "docs", "images", logo.File), Picture: Mark(logo.Ink, 256))).ToList();
        if (Environment.GetEnvironmentVariable(IconWriting) == "1")
        {
            File.WriteAllBytes(file, IconFile(painted));
            logos.ForEach(logo => logo.Picture.Save(logo.Path, ImageFormat.Png));
        }

        var icon = File.ReadAllBytes(file);
        Assert.Equal(ProgramIconSizes.Length, BitConverter.ToUInt16(icon, 4));
        foreach (var (picture, at) in painted.Select((picture, at) => (picture, at)))
        {
            var entry = 6 + (16 * at);
            using var stream = new MemoryStream(icon, BitConverter.ToInt32(icon, entry + 12), BitConverter.ToInt32(icon, entry + 8));
            using var frame = new Bitmap(stream);
            AssertSame(picture, frame);
        }

        foreach (var (path, picture) in logos)
        {
            using var stored = new Bitmap(path);
            AssertSame(picture, stored);
        }

        painted.Concat(logos.Select(logo => logo.Picture)).ToList().ForEach(picture => picture.Dispose());
    }

    private static Bitmap Mark(Rgb ink, int size) => TrayPainter.Paint(new TrayIcon(Guid.Empty, null, null, ink, "") { Mark = MarkState.App }, size);

    private static void AssertSame(Bitmap painted, Bitmap stored)
    {
        Assert.Equal(painted.Size, stored.Size);
        var far = Enumerable.Range(0, stored.Height)
            .Sum(y => Enumerable.Range(0, stored.Width).Count(x => Differs(painted.GetPixel(x, y), stored.GetPixel(x, y))));
        Assert.True(far == 0, $"{far} pixels of the {painted.Width} px mark are not the mark's");
    }

    // A faint edge's color says little, so colors are compared where both pixels are at least half opaque.
    private static bool Differs(Color painted, Color stored)
    {
        const int Near = 24;
        var color = new[] { painted.R - stored.R, painted.G - stored.G, painted.B - stored.B }.Max(Math.Abs);
        return Math.Abs(painted.A - stored.A) > Near || (Math.Min(painted.A, stored.A) >= 128 && color > Near);
    }

    // An .ico of PNG frames, which Windows reads at every size since Vista: the header, one entry per frame, then the frames.
    private static byte[] IconFile(List<Bitmap> pictures)
    {
        var frames = pictures.Select(picture =>
        {
            using var png = new MemoryStream();
            picture.Save(png, ImageFormat.Png);
            return png.ToArray();
        }).ToList();
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);
        var offset = 6 + (16 * frames.Count);
        foreach (var (picture, frame) in pictures.Zip(frames))
        {
            // A side of 256 is written as 0, the byte's only way to say it.
            writer.Write((byte)(picture.Width % 256));
            writer.Write((byte)(picture.Height % 256));
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frame.Length);
            writer.Write(offset);
            offset += frame.Length;
        }

        frames.ForEach(writer.Write);
        writer.Flush();
        return file.ToArray();
    }
}
