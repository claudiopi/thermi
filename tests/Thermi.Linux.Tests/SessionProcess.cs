using System.Diagnostics;

namespace Thermi.Linux.Tests;

/// <summary>
/// A program an <see cref="OwnSession"/> started and keeps running: both its output streams are read as they come
/// (<see cref="Lines"/>), so it never stops on a full pipe, and its input is the test's to write.
/// </summary>
internal sealed class SessionProcess(Process process)
{
    public Lines Out { get; } = new(process.StandardOutput);

    public Lines Error { get; } = new(process.StandardError);

    public StreamWriter Input => process.StandardInput;

    public int Id => process.Id;

    public int ExitCode => process.ExitCode;

    public Task WaitForExitAsync(CancellationToken cancel) => process.WaitForExitAsync(cancel);

    public void Kill() => process.Kill(entireProcessTree: true);

    /// <summary>The line it writes first on its standard output, as a program says it is ready; it fails with what it said once it ended first.</summary>
    public async Task<string> ReadyAsync(CancellationToken cancel) =>
        await Out.ReadLineAsync(cancel).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"{process.StartInfo.FileName} ended before it was ready: {await Error.ToEndAsync(cancel).ConfigureAwait(false)}");
}
