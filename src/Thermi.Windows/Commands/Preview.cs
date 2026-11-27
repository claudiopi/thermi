using System.Drawing.Imaging;
using Thermi.App.Preview;
using Thermi.Cli.Commands;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Painting;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;
using Windows.Win32.System.Console;

namespace Thermi.Windows.Commands;

/// <summary><c>thermi preview</c> on Windows: <see cref="SnapshotPreview"/> painted with GDI+, on the desktop's taskbar and contrast.</summary>
internal static class Preview
{
    public static async Task<int> RunAsync(PreviewRequest request, TimeProvider time)
    {
        var config = await SnapshotPreview.SettingsAsync(request.SettingsPath, time, ThermiPaths.RunsInstalled);
        if (await SnapshotPreview.ReadAsync(request.Snapshot, Said()) is not { Count: > 0 } series)
        {
            return 2;
        }

        var overlay = config.Overlays[0];
        using var painter = new Painter();
        using var bitmap = painter.Paint(
            Shown(config, series, CpuWatch.ReadBaseClock()),
            OverlayLook.Of(overlay.Style, Desktop.LightTaskbar(), Desktop.HighContrast()),
            request.Scale * (float)overlay.Size);
        return await WriteAsync(request.Png, png => bitmap.Save(png, ImageFormat.Png));
    }

    internal static IReadOnlyList<OverlayItem> Shown(ThermiConfig config, IReadOnlyList<Snapshot> series, double? baseClock) =>
        SnapshotPreview.Shown(config, series, baseClock, Desktop.LightTaskbar());

    /// <summary>Why goes to <paramref name="said"/>, the terminal's error output when none is given.</summary>
    internal static Task<int> WriteAsync(string path, Action<Stream> save, TextWriter? said = null) => SnapshotPreview.WriteAsync(path, save, said ?? Said());

    private static TextWriter Said()
    {
        CommandLineOutput.Borrow(STD_HANDLE.STD_ERROR_HANDLE);
        return Console.Error;
    }
}
