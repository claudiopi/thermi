using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Painting;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;
using Windows.Win32.System.Console;

namespace Thermi.Windows.Commands;

/// <summary>
/// <c>thermi preview</c>: the first overlay painted for a saved snapshot into a PNG, as the overlay would draw it, and
/// <c>thermi-cli preview</c>'s picture of any overlay. A snapshot that is none exits with 2, as any argument that makes no
/// sense; a picture Windows would not write exits with 1. Either says why on stderr.
/// </summary>
internal static class Preview
{
    // The settings file is the user's unless another is named, so a preview can show any style without touching theirs.
    public static async Task<int> RunAsync(PreviewRequest request, TimeProvider time)
    {
        ThermiConfig config;
        using (var store = new ConfigStore(request.SettingsPath ?? ConfigStore.DefaultPath, time, ThermiPaths.RunsInstalled))
        {
            config = (await store.ReadOnlyAsync()).Config;
        }

        if (await ReadAsync(request.Snapshot) is not { Count: > 0 } series)
        {
            return 2;
        }

        using var bitmap = Painted(config, 0, series, request.Scale);
        return await WriteAsync(request.Png, png => bitmap.Save(png, ImageFormat.Png));
    }

    /// <summary>An agent's picture: <paramref name="overlay"/> painted for one snapshot, as a PNG file holds it.</summary>
    public static OverlayPng Png(ThermiConfig config, int overlay, Snapshot snapshot, float scale)
    {
        using var bitmap = Painted(config, overlay, [snapshot], scale);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return new OverlayPng(png.ToArray(), bitmap.Width, bitmap.Height);
    }

    // In the light or dark and the high contrast the desktop has now, as the overlay paints itself.
    private static Bitmap Painted(ThermiConfig config, int overlay, IReadOnlyList<Snapshot> series, float scale)
    {
        var one = config.Overlays[overlay];
        using var painter = new Painter();
        return painter.Paint(
            Shown(config, series, CpuWatch.ReadBaseClock(), overlay),
            OverlayLook.Of(one.Style, Desktop.LightTaskbar(), Desktop.HighContrast()),
            scale * (float)one.Size);
    }

    /// <summary>
    /// An overlay's items, the first's when none is named, for the last snapshot of a series, a second each, as the overlay
    /// shows them after it read them all: its graphs drawing the series, the driver's marker and throttling included.
    /// </summary>
    internal static IReadOnlyList<OverlayItem> Shown(ThermiConfig config, IReadOnlyList<Snapshot> series, double? baseClock, int overlay = 0)
    {
        var clock = new SecondsClock();
        var history = new ItemHistory(clock);
        for (var second = 0; second < series.Count; second++)
        {
            clock.Second = second;
            history.Add([.. config.Overlays.Select(overlay => (overlay, SensorMap.Read(series[second], overlay)))]);
        }

        var texts = Texts.For(config.Language, CultureInfo.CurrentUICulture);
        var input = new TrayInput(series[^1], ServiceState.Running, CultureInfo.CurrentCulture, texts, Desktop.LightTaskbar(), "")
        {
            BaseClock = baseClock,
            History = history,
        };
        return ThermiMark.Overlay(config, overlay, input, windowOpenedOnce: true).Items;
    }

    /// <summary>
    /// Writes the picture through a file of its own, so a folder that is not there is named as such, not as GDI+'s generic
    /// error; a picture that fails midway leaves no file, since a partial PNG would pass for one. Why goes to <paramref name="said"/>,
    /// the terminal's error output when none is given.
    /// </summary>
    internal static async Task<int> WriteAsync(string path, Action<Stream> save, TextWriter? said = null)
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

            var reason = $"{path} could not be written: {ex.Message}";
            await (said is null ? SayAsync(reason) : said.WriteLineAsync(reason));
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

    private static async Task<IReadOnlyList<Snapshot>?> ReadAsync(string path)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path);
            if (SnapshotJson.Read(text.ReplaceLineEndings("")) is { } snapshot)
            {
                return [snapshot];
            }

            var series = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(SnapshotJson.Read).ToList();
            if (series.Count > 0 && series.All(each => each is not null))
            {
                return [.. series.OfType<Snapshot>()];
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

    // A saved series is read a second a snapshot, however fast it is replayed.
    private sealed class SecondsClock : TimeProvider
    {
        public int Second { get; set; }

        public override long TimestampFrequency => 1;

        public override long GetTimestamp() => Second;
    }
}
