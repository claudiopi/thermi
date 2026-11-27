using System.CommandLine;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;
using Thermi.Sensors.Linux.Reading;
using Thermi.Sensors.Linux.Serving;

[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Sensors.Linux;

/// <summary>
/// The <c>thermi-sensors</c> service on Linux, which reads <c>hwmon</c>, NVML, <c>/proc</c> and <c>/sys</c>: started by
/// systemd on its socket, it serves until no client came for a while; <c>--console</c> serves in a terminal, and
/// <c>--dump</c> and <c>--tree</c> record, for a bug report or tests. Arguments that make no sense say why and exit with 2,
/// before anything is read.
/// </summary>
internal static class Program
{
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

        var root = request switch
        {
            SensorRecord record => record.Root,
            SensorServe serve => serve.Root,
            _ => null,
        };
        if (root is not null && !Directory.Exists(root))
        {
            await error.WriteLineAsync($"{root} is no tree: record one with thermi-sensors --tree.");
            return 2;
        }

        using var live = root is null ? new LiveKernel(time) : null;
        IKernel kernel = live ?? (IKernel)new RecordedKernel(root!);
        return request is SensorRecord recording
            ? await RecordAsync(kernel, recording, time, stop)
            : await ServeAsync(new LinuxSensors(kernel), (SensorServe)request, time, output, error, stop);
    }

    // A dump names no address of the PC's, as a tree does not, so a bug report shares none.
    private static async Task<int> RecordAsync(IKernel kernel, SensorRecord record, TimeProvider time, CancellationToken stop)
    {
        using var tree = record.Tree is null ? null : new RecordingKernel(kernel, record.Tree);
        var sensors = new LinuxSensors(tree ?? kernel, scrubbed: true);
        await using var writer = record.Dump is null ? TextWriter.Null : new StreamWriter(record.Dump);
        await SeriesRecorder.RecordAsync(_ => Task.FromResult(sensors.Read()), writer, record.Seconds, time, stop);
        return 0;
    }

    // As systemd's service on the socket it hands, ending once idle; in a terminal on the user's own, until Ctrl+C.
    private static async Task<int> ServeAsync(
        LinuxSensors sensors, SensorServe serve, TimeProvider time, TextWriter output, TextWriter error, CancellationToken stop)
    {
        using var listener = serve.Console ? Own() : SocketServer.FromSystemd();
        if (listener is null)
        {
            await error.WriteLineAsync(serve.Console
                ? "There is no runtime folder to put a socket in: XDG_RUNTIME_DIR is not set."
                : "thermi-sensors runs as systemd's service, which hands it its socket; --console serves in this terminal.");
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
            await output.WriteLineAsync($"thermi-sensors is serving readings on {listener.LocalEndPoint}. Ctrl+C stops it.");
            await output.FlushAsync(stop);
            await server.RunAsync(idles: false, serving.Token);
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= Stop;
        }
    }

    private static Socket? Own() =>
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
            ? SocketServer.Listen(Path.Combine(runtime, SnapshotJson.RuntimeSocket))
            : null;
}
