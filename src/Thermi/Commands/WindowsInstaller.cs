using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.WinTrust;

namespace Thermi.Commands;

/// <summary>
/// The installer as Windows checks and runs it: its Authenticode signature, and its process, with no window and its own
/// folder for temp files.
/// </summary>
internal sealed class WindowsInstaller : IDriverInstaller
{
    // By its path: the open file shares neither writing nor deleting, so nothing can change or replace it meanwhile.
    public unsafe bool Signed(FileStream file)
    {
        fixed (char* path = file.Name)
        {
            var info = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                pcwszFilePath = path,
            };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                fdwRevocationChecks = WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
            };
            data.Anonymous.pFile = &info;
            var action = PInvoke.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var verdict = PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);
            data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
            PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);
            return verdict == 0;
        }
    }

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
