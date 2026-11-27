using System.Globalization;
using Thermi.App.Preview;
using Thermi.Cli.Commands;
using Thermi.Core.Look;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Commands;

/// <summary>
/// <c>thermi preview</c> on Linux: <see cref="SnapshotPreview"/> painted with Cairo and Pango, in the desktop's contrast;
/// an empty snapshot file paints "Sensors stopped", as the overlay shows it.
/// </summary>
internal static class Preview
{
    public static async Task<int> RunAsync(PreviewRequest request, TimeProvider time)
    {
        var config = await SnapshotPreview.SettingsAsync(request.SettingsPath, time, DesktopLooks.RunsInstalled);
        if (await SnapshotPreview.ReadAsync(request.Snapshot, Console.Error) is not { } series)
        {
            return 2;
        }

        var overlay = config.Overlays[0];
        using var painter = new Painter();
        var shown = SnapshotPreview.Shown(config, series, DesktopLooks.BaseClock(), DesktopLooks.LightPanels);
        var look = OverlayLook.Of(overlay.Style, DesktopLooks.LightPanels, DesktopLooks.HighContrast());
        var scale = request.Scale * overlay.Size;
        using var picture = shown.SensorsStopped
            ? painter.PaintStopped(Texts.For(config.Language, CultureInfo.CurrentUICulture).Of(OverlayText.SensorsStopped), look, scale, shown.Items)
            : painter.Paint(shown.Items, look, scale);
        var bytes = picture.Png();
        return await SnapshotPreview.WriteAsync(request.Png, png => png.Write(bytes), Console.Error);
    }
}
