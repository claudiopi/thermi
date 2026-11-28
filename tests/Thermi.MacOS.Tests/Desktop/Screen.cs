using System.Globalization;

namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// The screen of a session made for tests, as a person sees it: what is on it, read back through its capture; a window
/// of another app for thermi to show above, made by osascript's JavaScript with AppKit; and what the Dock and the app
/// switcher take an app for.
/// </summary>
internal static class Screen
{
    // A plain window of its own app, which says "shown" once it is in front, focused, and "click" for each click it gets;
    // full screen, it takes a Space of its own, as an app's full screen does.
    private const string Window = """
        ObjC.import('Cocoa');
        function run(argv) {
          const [x, y, width, height, full] = argv.map(Number);
          const app = $.NSApplication.sharedApplication;
          app.setActivationPolicy($.NSApplicationActivationPolicyRegular);
          ObjC.registerSubclass({ name: 'Clicked', superclass: 'NSView',
            methods: { 'mouseDown:': { types: ['void', ['id']], implementation: () => console.log('click') } } });
          const top = $.NSScreen.mainScreen.frame.size.height;
          const window = $.NSWindow.alloc.initWithContentRectStyleMaskBackingDefer($.NSMakeRect(x, top - y - height, width, height), 15, 2, false);
          window.contentView = $.Clicked.alloc.init;
          window.backgroundColor = $.NSColor.systemTealColor;
          window.makeKeyAndOrderFront(null);
          app.activateIgnoringOtherApps(true);
          if (full) { window.toggleFullScreen(null); }
          console.log('shown');
          app.run;
        }
        """;

    // The windows an app of a pid has on screen, front to back, each its level and bounds in points from the top left: the
    // window server's list, which says that much of every window with no permission.
    private const string Windows = """
        ObjC.import('CoreGraphics');
        function run([pid]) {
          const all = ObjC.deepUnwrap(ObjC.castRefToObject($.CGWindowListCopyWindowInfo(1, 0)));
          return all.filter(window => window.kCGWindowOwnerPID === Number(pid))
            .map(({ kCGWindowLayer: level, kCGWindowBounds: b }) => [level, b.X, b.Y, b.Width, b.Height].join(' ')).join('\n');
        }
        """;

    /// <summary>The menu bar's level, where its items' windows are, thermi's among them.</summary>
    public const int MenuBarLevel = 25;

    /// <summary>The windows the app of <paramref name="pid"/> has on screen, front to back.</summary>
    public static async Task<IReadOnlyList<(int Level, (int X, int Y, int Width, int Height) Bounds)>> WindowsAsync(
        OwnSession session, int pid, CancellationToken cancel)
    {
        var script = Path.Combine(session.Temporary, "windows.js");
        await File.WriteAllTextAsync(script, Windows, cancel).ConfigureAwait(false);
        var told = await session.RunAsync("osascript", ["-l", "JavaScript", script, $"{pid}"], cancel: cancel).ConfigureAwait(false);
        return [.. told.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ').Select(each => (int)double.Parse(each, CultureInfo.InvariantCulture)).ToArray())
            .Select(n => (n[0], (n[1], n[2], n[3], n[4])))];
    }

    /// <summary>What is on the screen in <paramref name="area"/>, in points from the top left, at the monitor's own scale.</summary>
    public static async Task<Picture> CaptureAsync(OwnSession session, (int X, int Y, int Width, int Height) area, CancellationToken cancel)
    {
        var file = Path.Combine(session.Temporary, $"screen-{Guid.NewGuid():N}.png");
        var region = string.Create(CultureInfo.InvariantCulture, $"{area.X},{area.Y},{area.Width},{area.Height}");
        var captured = await session.RunAsync("screencapture", ["-x", "-R", region, file], cancel: cancel).ConfigureAwait(false);
        return captured.Exit == 0
            ? await Picture.ReadAsync(session, file, cancel).ConfigureAwait(false)
            : throw new InvalidOperationException($"screencapture took nothing: {captured.Error}");
    }

    /// <summary>A window of another app at <paramref name="area"/>, in front and focused, or full screen, once it says so.</summary>
    public static async Task<SessionProcess> ShowWindowAsync(
        OwnSession session, (int X, int Y, int Width, int Height) area, bool fullScreen, CancellationToken cancel)
    {
        var script = Path.Combine(session.Temporary, "window.js");
        await File.WriteAllTextAsync(script, Window, cancel).ConfigureAwait(false);
        string[] args = [script, .. new[] { area.X, area.Y, area.Width, area.Height, fullScreen ? 1 : 0 }.Select(each => $"{each}")];
        var window = session.Start("osascript", ["-l", "JavaScript", .. args]);
        await window.Error.NextAsync(line => line == "shown", cancel).ConfigureAwait(false);
        return window;
    }

    /// <summary>The pid of the app in front, which has the keyboard's focus, as <c>lsappinfo</c> says it.</summary>
    public static async Task<int> FrontAsync(OwnSession session, CancellationToken cancel)
    {
        var front = (await session.RunAsync("lsappinfo", ["front"], cancel: cancel).ConfigureAwait(false)).Out.Trim();
        var info = await session.RunAsync("lsappinfo", ["info", "-only", "pid", front], cancel: cancel).ConfigureAwait(false);
        return info.Out.Split('=', 2) is [_, var pid] ? int.Parse(pid, CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>Once an app of <paramref name="bundle"/> runs, as Launch Services knows it, within a line's bound; false past it.</summary>
    public static async Task<bool> RunsAsync(OwnSession session, string bundle, CancellationToken cancel)
    {
        // Launch Services tells a program of plain .NET of no app's start, so the shell looks every tenth of a second, for a line's bound.
        string[] wait = ["-c", $"for i in $(seq {Bound.LineSeconds}0); do lsappinfo find bundleid={bundle} | grep -q . && exit 0; sleep 0.1; done; exit 1"];
        return (await session.RunAsync("/bin/sh", wait, cancel: cancel).ConfigureAwait(false)).Exit == 0;
    }

    /// <summary>
    /// What macOS takes the app of <paramref name="pid"/> for, as <c>lsappinfo</c> says it: <c>UIElement</c> for an agent
    /// app, off the Dock and the app switcher, and <c>Foreground</c> for one in both.
    /// </summary>
    public static async Task<string> AppKindAsync(OwnSession session, int pid, CancellationToken cancel)
    {
        var info = await session.RunAsync("lsappinfo", ["info", "-only", "ApplicationType", $"{pid}"], cancel: cancel).ConfigureAwait(false);
        return info.Out.Split('=', 2) is [_, var kind] ? kind.Trim().Trim('"') : throw new InvalidOperationException($"lsappinfo knows no app {pid}.");
    }
}
