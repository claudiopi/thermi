using System.Diagnostics;
using System.Security.Cryptography;
using Thermi.Core.Install;
using Thermi.Core.Updates;
using Thermi.SensorHost.Updates;

namespace Thermi.SensorHost.User;

/// <summary>
/// "Install for everyone" from a per-user install (docs/adr/0016-admin-on-demand.md): the machine MSI of the version the
/// channel offers, as its signed manifest names it, downloaded and checked, held so no one swaps it, and run by Windows'
/// own msiexec without runas, so the one UAC prompt names thermi's signer. What it came to goes in the state the window reads.
/// </summary>
internal sealed class MachineInstaller(UpdateBuild build, InstallFolder folder, ManifestClient client, UpdateHost host, TimeProvider time, FileLog log)
{
    private int _running;

    /// <summary>Whether an install runs now, which keeps the host alive past its last client.</summary>
    public bool Running => Volatile.Read(ref _running) == 1;

    public async Task RunAsync(Func<string, Task<int>> msiexec, CancellationToken stop)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            Write(MachineInstallOutcome.Running);
            Write(await InstallAsync(msiexec, stop));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException
            or System.ComponentModel.Win32Exception or OperationCanceledException && !stop.IsCancellationRequested)
        {
            log.Say($"thermi could not install for everyone: {ex.Message}", EventLogEntryType.Warning);
            Write(MachineInstallOutcome.Failed);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Windows' msiexec on <paramref name="msi"/>, with the driver, its prompt and progress only: its exit code.</summary>
    public static async Task<int> MsiexecAsync(string msi)
    {
        var install = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe")) { UseShellExecute = false };
        foreach (var argument in (string[])["/i", msi, InstallForEveryone.DriverProperty + "=1", "/passive"])
        {
            install.ArgumentList.Add(argument);
        }

        using var process = Process.Start(install) ?? throw new System.ComponentModel.Win32Exception("msiexec did not start.");
        await process.WaitForExitAsync(CancellationToken.None);
        return process.ExitCode;
    }

    private async Task<MachineInstallOutcome> InstallAsync(Func<string, Task<int>> msiexec, CancellationToken stop)
    {
        if (!folder.Trusted())
        {
            log.Say(
                $"{folder.Top} is not a folder only this user, SYSTEM and administrators change, so thermi installs nothing from it.",
                EventLogEntryType.Error);
            return MachineInstallOutcome.Failed;
        }

        if (await OfferedAsync(stop) is not { } msi)
        {
            return MachineInstallOutcome.DownloadPage;
        }

        var path = await DownloadAsync(msi, stop);
        if (path is null)
        {
            return MachineInstallOutcome.Failed;
        }

        try
        {
            // Held for reading alone until msiexec is done, so the file it installs is the one checked.
            await using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var code = await msiexec(path);
            log.Say($"msiexec installed thermi for everyone with exit code {code}.", EventLogEntryType.Information);
            return InstallForEveryone.OutcomeOf(code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The machine MSI the channel's manifest offers now, when the policy lets this PC update and the manifest is current.
    private async Task<Package?> OfferedAsync(CancellationToken stop)
    {
        var policy = host.Policy(build);
        if (!policy.Checks)
        {
            return null;
        }

        var state = folder.Read();
        var envelope = await client.EnvelopeAsync(build.ManifestOf(policy.Channel), stop);
        var read = envelope is null ? null : ManifestEnvelope.Open(envelope, build.ReleaseKeyHash == state.RevokedKey ? null : build.Release, build.Backup);
        var current = read?.Manifest is not null && UpdatePlan.Refusal(state, read, policy.Channel, time.GetUtcNow(), build.ReleaseKeyHash) is null;
        return InstallForEveryone.Offer(perUser: true, policyOff: false, current ? read!.Manifest : null, build.Version, build.Processor).Msi;
    }

    // Into a file of a name nobody can guess in update\, checked for the manifest's size and SHA-256 as it is written.
    private async Task<string?> DownloadAsync(Package msi, CancellationToken stop)
    {
        var path = Path.Combine(folder.Update, "machine-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + ".msi");
        string? refused;
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            refused = await client.FetchAsync(msi, file, stop);
        }

        if (refused is null)
        {
            return path;
        }

        log.Say($"thermi refused the machine MSI: {refused}. Nothing changed.", EventLogEntryType.Warning);
        File.Delete(path);
        return null;
    }

    private void Write(MachineInstallOutcome outcome) => folder.Write(folder.Read() with { MachineInstall = new(outcome, time.GetUtcNow()) });
}
