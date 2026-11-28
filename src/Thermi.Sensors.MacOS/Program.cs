using System.CommandLine;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Sensors.MacOS.Native;
using Thermi.Sensors.MacOS.Reading;
using Thermi.Sensors.MacOS.Serving;
using Thermi.Sensors.MacOS.Source;

[assembly: SupportedOSPlatform("macos")]

namespace Thermi.Sensors.MacOS;

/// <summary>
/// The <c>thermi-sensors</c> service on macOS, which reads the SMC, the HID sensors and IOReport as the person: started by
/// launchd on its socket, it serves until no client came for a while; <c>--console</c> serves in a terminal, and
/// <c>--dump</c> and <c>--raw</c> record, for a bug report or tests. Arguments that make no sense say why and exit with 2,
/// before anything is read.
/// </summary>
internal static class Program
{
    // The name of the socket in the agent's plist, which launchd hands it under.
    private const string Listener = "Listener";

    private static Task<int> Main(string[] args) => RunAsync(args, TimeProvider.System, Console.Out, Console.Error, CancellationToken.None);

    /// <summary>What <c>thermi-sensors</c> does with <paramref name="args"/>; <paramref name="stop"/> ends serving in a terminal, as Ctrl+C does.</summary>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TimeProvider time, TextWriter output, TextWriter error, CancellationToken stop)
    {
        var request = SensorCommands.Parse(args, out var errors);
        if (request is null)
        {
            foreach (var said in errors)
            {
                await error.WriteLineAsync(said);
            }

            return 2;
        }

        if (request is SensorBuiltIn builtIn)
        {
            return await builtIn.Parsed.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, stop);
        }

        var replay = request switch
        {
            SensorRecord record => record.Replay,
            SensorServe serve => serve.Replay,
            _ => null,
        };
        if (replay is not null && !Directory.Exists(replay))
        {
            await error.WriteLineAsync($"{replay} is no recording: record one with thermi-sensors --raw.");
            return 2;
        }

        using var live = replay is null ? new LiveMac(time) : null;
        IMac mac;
        try
        {
            mac = live ?? (IMac)new RecordedMac(replay!);
        }
        catch (InvalidDataException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return 2;
        }

        return request is SensorRecord recording
            ? await RecordAsync(mac, recording, time, stop)
            : await ServeAsync(new MacSensors(mac), (SensorServe)request, time, output, error, stop);
    }

    // A dump names no address of the Mac's, as a recording does not, so a bug report shares none.
    private static async Task<int> RecordAsync(IMac mac, SensorRecord record, TimeProvider time, CancellationToken stop)
    {
        using var raw = record.Raw is null ? null : new RecordingMac(mac, record.Raw);
        var sensors = new MacSensors(raw ?? mac, scrubbed: true);
        await using var writer = record.Dump is null ? TextWriter.Null : new StreamWriter(record.Dump);
        await SeriesRecorder.RecordAsync(_ => Task.FromResult(sensors.Read()), writer, record.Seconds, time, stop);
        return 0;
    }

    // As launchd's agent on the socket it hands, ending once idle; in a terminal on the person's own, until Ctrl+C.
    private static async Task<int> ServeAsync(
        MacSensors sensors, SensorServe serve, TimeProvider time, TextWriter output, TextWriter error, CancellationToken stop)
    {
        var path = SnapshotJson.MacSocketPath();
#pragma warning disable CA2000 // Either way, the using disposes it.
        using var listener = serve.Console ? await OwnAsync(path, error, stop) : Handed();
#pragma warning restore CA2000
        if (listener is null)
        {
            if (!serve.Console)
            {
                await error.WriteLineAsync("thermi-sensors runs as launchd's agent, which hands it its socket; --console serves in this terminal.");
            }

            return 2;
        }

        using var broadcast = new SnapshotBroadcast(_ => Task.FromResult(sensors.Read()), time);
        var server = new SocketServer(listener, broadcast, time);
        if (!serve.Console)
        {
            await server.RunAsync(idles: true, stop);
            return 0;
        }

        using var serving = CancellationTokenSource.CreateLinkedTokenSource(stop);
        void Stop(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            serving.Cancel();
        }

        Console.CancelKeyPress += Stop;
        try
        {
            // Its first line says it listens, so whoever started it may connect.
            await output.WriteLineAsync($"thermi-sensors is serving readings on {path}. Ctrl+C stops it.");
            await output.FlushAsync(stop);
            await server.RunAsync(idles: false, serving.Token);
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= Stop;
        }
    }

    // launchd holds the agent's one socket, as its plist names it.
#pragma warning disable CA2000 // The socket owns the handle, and disposes it with itself.
    private static Socket? Handed() =>
        SystemLibrary.LaunchdSockets(Listener) is [var handle] ? new Socket(new SafeSocketHandle(handle, ownsHandle: true)) : null;
#pragma warning restore CA2000

    // The person's service may listen at the path already, as launchd's agent: a run in a terminal leaves its socket be.
    private static async Task<Socket?> OwnAsync(string path, TextWriter error, CancellationToken stop)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await probe.ConnectAsync(new UnixDomainSocketEndPoint(path), stop);
            await error.WriteLineAsync($"A sensor service serves on {path} already: thermi reads it.");
            return null;
        }
        catch (SocketException)
        {
            return SocketServer.Listen(path);
        }
    }
}
