using Thermi.App.Preview;
using Thermi.Cli.Commands;
using Thermi.Core.Look;
using Thermi.Linux.Desktop;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Commands;

/// <summary><c>thermi preview</c> on Linux: <see cref="SnapshotPreview"/> painted with Cairo and Pango, in the desktop's contrast.</summary>
internal static class Preview
{
    public static async Task<int> RunAsync(PreviewRequest request, TimeProvider time)
    {
        var config = await SnapshotPreview.SettingsAsync(request.SettingsPath, time, DesktopLooks.RunsInstalled);
        if (await SnapshotPreview.ReadAsync(request.Snapshot, Console.Error) is not { Count: > 0 } series)
        {
            return 2;
        }

        var overlay = config.Overlays[0];
        using var painter = new Painter();
        using var picture = painter.Paint(
            SnapshotPreview.Shown(config, series, DesktopLooks.BaseClock(), DesktopLooks.LightPanels),
            OverlayLook.Of(overlay.Style, DesktopLooks.LightPanels, DesktopLooks.HighContrast()),
            request.Scale * overlay.Size);
        var bytes = picture.Png();
        return await SnapshotPreview.WriteAsync(request.Png, png => png.Write(bytes), Console.Error);
    }
}
