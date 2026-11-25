extern alias sensors;

using System.IO.Pipes;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using Thermi.Agents;
using Thermi.Commands;
using Thermi.Core.Agents;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Thermi.Ui;
using Thermi.Windows.Tests.SensorHost;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// Agents beside the overlay on the service's one pipe (<c>docs/adr/0013-agents-cli-and-mcp.md</c>): a call holds its
/// connection only while it reads, so idle MCP servers take no slot, and three watches leave room for everyone else.
/// </summary>
public sealed class AgentPipeTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly Snapshot Sent = new([new RawSensor("/amdcpu/0/load/0", "Cpu", "AMD Ryzen 9 9950X", "Load", "CPU Total", 40)]);

    private readonly string _pipe = $"thermi-test-{Guid.NewGuid():N}";
    private readonly FakeTimeProvider _clock = new();
    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AgentsNeverHoldThePipe()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        using var stop = new CancellationTokenSource();
        using var server = new SensorPipeServer(_ => Task.FromResult(Sent), _clock, _pipe) { Hung = () => Assert.Fail("No read should hang.") };
        var serving = Task.Run(() => server.RunAsync(stop.Token), CancellationToken.None);
        var ticking = Tick(stop.Token);
        var environment = new AgentEnvironment(
            _folder.File,
            () => new SystemFeed(new SensorClient(_clock, NullLogger.Instance, new FixedPipe(_pipe, _ => true)), _clock),
            new StartWithWindows(@"Software\thermi-tests\unused", @"Software\thermi-tests\unused", "thermi.exe"),
            NullLogger.Instance);
        var clients = new List<NamedPipeClientStream>();
        try
        {
            // Four MCP servers' commands, each after its read; then a read of 2 s, which hangs up as it answers.
            var idle = Enumerable.Range(0, 4).Select(_ => new AgentCommands(environment, _clock)).ToList();
            foreach (var commands in idle)
            {
                Assert.Equal(AgentExit.Done, (await commands.RunAsync(new AgentRequest(AgentVerb.Read), stop.Token)).Exit);
            }

            var sampled = await idle[0].RunAsync(new AgentRequest(AgentVerb.Read) { Ids = ["cpu.load"], Seconds = 2 }, stop.Token);
            Assert.Equal(2, (int)JsonNode.Parse(sampled.Json!)!["values"]![0]!["samples"]!);

            // Three watches, each a line a second until its stdin closes.
            var watches = Enumerable.Range(0, 3).Select(_ => new Watch()).ToList();
            var watching = watches.Select(watch => AgentRun.RunAsync(
                new AgentRequest(AgentVerb.Watch) { Ids = ["cpu.load"] },
                environment,
                new AgentConsole(watch.Read, watch.Write, TextWriter.Null),
                NullLoggerFactory.Instance,
                _clock)).ToList();
            foreach (var watch in watches)
            {
                for (var line = 0; line < 2; line++)
                {
                    var said = await watch.Lines.ReadLineAsync(TestContext.Current.CancellationToken);
                    Assert.Equal(AgentSchema.Current, (int)JsonNode.Parse(said!)!["schema"]!);
                }
            }

            // The overlay, the window and three more: with the watches, the session's whole share, each of them reads.
            for (var each = 0; each < 5; each++)
            {
                var client = new NamedPipeClientStream(".", _pipe, PipeDirection.In, PipeOptions.Asynchronous);
                clients.Add(client);
                await client.ConnectAsync(Patience, TestContext.Current.CancellationToken);
                Assert.Equal(Sent.Sensors, (await PipeReading.NextSnapshotAsync(client, _clock, Patience)).Sensors);
            }

            watches.ForEach(watch => watch.Input.Dispose());
            var ended = await Task.WhenAll(watching).WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
            Assert.Equal([0, 0, 0], ended);
            watches.ForEach(watch => watch.Dispose());
            idle.ForEach(commands => commands.Dispose());
        }
        finally
        {
            clients.ForEach(client => client.Dispose());
            await stop.CancelAsync();
            await serving.WaitAsync(Patience, TimeProvider.System, CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            await ticking.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    // The service writes once a second on the fake clock, which runs ten times as fast as a person's.
    private async Task Tick(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(TimeSpan.FromMilliseconds(10), TimeProvider.System, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>A watch's stdin, which the test closes, and its stdout, which the test reads line by line.</summary>
    private sealed class Watch : IDisposable
    {
        public Watch()
        {
            Read = new AnonymousPipeClientStream(PipeDirection.In, Input.ClientSafePipeHandle);
            Write = new AnonymousPipeClientStream(PipeDirection.Out, Output.ClientSafePipeHandle);
            Lines = new StreamReader(Output);
        }

        public AnonymousPipeServerStream Input { get; } = new(PipeDirection.Out);

        public AnonymousPipeClientStream Read { get; }

        public AnonymousPipeServerStream Output { get; } = new(PipeDirection.In);

        public AnonymousPipeClientStream Write { get; }

        public StreamReader Lines { get; }

        public void Dispose()
        {
            Input.Dispose();
            Read.Dispose();
            Write.Dispose();
            Lines.Dispose();
        }
    }
}
