using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Painting;
using Thermi.Windows;
using Windows.Win32.System.Console;

namespace Thermi.Commands;

/// <summary>
/// <c>thermi preview</c>: the first overlay painted for a saved snapshot into a PNG, as the overlay would draw it. A
/// snapshot that is none exits with 2, as any argument that makes no sense; a picture Windows would not write exits with 1.
/// Either says why on stderr.
/// </summary>
internal static class Preview
{
    // The settings file is the user's unless another is named, so a preview can show any style without touching theirs.
    public static async Task<int> RunAsync(PreviewRequest request, TimeProvider time)
    {
        ThermiConfig config;
        using (var store = new ConfigStore(request.SettingsPath ?? ConfigStore.DefaultPath, time))
        {
            config = (await store.ReadOnlyAsync()).Config;
        }

        if (await ReadAsync(request.Snapshot) is not { } snapshot)
        {
            return 2;
        }

        var overlay = config.Overlays[0];
        using var painter = new Painter();
        using var bitmap = painter.Paint(
            Shown(config, snapshot, CpuWatch.ReadBaseClock()),
            OverlayLook.Of(overlay.Style, Desktop.LightTaskbar(), Desktop.HighContrast()),
            request.Scale * (float)overlay.Size);
        return await WriteAsync(request.Png, png => bitmap.Save(png, ImageFormat.Png));
    }

    /// <summary>The first overlay's items for the snapshot, as the overlay shows them, the driver's marker and throttling included.</summary>
    internal static IReadOnlyList<OverlayItem> Shown(ThermiConfig config, Snapshot snapshot, double? baseClock)
    {
        var texts = Texts.For(config.Language, CultureInfo.CurrentUICulture);
        var input = new TrayInput(snapshot, ServiceState.Running, CultureInfo.CurrentCulture, texts, Desktop.LightTaskbar(), "") { BaseClock = baseClock };
        return ThermiMark.Overlay(config, 0, input, windowOpenedOnce: true).Items;
    }

    /// <summary>
    /// Writes the picture through a file of its own, so a folder that is not there is named as such, not as GDI+'s generic
    /// error; a picture that fails midway leaves no file, since a partial PNG would pass for one.
    /// </summary>
    internal static async Task<int> WriteAsync(string path, Action<Stream> save)
    {
        var created = false;
        try
        {
            await using var png = File.Create(path);
            created = true;
            save(png);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            if (created)
            {
                Delete(path);
            }

            await SayAsync($"{path} could not be written: {ex.Message}");
            return 1;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left where it is: why it is no whole picture is said anyway.
        }
    }

    private static async Task<Snapshot?> ReadAsync(string path)
    {
        try
        {
            if (SnapshotJson.Read((await File.ReadAllTextAsync(path)).ReplaceLineEndings("")) is { } snapshot)
            {
                return snapshot;
            }

            await SayAsync($"{path} is not a snapshot saved with thermi-sensors --dump.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await SayAsync($"{path} could not be read: {ex.Message}");
        }

        return null;
    }

    private static Task SayAsync(string reason)
    {
        CommandLineOutput.Borrow(STD_HANDLE.STD_ERROR_HANDLE);
        return Console.Error.WriteLineAsync(reason);
    }
}
