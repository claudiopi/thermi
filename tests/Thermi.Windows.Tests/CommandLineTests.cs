extern alias sensors;

using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using Thermi.Commands;

namespace Thermi.Windows.Tests;

/// <summary>
/// What <c>thermi</c> and <c>thermi-sensors</c> make of their arguments, read before anything runs: arguments that make
/// no sense are null, which exits with 2.
/// </summary>
public sealed class CommandLineTests : IDisposable
{
    // A snapshot that exists, since a preview refuses one that does not.
    private static readonly string Snapshot = TestRecordings.Path(TestRecordings.Admin);

    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData(new string[0], 0)]
    [InlineData(new[] { "--overlay", "2" }, 2)]
    [InlineData(new[] { "--overlay", "9" }, 9)]
    [InlineData(new[] { "--overlay", "0" }, null)]
    [InlineData(new[] { "--overlay", "-1" }, null)]
    [InlineData(new[] { "--overlay", "two" }, null)]
    [InlineData(new[] { "--overlay" }, null)]
    [InlineData(new[] { "--other" }, null)]
    [InlineData(new[] { "--overlay", "2", "extra" }, null)]
    public void SettingsTakesAnOverlayCountedFromOne(string[] args, int? overlay)
    {
        var request = ThermiCommands.Parse(["settings", .. args], out var errors);

        Assert.Equal(overlay, (request as SettingsRequest)?.Overlay);
        Assert.Equal(overlay is null, request is null);
        Assert.Equal(overlay is null, errors.Count > 0);
    }

    // Without --trace the debug events are left out; the option goes before or after the command.
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--trace" }, true)]
    public void TheOverlayRunsWithNoCommand(string[] args, bool trace)
    {
        Assert.Equal(new OverlayRequest(trace), ThermiCommands.Parse(args, out _));
        Assert.Equal(new SettingsRequest(2, trace, Pseudo: false), ThermiCommands.Parse(["settings", .. args, "--overlay", "2"], out _));
        Assert.Equal(new SettingsRequest(0, trace, Pseudo: false), ThermiCommands.Parse([.. args, "settings"], out _));
    }

    // A word thermi does not know is an error, never an overlay started by mistake: the old command line ignored it. The
    // elevated mode takes its verb and nothing else, not even an option every command takes, so whoever starts it cannot
    // steer it.
    [Theory]
    [InlineData(false, "show")]
    [InlineData(false, "--tracing")]
    [InlineData(false, "elevated")]
    [InlineData(false, "elevated", "other")]
    [InlineData(false, "elevated", StartSensors.Verb, "extra")]
    [InlineData(false, "elevated", StartSensors.Verb, "--trace")]
    [InlineData(false, "--trace", "elevated", StartSensors.Verb)]
    [InlineData(false, "elevated", "--pseudo", StartSensors.Verb)]
    [InlineData(false, "elevated", "--trace", InstallDriver.Verb)]
    [InlineData(true, "elevated", StartSensors.Verb)]
    [InlineData(true, "elevated", InstallDriver.Verb)]
    public void AWordItDoesNotKnowExitsWithTwo(bool known, params string[] words)
    {
        var request = ThermiCommands.Parse(words, out var errors);

        Assert.Equal(known ? new ElevatedRequest(words[^1]) : (IThermiRequest?)null, request);
        Assert.Equal(!known, errors.Count > 0);
    }

    // The mode runs only the verbs it knows, whatever reaches it past the command line.
    [Fact]
    public async Task AVerbTheElevatedModeDoesNotKnowRunsNothing()
    {
        var services = new FakeServices("R", starts: true);
        var parts = new ElevatedParts(services, _ => throw new InvalidOperationException("No verb it does not know installs the driver."));

        Assert.Equal(Elevated.Unknown, await Elevated.RunAsync("other", parts, new FakeTimeProvider()));
        Assert.Equal(0, services.Asked);
    }

    // "Start sensors" as administrator: the service's states as the service manager gives them, one a poll, then the
    // exit code. A start Windows refuses is judged by the state after it, and a service that never runs gives up at 10 s.
    [Theory]
    [InlineData("M", true, StartSensors.Missing)]
    [InlineData("R", true, StartSensors.Started)]
    [InlineData("SSSR", true, StartSensors.Started)]
    [InlineData("SR", false, StartSensors.Started)]
    [InlineData("S", false, StartSensors.Failed)]
    [InlineData("S", true, StartSensors.Failed)]
    public async Task StartSensorsWaitsForTheServiceToRun(string states, bool starts, int exit)
    {
        var time = new FakeTimeProvider();
        var services = new FakeServices(states, starts);

        var run = StartSensors.RunAsync(services, time);
        for (var waited = TimeSpan.Zero; !run.IsCompleted && waited <= StartSensors.Wait; waited += TimeSpan.FromMilliseconds(250))
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
        }

        Assert.True(run.IsCompleted);
        Assert.Equal(exit, await run);
    }

    // The scale is read in the invariant culture, so 1.5 is the same in every language. The settings file may be missing:
    // the preview paints the defaults then, and creates none.
    [Theory]
    [InlineData(new[] { "o.png" }, 1f, null)]
    [InlineData(new[] { "o.png", "1.5" }, 1.5f, null)]
    [InlineData(new[] { "o.png", "0.25" }, 0.25f, null)]
    [InlineData(new[] { "o.png", "8" }, 8f, null)]
    [InlineData(new[] { "o.png", "2", "c.json" }, 2f, "c.json")]
    public void APreviewTakesASnapshotAPngAScaleAndSettings(string[] args, float scale, string? settings)
    {
        Assert.Equal(new PreviewRequest(Snapshot, "o.png", scale, settings), ThermiCommands.Parse(["preview", Snapshot, .. args], out _));
    }

    // A comma is no decimal point, and a scale past 8 or under a quarter is a typo, never a picture worth painting.
    [Theory]
    [InlineData("o.png", "big")]
    [InlineData("o.png", "1,5")]
    [InlineData("o.png", "NaN")]
    [InlineData("o.png", "Infinity")]
    [InlineData("o.png", "-Infinity")]
    [InlineData("o.png", "0")]
    [InlineData("o.png", "-1")]
    [InlineData("o.png", "0.2")]
    [InlineData("o.png", "8.5")]
    [InlineData("o.png", "1e9")]
    [InlineData("o.png", "1", "c.json", "more")]
    [InlineData]
    public void APreviewWithoutItsFilesOrWithABadScaleExitsWithTwo(params string[] args)
    {
        Assert.Null(ThermiCommands.Parse(["preview", Snapshot, .. args], out var errors));
        Assert.NotEmpty(errors);
    }

    // thermi.exe is a windowed program, so what it says goes to stderr only when someone reads it: here, a pipe. Arguments
    // that make no sense exit with 2, a snapshot that is none too, as the user's input; a picture Windows would not write,
    // here into a folder that is not there, is a run that failed, with 1.
    [Theory]
    [InlineData(2, "--overlay takes a whole number from 1 up.", "settings", "--overlay", "0")]
    [InlineData(2, "not-there.json", "preview", "not-there.json", "o.png", "1", "own-settings.json")]
    [InlineData(2, "scale takes a number from 0.25 to 8, such as 1.5.", "preview", TestRecordings.Admin, "o.png", "1,5", "own-settings.json")]
    [InlineData(2, "is not a snapshot saved with thermi-sensors --dump.", "preview", "own-junk.json", "o.png", "1", "own-settings.json")]
    [InlineData(1, "could not be written: Could not find a part of the path", "preview", TestRecordings.Admin, "own-missing-folder", "1", "own-settings.json")]
    public async Task WhatItCannotDoIsSaidOnStandardErrorWithItsExitCode(int expectedCode, string expected, params string[] args)
    {
        var (code, said) = await RunAsync(args);

        Assert.Equal(expectedCode, code);
        Assert.Contains(expected, said, StringComparison.Ordinal);
    }

    // The test's own files stand in for the words that name them: its settings, a file that is no snapshot, and a picture
    // in a folder that is not there.
    private async Task<(int Code, string Said)> RunAsync(string[] args)
    {
        var folder = Path.GetDirectoryName(_folder.File)!;
        var junk = Path.Combine(folder, "junk.json");
        await File.WriteAllTextAsync(junk, "{ \"not\": \"a snapshot\" }", TestContext.Current.CancellationToken);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "thermi.exe"))
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(Snapshot),
        };
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument switch
            {
                "own-settings.json" => _folder.File,
                "own-junk.json" => junk,
                "own-missing-folder" => Path.Combine(folder, "not-there", "o.png"),
                _ => argument,
            });
        }

        using var thermi = Process.Start(start)!;
        var said = await thermi.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await thermi.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (thermi.ExitCode, said);
    }

    // The fans' answer is recorded under a load of whole seconds, up to ten minutes, so a typo never loads the PC for hours.
    [Theory]
    [InlineData("30", 30)]
    [InlineData("600", 600)]
    [InlineData("601", null)]
    [InlineData("0", null)]
    [InlineData("1.5", null)]
    public void ALoadRunsForWholeSecondsUpToTenMinutes(string seconds, int? expected)
    {
        Assert.Equal(expected, (ThermiCommands.Parse(["cpu-load", seconds], out _) as CpuLoadRequest)?.Seconds);
    }

    [Fact]
    public void HelpIsAnsweredByTheCommandLine()
    {
        Assert.IsType<BuiltInRequest>(ThermiCommands.Parse(["--help"], out _));
        Assert.IsType<SensorBuiltIn>(SensorCommands.Parse(["--help"], out _));
    }

    [Theory]
    [InlineData(new string[0], "serve")]
    [InlineData(new[] { "--console" }, "console")]
    [InlineData(new[] { "--dump", "now.json" }, "dump now.json once")]
    [InlineData(new[] { "--dump", "load.json", "--seconds", "45" }, "dump load.json 45")]
    [InlineData(new[] { "--seconds", "45" }, "--seconds needs --dump.")]
    [InlineData(new[] { "--dump", "load.json", "--seconds", "0" }, "--seconds needs a whole number above zero.")]
    [InlineData(new[] { "--dump", "load.json", "--seconds", "many" }, "--seconds needs a whole number above zero.")]
    public void TheSensorServiceServesOrDumps(string[] args, string expected)
    {
        var request = SensorCommands.Parse(args, out var errors);

        Assert.Equal(expected, request switch
        {
            SensorServe { Console: false } => "serve",
            SensorServe => "console",
            SensorDump { Seconds: null } dump => $"dump {dump.File} once",
            SensorDump dump => $"dump {dump.File} {dump.Seconds}",
            _ => string.Join(" ", errors),
        });
    }

    /// <summary>The service manager, as each state asked for: the next of its letters, the last one kept, a start only when it takes it.</summary>
    internal sealed class FakeServices(string states, bool starts) : IServiceManager
    {
        public int Asked { get; private set; }

        public ServiceRun State() => states[Math.Min(Asked++, states.Length - 1)] switch
        {
            'M' => ServiceRun.Missing,
            'R' => ServiceRun.Running,
            'C' => ServiceRun.Changing,
            _ => ServiceRun.Stopped,
        };

        public bool Start() => starts;

        public bool Stop() => true;
    }
}
