using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Thermi.Client;

/// <summary>
/// thermi's sensors as typed values, read by starting <c>thermi-cli</c>, found on <c>PATH</c> or under Program Files, with
/// the contract's schema 1. It installs nothing: without thermi, or with its service not installed, a call fails with a
/// <see cref="ThermiException"/> that says so and how to install it.
/// </summary>
public sealed class ThermiClient
{
    /// <summary>Where thermi is installed from.</summary>
#pragma warning disable S1075 // thermi's own releases, the one page a person who has no thermi yet installs it from.
    public const string InstallUrl = "https://github.com/claudiopi/thermi/releases";
#pragma warning restore S1075

    private const string Cli = "thermi-cli.exe";

    private readonly Func<string?> _find;
    private readonly ICli _cli;

    /// <summary>A client of the <c>thermi-cli</c> on <c>PATH</c>, or else under Program Files.</summary>
    public ThermiClient()
        : this(Locate, ProcessCli.Instance)
    {
    }

    /// <summary>A client of the <c>thermi-cli</c> at <paramref name="path"/>.</summary>
    public ThermiClient(string path)
        : this(() => File.Exists(path) ? path : null, ProcessCli.Instance)
    {
    }

    internal ThermiClient(Func<string?> find, ICli cli)
    {
        _find = find;
        _cli = cli;
    }

    /// <summary>Whether the sensors read, an overlay runs, and each problem with what a person does about it.</summary>
    public Task<ThermiStatus> StatusAsync(CancellationToken cancel = default) => RunAsync(["status"], ThermiJson.Default.ThermiStatus, cancel);

    /// <summary>
    /// Metrics as <c>cpu.temperature</c>, sensor ids and items as <c>item:&lt;id&gt;</c>, every metric and item when none are
    /// given; with <paramref name="seconds"/> past 1, sampled once a second, with each value's minimum, maximum and average.
    /// </summary>
    public Task<ThermiReading> ReadAsync(IEnumerable<string>? ids = null, int seconds = 1, CancellationToken cancel = default) =>
        RunAsync(["read", .. ids ?? [], "--seconds", seconds.ToString(CultureInfo.InvariantCulture)], ThermiJson.Default.ThermiReading, cancel);

    /// <summary>Every sensor the service reads, by device, or those <paramref name="search"/> finds, of the types given.</summary>
    public Task<ThermiSensors> SensorsAsync(string? search = null, IEnumerable<string>? types = null, CancellationToken cancel = default) =>
        RunAsync(
            ["sensors", .. search is null ? Array.Empty<string>() : ["--search", search], .. (types ?? []).SelectMany(type => new[] { "--type", type })],
            ThermiJson.Default.ThermiSensors,
            cancel);

    /// <summary>A read once a second until <paramref name="cancel"/>, which ends the watch; one that stops otherwise throws why.</summary>
    public async IAsyncEnumerable<ThermiReading> WatchAsync(IEnumerable<string>? ids = null, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        CliEnd? end = null;
        await foreach (var line in _cli.LinesAsync(Found(), Args(["watch", .. ids ?? []]), ended => end = ended, cancel).ConfigureAwait(false))
        {
            yield return Parse(line, ThermiJson.Default.ThermiReading);
        }

        if (end is { Exit: not 0 } failed && !cancel.IsCancellationRequested)
        {
            throw Failed(failed);
        }
    }

    internal static T Parse<T>(string json, JsonTypeInfo<T> type)
    {
        try
        {
            return JsonSerializer.Deserialize(json, type) ?? throw new ThermiException("thermi-cli answered nothing.", exitCode: 0);
        }
        catch (JsonException ex)
        {
            throw new ThermiException($"thermi-cli answered what is not its contract's JSON: {ex.Message}", exitCode: 0, ex);
        }
    }

    private async Task<T> RunAsync<T>(IReadOnlyList<string> args, JsonTypeInfo<T> type, CancellationToken cancel)
    {
        var end = await _cli.RunAsync(Found(), Args(args), cancel).ConfigureAwait(false);
        return end.Exit == 0 ? Parse(end.Out, type) : throw Failed(end);
    }

    private static string[] Args(IReadOnlyList<string> args) => [.. args, "--schema", "1"];

    private string Found() => _find() ?? throw new ThermiException(
        $"thermi is not installed: {Cli} is neither on PATH nor under Program Files. Install thermi from {InstallUrl}; its sensors alone are enough.",
        exitCode: -1);

    // thermi-cli says on stderr why it did not finish, and how a person fixes it when they must, as a service not installed.
    private static ThermiException Failed(CliEnd end) =>
        new(end.Error.Trim() is { Length: > 0 } said ? said : $"thermi-cli ended with {end.Exit}.", end.Exit);

    private static string? Locate()
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "thermi"));
        return folders.Select(folder => Path.Combine(folder, Cli)).FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// Why a call did not answer, in <c>thermi-cli</c>'s words when it ran: <see cref="ExitCode"/> is its exit code, 2 for bad
/// arguments, 3 for a service unreachable, stopped, stale or not installed, or -1 when it could not be found or started.
/// </summary>
public sealed class ThermiException(string message, int exitCode, Exception? inner = null) : Exception(message, inner)
{
    public int ExitCode => exitCode;
}
