using System.Globalization;
using Thermi.Core.Settings;

namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// The Mac's desktop of a session made for tests, as a person uses it: the session's settings, where thermi reads them,
/// the real service replaying a recorded Mac on the session's socket, thermi started from its app under <c>--trace</c>,
/// what <c>thermi preview</c> paints for the same snapshot, and the main monitor as AppKit tells it.
/// </summary>
internal sealed class TestMac : IAsyncDisposable
{
    // The main monitor's frame and visible frame, in points from the top left, and its scale, as AppKit tells them.
    private const string Monitors = """
        ObjC.import('Cocoa');
        function run() {
          return $.NSScreen.screens.js.map(screen => {
            const top = $.NSScreen.screens.js[0].frame.size.height;
            const [f, v] = [screen.frame, screen.visibleFrame];
            return [f.origin.x, top - f.origin.y - f.size.height, f.size.width, f.size.height,
              v.origin.x, top - v.origin.y - v.size.height, v.size.width, v.size.height, screen.backingScaleFactor].join(' ');
          }).join('\n');
        }
        """;

    // Puts the second monitor on the other side of the main one, as the person drags it in System Settings' Arrange.
    private const string Swap = """
        ObjC.import('Cocoa');
        ObjC.import('CoreGraphics');
        function run() {
          const [main, other] = $.NSScreen.screens.js;
          const id = other.deviceDescription.objectForKey('NSScreenNumber').js;
          const x = other.frame.origin.x >= 0 ? -other.frame.size.width : main.frame.size.width;
          const config = Ref();
          $.CGBeginDisplayConfiguration(config);
          $.CGConfigureDisplayOrigin(config[0], id, x, other.frame.origin.y);
          return $.CGCompleteDisplayConfiguration(config[0], 2);
        }
        """;

    private TestMac(OwnSession session)
    {
        Session = session;
    }

    public OwnSession Session { get; }

    /// <summary>The settings file, under the session's <c>~/Library/Application Support/thermi</c>, as on every Mac.</summary>
    public string SettingsFile => Path.Combine(Session.Home, "Library", "Application Support", "thermi", "config.json");

    /// <summary>A session made for tests, with the service replaying <paramref name="recording"/> on its socket, or none.</summary>
    public static async Task<TestMac> StartAsync(string? recording, CancellationToken cancel)
    {
        var mac = new TestMac(OwnSession.OnScreen());
        if (recording is not null)
        {
            await SensorFeed.ServeAsync(mac.Session, recording, cancel);
        }

        return mac;
    }

    public Task WriteSettingsAsync(ThermiConfig config, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        return File.WriteAllTextAsync(SettingsFile, ThermiConfigJson.Write(config), cancel);
    }

    public async Task<ThermiConfig> ReadSettingsAsync(CancellationToken cancel)
    {
        var (config, problem) = ThermiConfigJson.Read(await File.ReadAllTextAsync(SettingsFile, cancel).ConfigureAwait(false));
        return problem is null ? config : throw new InvalidOperationException($"The settings did not read: {problem}");
    }

    /// <summary>thermi from its app, under <c>--trace</c>, once every overlay that is not hidden is on screen.</summary>
    public async Task<SessionProcess> ShowThermiAsync(IEnumerable<string> args, CancellationToken cancel)
    {
        var thermi = Session.Start(Built.Thermi, ["--trace", .. args]);
        await Traced.FirstAsync(thermi.Error, Traced.Shown, cancel).ConfigureAwait(false);
        return thermi;
    }

    /// <summary>The next second thermi paints, after what a test did.</summary>
    public static Task PaintedAsync(SessionProcess thermi, CancellationToken cancel) => Traced.NextAsync(thermi.Error, Traced.Paint, cancel);

    /// <summary>What <c>thermi preview</c> paints for the first snapshot of <paramref name="recording"/>, or for none, at <paramref name="scale"/>.</summary>
    public async Task<Picture> PreviewAsync(string? recording, double scale, CancellationToken cancel)
    {
        var snapshot = recording is null ? Path.Combine(Session.Temporary, "none.json") : Built.Recording(recording) + ".json";
        if (recording is null)
        {
            await File.WriteAllTextAsync(snapshot, """{"sensors":[]}""", cancel).ConfigureAwait(false);
        }

        var png = Path.Combine(Session.Temporary, $"preview-{Guid.NewGuid():N}.png");
        var args = new[] { "preview", snapshot, png, scale.ToString(CultureInfo.InvariantCulture), SettingsFile };
        var painted = await Session.RunAsync(Built.Thermi, args, cancel: cancel).ConfigureAwait(false);
        return painted.Exit == 0
            ? await Picture.ReadAsync(Session, png, cancel).ConfigureAwait(false)
            : throw new InvalidOperationException($"thermi preview painted nothing: {painted.Error}");
    }

    /// <summary>Every monitor, the main one first: its frame and visible frame, in points from the main one's top left, and its scale.</summary>
    public async Task<IReadOnlyList<MacMonitor>> MonitorsAsync(CancellationToken cancel)
    {
        var script = Path.Combine(Session.Temporary, "monitors.js");
        await File.WriteAllTextAsync(script, Monitors, cancel).ConfigureAwait(false);
        var told = await Session.RunAsync("osascript", ["-l", "JavaScript", script], cancel: cancel).ConfigureAwait(false);
        return [.. told.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(MacMonitor.Of)];
    }

    /// <summary>The second monitor moved to the other side of the main one.</summary>
    public async Task SwapMonitorsAsync(CancellationToken cancel)
    {
        var script = Path.Combine(Session.Temporary, "swap.js");
        await File.WriteAllTextAsync(script, Swap, cancel).ConfigureAwait(false);
        var swapped = await Session.RunAsync("osascript", ["-l", "JavaScript", script], cancel: cancel).ConfigureAwait(false);
        if (swapped.Out.Trim() != "0")
        {
            throw new InvalidOperationException($"The monitors did not move: {swapped.Out}{swapped.Error}");
        }
    }

    /// <summary>
    /// The Dock on <paramref name="side"/>, <c>bottom</c>, <c>left</c> or <c>right</c>, hidden or not, as the person sets it
    /// in System Settings.
    /// </summary>
    public async Task DockAsync(string side, bool hides, CancellationToken cancel)
    {
        await Session.RunAsync("defaults", ["write", "com.apple.dock", "orientation", "-string", side], cancel: cancel).ConfigureAwait(false);
        await Session.RunAsync("defaults", ["write", "com.apple.dock", "autohide", "-bool", hides ? "true" : "false"], cancel: cancel).ConfigureAwait(false);
        await Session.RunAsync("killall", ["Dock"], cancel: cancel).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

/// <summary>A monitor as AppKit tells it: in points, its whole frame, the part the menu bar and the Dock leave, and its scale.</summary>
internal sealed record MacMonitor(Area Frame, Area Visible, double Scale)
{
    public static MacMonitor Of(string line)
    {
        var n = line.Split(' ').Select(each => double.Parse(each, CultureInfo.InvariantCulture)).ToArray();
        return new MacMonitor(new((int)n[0], (int)n[1], (int)n[2], (int)n[3]), new((int)n[4], (int)n[5], (int)n[6], (int)n[7]), n[8]);
    }
}

/// <summary>A part of the screen, in points from the main monitor's top left.</summary>
internal readonly record struct Area(int X, int Y, int Width, int Height);
