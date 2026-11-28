using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Thermi.App.Link;
using Thermi.App.Logging;
using Thermi.Cli.Commands;
using Thermi.Core.Messages;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;
using Thermi.Linux.Window;

namespace Thermi.Linux.Overlay.Link;

/// <summary>
/// The overlay's side of its link with the settings window on Linux's session bus (docs/adr/0009-overlay-and-window-messages.md):
/// App's <see cref="OverlayEnd"/> decides what is told, and this carries it. The window runs while its GTK application owns
/// its id on the bus, which GDBus watches, so asking costs no call; a message is Core's payload, sent to the window's
/// <see cref="WindowObject"/> with the desktop's token when it may come in front; and the window opens as a process of its
/// own, a second one handing its request to the first.
/// </summary>
internal sealed partial class WindowChannel : IWindowChannel, IDisposable
{
    // The window's last lines of its standard error its log keeps when it dies, and how long thermi waits for them past its end.
    private const int SaidLines = 40;
    private static readonly TimeSpan Drained = TimeSpan.FromSeconds(1);

    private readonly Gio.DBusConnection _bus;
    private readonly ILogger _log;
    private readonly Gio.DBusProxy _owner;

    public WindowChannel(Gio.DBusConnection bus, ILogger log)
    {
        (_bus, _log) = (bus, log);
        // A proxy's owner comes through GObject's notify, which GirCore marshals, where a name watch's callback it does not.
        const Gio.DBusProxyFlags Flags = Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotConnectSignals | Gio.DBusProxyFlags.DoNotAutoStart;
        _owner = Gio.DBusProxy.NewSync(bus, Flags, null, WindowProcess.Id, WindowObject.Path, WindowObject.Name, null);
        _owner.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "g-name-owner")
            {
                Changed();
            }
        };
    }

    /// <summary>The window opened or closed, on the main loop.</summary>
    public Action Changed { get; set; } = () => { };

    /// <summary>The desktop's token for a program shown on a click at a time of the desktop's, or now for 0, or empty.</summary>
    public Func<uint, Task<string>> Activation { get; init; } = _ => Task.FromResult("");

    /// <summary>Runs on the main loop's thread once it is free, from any thread.</summary>
    public Action<Action> Post { get; init; } = action => action();

    /// <summary>Whether thermi runs under <c>--trace</c>, which the settings window it starts then runs under too.</summary>
    public bool Traced { get; init; }

    public required TimeProvider Time { get; init; }

    /// <summary>thermi is opening the settings window, which ends its first start.</summary>
    public Action Opening { get; set; } = () => { };

    /// <summary>The settings window could not start, or ended with an error: the person is told.</summary>
    public Action Failed { get; set; } = () => { };

    /// <summary>The time of the desktop's click that a message or a window sent next answers, or 0 for the present.</summary>
    public uint Click { get; set; }

    /// <summary>A token the desktop gave another start of thermi, which the window opened for it shows with, in place of a click's.</summary>
    public string? Token { get; set; }

    public bool WindowOpen() => _owner.GetNameOwner() is not null;

    // A message the window may come in front for carries the desktop's token, which a person's click lets it take.
    public bool Send(WindowMessage message, bool front)
    {
        if (!WindowOpen())
        {
            return false;
        }

        var payload = Convert.ToBase64String(WindowMessages.Encode(message));
        _ = SendAsync(payload, front);
        return true;
    }

    public void Open(int overlay, string? item, bool sizeRow)
    {
        Opening();
        List<string> args = ["settings", ThermiCommandLine.OverlayOption, $"{overlay + 1}"];
        args.AddRange(item is null ? [] : [ThermiCommandLine.ItemOption, item]);
        args.AddRange(sizeRow ? [ThermiCommandLine.SizeOption] : []);

        // The window writes its events on its standard error, which thermi passes on as its own, so one reader hears both.
        if (Traced)
        {
            args.Add(ThermiCommandLine.TraceOption);
        }

        _ = WindowAsync(Environment.ProcessPath ?? "thermi", args);
    }

    /// <summary>Starts a program of the desktop's, as the system monitor, in front as a click on the menu lets it come.</summary>
    public void Start(string program) => _ = StartedAsync(program);

    public void Dispose() => _owner.Dispose();

    private async Task SendAsync(string payload, bool front)
    {
        try
        {
            var token = front ? await TokenAsync().ConfigureAwait(false) : "";
            using var args = GLib.Variant.NewTuple([GLib.Variant.NewString(payload), GLib.Variant.NewString(token)]);
            using var _ = await _bus.CallAsync(WindowProcess.Id, WindowObject.Path, WindowObject.Name, WindowObject.Message, args).ConfigureAwait(false);
        }
        catch (GLib.GException ex)
        {
            MessageNotTaken(_log, ex);
        }
    }

    // A window that dies, as it opens or later, would otherwise leave the person a click that did nothing. A second
    // request ends at once with 0, once the first window took it. One that dies in native code, as WebKit's sandbox
    // aborts it, logs nothing of its own: what it last wrote on its standard error goes into its log, which the notice
    // names.
    private async Task WindowAsync(string program, IEnumerable<string> args)
    {
        using var window = await StartAsync(program, args, isWindow: true).ConfigureAwait(false);
        if (window is null)
        {
            Post(Failed);
            return;
        }

        var said = new ConcurrentQueue<string>();
        var reading = PassOnAsync(window.StandardError, said);
        await window.WaitForExitAsync().ConfigureAwait(false);
        try
        {
            // What the window wrote last is in the pipe already, unless a process it started holds the pipe on.
            await reading.WaitAsync(Drained, Time).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            // What it said by then is enough to say why.
        }

        var code = window.ExitCode;
        if (code != 0)
        {
            _log.WindowEnded(code);
            InWindowLog(log => log.WindowDied(code, string.Join('\n', said)));
            Post(Failed);
        }
    }

    private async Task StartedAsync(string program)
    {
        using var started = await StartAsync(program, [], isWindow: false).ConfigureAwait(false);
    }

    // The program comes in front with the desktop's token for the click, and never with the one thermi was started with,
    // long spent. The window's standard error comes to thermi, which passes it on as its own.
    private async Task<Process?> StartAsync(string program, IEnumerable<string> args, bool isWindow)
    {
        var start = new ProcessStartInfo(program, args) { UseShellExecute = false, RedirectStandardError = isWindow };
        LaunchTokens.Set(start.Environment, await TokenAsync().ConfigureAwait(false));
        try
        {
            return Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.StartFailed(program, ex);
            if (isWindow)
            {
                InWindowLog(log => log.StartFailed(program, ex));
            }

            return null;
        }
    }

    // Each line on to thermi's own standard error, where the window's trace and GTK's warnings were always read, keeping
    // the last few.
    private static async Task PassOnAsync(StreamReader errors, ConcurrentQueue<string> said)
    {
        while (await errors.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            await Console.Error.WriteLineAsync(line).ConfigureAwait(false);
            said.Enqueue(line);
            while (said.Count > SaidLines && said.TryDequeue(out _))
            {
                // Only the last lines say why it ended.
            }
        }
    }

    // The window's own log, written by thermi once the window has ended, so the two never write it at once.
    private void InWindowLog(Action<ILogger> write)
    {
        using var logs = ThermiLog.Create(ThermiLog.WindowPath, Time, trace: false);
        write(logs.CreateLogger<WindowChannel>());
    }

    private async Task<string> TokenAsync()
    {
        if (Token is { } given)
        {
            Token = null;
            return given;
        }

        try
        {
            return await Activation(Click).ConfigureAwait(false);
        }
        catch (GLib.GException ex)
        {
            _log.ShellCallFailed(ex);
            return "";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings window did not take a message")]
    private static partial void MessageNotTaken(ILogger logger, Exception error);
}
