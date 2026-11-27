using System.Globalization;
using System.Runtime.InteropServices;
using Thermi.App.SettingsFile;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.App.Preview;

/// <summary>
/// What <c>thermi preview</c> does on every OS around its painter: the settings and the snapshots it paints, the items
/// the overlay would show for them, and the PNG written whole or not at all. A snapshot that is none exits with 2, as any
/// argument that makes no sense; a picture that could not be written exits with 1. Either says why on <c>said</c>.
/// </summary>
internal static class SnapshotPreview
{
    /// <summary>The settings, the user's unless another file is named, so a preview can show any style without touching theirs.</summary>
    public static async Task<ThermiConfig> SettingsAsync(string? path, TimeProvider time, Func<bool> installed)
    {
        using var store = new ConfigStore(path ?? ConfigStore.DefaultPath, time, installed);
        return (await store.ReadOnlyAsync()).Config;
    }

    /// <summary>
    /// The first overlay as it shows after it read a series, a second each: its graphs drawing the series, the driver's
    /// marker and throttling included. No snapshot at all is the service stopped.
    /// </summary>
    public static OverlayScene Shown(ThermiConfig config, IReadOnlyList<Snapshot> series, double? baseClock, bool lightTaskbar)
    {
        var clock = new SecondsClock();
        var history = new ItemHistory(clock);
        for (var second = 0; second < series.Count; second++)
        {
            clock.Second = second;
            history.Add([.. config.Overlays.Select(overlay => (overlay, SensorMap.Read(series[second], overlay)))]);
        }

        var texts = Texts.For(config.Language, CultureInfo.CurrentUICulture);
        var input = new TrayInput(
            series.Count > 0 ? series[^1] : null,
            series.Count > 0 ? ServiceState.Running : ServiceState.Stopped,
            CultureInfo.CurrentCulture,
            texts,
            lightTaskbar,
            "")
        {
            BaseClock = baseClock,
            History = history,
        };
        return ThermiMark.Overlay(config, 0, input, windowOpenedOnce: true);
    }

    /// <summary>
    /// A snapshot saved with <c>thermi-sensors --dump</c>, or a series of them a line each, or none in an empty file, for
    /// the sensors stopped; null, with why, otherwise.
    /// </summary>
    public static async Task<IReadOnlyList<Snapshot>?> ReadAsync(string path, TextWriter said)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            if (SnapshotJson.Read(text.ReplaceLineEndings("")) is { } snapshot)
            {
                return [snapshot];
            }

            var series = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(SnapshotJson.Read).ToList();
            if (series.Count > 0 && series.All(each => each is not null))
            {
                return [.. series.OfType<Snapshot>()];
            }

            await said.WriteLineAsync($"{path} is not a snapshot saved with thermi-sensors --dump.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await said.WriteLineAsync($"{path} could not be read: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Writes the picture through a file of its own, so a folder that is not there is named as such, not as the painter's
    /// generic error; a picture that fails midway leaves no file, since a partial PNG would pass for one.
    /// </summary>
    public static async Task<int> WriteAsync(string path, Action<Stream> save, TextWriter said)
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

            await said.WriteLineAsync($"{path} could not be written: {ex.Message}");
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

    // A saved series is read a second a snapshot, however fast it is replayed.
    private sealed class SecondsClock : TimeProvider
    {
        public int Second { get; set; }

        public override long TimestampFrequency => 1;

        public override long GetTimestamp() => Second;
    }
}
