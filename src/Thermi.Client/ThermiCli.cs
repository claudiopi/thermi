using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Thermi.Client;

/// <summary>How a run of <c>thermi-cli</c> ended: its exit code, its stdout and its stderr.</summary>
internal sealed record CliEnd(int Exit, string Out, string Error);

/// <summary>Runs <c>thermi-cli</c>, which tests replace: once to its end, or a watch line by line until its stdin closes.</summary>
internal interface ICli
{
    Task<CliEnd> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken cancel);

    /// <summary>Each line it prints; <paramref name="ended"/> is told how it ended, once it has.</summary>
    IAsyncEnumerable<string> LinesAsync(string exe, IReadOnlyList<string> args, Action<CliEnd> ended, CancellationToken cancel);
}

/// <summary>
/// <c>thermi-cli</c> as a process of its own, without a window, its streams redirected; a cancel closes its stdin, which
/// ends a watch, and ends a run at once.
/// </summary>
internal sealed class ProcessCli : ICli
{
    public static ProcessCli Instance { get; } = new();

    public async Task<CliEnd> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken cancel)
    {
        using var process = Start(exe, args);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancel);
        var error = process.StandardError.ReadToEndAsync(cancel);
        try
        {
            await process.WaitForExitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            End(process);
            throw;
        }

        return new CliEnd(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    public async IAsyncEnumerable<string> LinesAsync(
        string exe, IReadOnlyList<string> args, Action<CliEnd> ended, [EnumeratorCancellation] CancellationToken cancel)
    {
        using var process = Start(exe, args);
        using var closing = cancel.Register(process.StandardInput.Close);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                yield return line;
            }
        }
        finally
        {
            // A line thermi-cli is still writing would block it before it sees stdin close, so what is left is read away.
            process.StandardInput.Close();
            var rest = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await rest.ConfigureAwait(false);
            ended(new CliEnd(process.ExitCode, "", await error.ConfigureAwait(false)));
        }
    }

    private static Process Start(string exe, IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(start) ?? throw new ThermiException($"{exe} did not start.", exitCode: -1);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            throw new ThermiException($"{exe} did not start: {ex.Message}", exitCode: -1);
        }
    }

    private static void End(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // It had exited already.
        }
    }
}
