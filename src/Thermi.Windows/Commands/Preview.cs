using System.Drawing.Imaging;
using System.Globalization;
using Thermi.App.Preview;
using Thermi.Cli.Commands;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows.Painting;
using Thermi.Windows.Tray;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;
using Windows.Win32.System.Console;

namespace Thermi.Windows.Commands;

/// <summary>
/// <c>thermi preview</c> on Windows: <see cref="SnapshotPreview"/> painted with GDI+, on the desktop's taskbar and contrast;
/// an empty snapshot file paints "Sensors stopped", as the overlay shows it. A tray icon is painted at the notification
/// area's size.
/// </summary>
internal static class Preview
{
    public static async Task<int> RunAsync(PreviewRequest request, TimeProvider time)
    {
        var config = await SnapshotPreview.SettingsAsync(request.SettingsPath, time, ThermiPaths.RunsInstalled);
        if (await SnapshotPreview.ReadAsync(request.Snapshot, Said()) is not { } series)
        {
            return 2;
        }

        if (request.Icon is { } id)
        {
            if (SnapshotPreview.Icon(config, series, id, CpuWatch.ReadBaseClock(), Desktop.LightTaskbar(), Said()) is not { } icon)
            {
                return 2;
            }

            using var painted = TrayPainter.Paint(icon, TrayIcons.SizeAt((uint)Math.Round(96 * request.Scale)));
            return await WriteAsync(request.Png, png => painted.Save(png, ImageFormat.Png));
        }

        var overlay = config.Overlays[0];
        using var painter = new Painter();
        var shown = SnapshotPreview.Shown(config, series, CpuWatch.ReadBaseClock(), Desktop.LightTaskbar());
        var look = OverlayLook.Of(overlay.Style, Desktop.LightTaskbar(), Desktop.HighContrast());
        var scale = request.Scale * (float)overlay.Size;
        using var bitmap = shown.SensorsStopped
            ? painter.PaintStopped(Texts.For(config.Language, CultureInfo.CurrentUICulture).Of(OverlayText.SensorsStopped), look, scale, shown.Items)
            : painter.Paint(shown.Items, look, scale);
        return await WriteAsync(request.Png, png => bitmap.Save(png, ImageFormat.Png));
    }

    internal static IReadOnlyList<OverlayItem> Shown(ThermiConfig config, IReadOnlyList<Snapshot> series, double? baseClock) =>
        SnapshotPreview.Shown(config, series, baseClock, Desktop.LightTaskbar()).Items;

    /// <summary>Why goes to <paramref name="said"/>, the terminal's error output when none is given.</summary>
    internal static Task<int> WriteAsync(string path, Action<Stream> save, TextWriter? said = null) => SnapshotPreview.WriteAsync(path, save, said ?? Said());

    private static TextWriter Said()
    {
        CommandLineOutput.Borrow(STD_HANDLE.STD_ERROR_HANDLE);
        return Console.Error;
    }
}
