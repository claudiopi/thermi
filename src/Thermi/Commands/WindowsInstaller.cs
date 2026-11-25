using System.ComponentModel;
using System.Diagnostics;
using Thermi.Trust;

namespace Thermi.Commands;

/// <summary>
/// The installer as Windows checks and runs it: its Authenticode signature, and its process, with no window and its own
/// folder for temp files.
/// </summary>
internal sealed class WindowsInstaller : IDriverInstaller
{
    // By its path: the open file shares neither writing nor deleting, so nothing can change or replace it meanwhile.
    public bool Signed(FileStream file) => Authenticode.Trusted(file.Name);

    public async Task<int?> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan wait, TimeProvider time)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Clean(start.Environment, Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var process = Started(start);
        if (process is null)
        {
            return null;
        }

        using var cancel = new CancellationTokenSource(wait, time);
        try
        {
            await process.WaitForExitAsync(cancel.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // Left running, it would go on as administrator while the window says it failed, and hold its folder.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return null;
        }
    }

    // The elevated mode's environment is the one the thermi-elevated service left it, Windows' own folders alone; the
    // installer's temp files go in its own folder, which only administrators write.
    private static void Clean(IDictionary<string, string?> environment, string folder)
    {
        environment["TEMP"] = folder;
        environment["TMP"] = folder;
    }

    private static Process? Started(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
