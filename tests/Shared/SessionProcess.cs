using System.Diagnostics;

namespace Thermi.Tests.Shared;

/// <summary>
/// A program a test's own session started and keeps running: both its output streams are read as they come
/// (<see cref="Lines"/>), so it never stops on a full pipe, and its input is the test's to write.
/// </summary>
internal sealed class SessionProcess
{
    private readonly Process _process;

    public SessionProcess(Process process)
    {
        _process = process;
        var program = Path.GetFileName(process.StartInfo.FileName);
        Error = new Lines(process.StandardError, program);
        Out = new Lines(process.StandardOutput, program, Error);
    }

    public Lines Out { get; }

    public Lines Error { get; }

    public StreamWriter Input => _process.StandardInput;

    public int Id => _process.Id;

    public int ExitCode => _process.ExitCode;

    /// <summary>Its end, bounded (<see cref="Bound.End"/>), failing with its last lines once the bound runs out.</summary>
    public async Task WaitForExitAsync(CancellationToken cancel)
    {
        try
        {
            await _process.WaitForExitAsync(cancel).WaitAsync(Bound.End, TimeProvider.System, cancel).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw Error.Late($"did not end in {Bound.End.TotalSeconds:0} s");
        }
    }

    /// <summary>Its end, with its input closed: its exit code, and what it wrote to each stream.</summary>
    public async Task<Ran> ToEndAsync(CancellationToken cancel)
    {
        Input.Close();
        var output = Out.ToEndAsync(cancel);
        var error = Error.ToEndAsync(cancel);
        await WaitForExitAsync(cancel).ConfigureAwait(false);
        return new Ran(ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    public void Kill() => _process.Kill(entireProcessTree: true);

    /// <summary>The line it writes first on its standard output, as a program says it is ready; it fails with what it said once it ended first.</summary>
    public async Task<string> ReadyAsync(CancellationToken cancel) =>
        await Out.ReadLineAsync(cancel).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"{_process.StartInfo.FileName} ended before it was ready: {await Error.ToEndAsync(cancel).ConfigureAwait(false)}");
}
