using System.ComponentModel;
using System.Diagnostics;

namespace Thermi.Linux.Agents;

/// <summary>
/// A program of the system's, run by its full path, never one found on the PATH, and waited for: its exit code, or -1 for
/// one that is not there, or one still running as <c>cancel</c> fires, which ends it. What it writes is read as it
/// comes, or one that writes more than a pipe holds would wait on thermi, and thermi on it, forever.
/// </summary>
internal static class SystemProgram
{
    public static async Task<int> RunAsync(string program, IReadOnlyList<string> args, CancellationToken cancel)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(program, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
        }
        catch (Win32Exception)
        {
            return -1;
        }

        if (process is null)
        {
            return -1;
        }

        using (process)
        {
            var read = Task.WhenAll(
                process.StandardOutput.ReadToEndAsync(CancellationToken.None),
                process.StandardError.ReadToEndAsync(CancellationToken.None));
            try
            {
                await process.WaitForExitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await read;
                return -1;
            }

            await read;
            return process.ExitCode;
        }
    }
}
