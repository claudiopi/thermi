using System.Drawing.Imaging;
using System.Globalization;
using Thermi.Core;

namespace Thermi;

/// <summary>Composition root.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["preview", var snapshotPath, var png, ..])
        {
            var scale = 1f;
            return args.Length > 3 && !float.TryParse(args[3], CultureInfo.InvariantCulture, out scale) ? 2 : Preview(snapshotPath, png, scale);
        }

        using var single = new Mutex(initiallyOwned: true, @"Local\thermi-overlay", out var first);
        if (!first)
        {
            return 0;
        }

        var time = TimeProvider.System;
        var sensors = new SensorClient(time);
        using var stop = new CancellationTokenSource();
        var reading = Task.Run(() => sensors.RunAsync(stop.Token), stop.Token);
        using (var window = new OverlayWindow(new ConfigStore(ConfigStore.DefaultPath), sensors, CultureInfo.CurrentCulture, time))
        {
            window.Run();
        }

        await stop.CancelAsync();
        await reading;
        return 0;
    }

    private static int Preview(string snapshotPath, string png, float scale)
    {
        if (SnapshotJson.Read(File.ReadAllText(snapshotPath).ReplaceLineEndings("")) is not { } snapshot)
        {
            return 1;
        }

        var (config, _) = new ConfigStore(ConfigStore.DefaultPath).Load();
        var items = OverlayLayout.Build(config, SensorMap.Read(snapshot, config.Sources), CultureInfo.CurrentCulture);
        using var painter = new Painter();
        using var bitmap = painter.Paint(items, scale);
        bitmap.Save(png, ImageFormat.Png);
        return 0;
    }
}
