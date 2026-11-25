using System.Diagnostics;
using System.Security.Cryptography;
using Thermi.Core.Install;
using Thermi.Core.Updates;
using Thermi.SensorHost.Updates;
using Thermi.Trust;

namespace Thermi.SensorHost.User;

/// <summary>
/// "Install for everyone" from a per-user install (docs/adr/0016-admin-on-demand.md): the machine MSI of the version the
/// channel offers, as its signed manifest names it, downloaded and checked, in a release build signed by thermi's
/// certificate too, held so no one swaps it, and run by Windows'
/// own msiexec without runas, so the one UAC prompt names thermi's signer. It holds the updater's lock, <c>busy</c>, from start
/// to end, so no check or switch begins meanwhile to end the host under it, and neither writes over the other's state; what
/// it came to goes in the state the window reads.
/// </summary>
internal sealed class MachineInstaller(
    UpdateBuild build, InstallFolder folder, ManifestClient client, UpdateHost host, TimeProvider time, FileLog log, SemaphoreSlim busy)
{
    private const int LockTries = 25;

    private int _running;

    /// <summary>Whether an install runs now, which keeps the host alive past its last client.</summary>
    public bool Running => Volatile.Read(ref _running) == 1;

    /// <summary>
    /// Installs thermi for everyone: what it came to, null when another install runs already. <paramref name="worked"/> runs
    /// once it installed, while the lock is still held.
    /// </summary>
    public async Task<MachineInstallOutcome?> RunAsync(Func<string, Task<int>> msiexec, CancellationToken stop, Action? worked = null)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return null;
        }

        try
        {
            await busy.WaitAsync(stop);
            try
            {
                var outcome = await InstallHeldAsync(msiexec, stop);
                if (outcome == MachineInstallOutcome.Installed)
                {
                    worked?.Invoke();
                }

                return outcome;
            }
            finally
            {
                busy.Release();
            }
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<MachineInstallOutcome> InstallHeldAsync(Func<string, Task<int>> msiexec, CancellationToken stop)
    {
        await WriteAsync(MachineInstallOutcome.Running, stop);
        MachineInstallOutcome outcome;
        try
        {
            outcome = await InstallAsync(msiexec, stop);
        }
        catch (Exception ex) when (Failed(ex) || (ex is OperationCanceledException && !stop.IsCancellationRequested))
        {
            log.Say($"thermi could not install for everyone: {ex.Message}", EventLogEntryType.Warning);
            outcome = MachineInstallOutcome.Failed;
        }

        await WriteAsync(outcome, stop);
        return outcome;
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

    private static bool Failed(Exception ex) => ex is IOException or UnauthorizedAccessException or HttpRequestException
        or InvalidOperationException or System.ComponentModel.Win32Exception;

    private async Task<MachineInstallOutcome> InstallAsync(Func<string, Task<int>> msiexec, CancellationToken stop)
    {
        if (!folder.Trusted())
        {
            log.Say(
                $"{folder.Top} is not a folder only this user, SYSTEM and administrators change, so thermi installs nothing from it.",
                EventLogEntryType.Error);
            return MachineInstallOutcome.Failed;
        }

        var policy = host.Policy(build);
        var manifest = policy.Checks ? await ManifestAsync(policy.Channel, stop) : null;
        if (InstallForEveryone.WithoutMsi(policy.Checks, manifest, build.Version, build.Processor) is { } outcome)
        {
            return outcome;
        }

        var msi = InstallForEveryone.Msi(manifest!, build.Version, build.Processor)!;

        var path = Path.Combine(folder.Update, "machine-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + ".msi");
        try
        {
            return await DownloadAsync(msi, path, stop) && await RunCheckedAsync(msi, path, msiexec, stop)
                is { } code ? InstallForEveryone.OutcomeOf(code) : MachineInstallOutcome.Failed;
        }
        finally
        {
            Delete(path);
        }
    }

    // A file a scanner still holds stays for the folder's tidying, and never turns an install that worked into a failure.
    private void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Say($"thermi left {path} for later: {ex.Message}", EventLogEntryType.Information);
        }
    }

    // The channel's current manifest, as the updater would take it; null, said in the log, when none could be read or taken.
    private async Task<Manifest?> ManifestAsync(string channel, CancellationToken stop)
    {
        var state = folder.Read();
        var envelope = await client.EnvelopeAsync(build.ManifestOf(channel), stop);
        var read = envelope is null ? null : build.Open(envelope, state);
        var refusal = read?.Manifest is null ? $"no manifest could be read: {read?.Problem}"
            : UpdatePlan.Refusal(state, read, channel, time.GetUtcNow(), build.ReleaseKeyHash);
        if (refusal is null)
        {
            return read!.Manifest;
        }

        log.Say($"thermi could not install for everyone, since the {channel} channel's manifest was refused: {refusal}.", EventLogEntryType.Warning);
        return null;
    }

    // Into a file of a name nobody can guess in update\, checked for the manifest's size and SHA-256 as it is written.
    private async Task<bool> DownloadAsync(Package msi, string path, CancellationToken stop)
    {
        string? refused;
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            refused = await client.FetchAsync(msi, file, stop);
        }

        if (refused is not null)
        {
            log.Say($"thermi refused the machine MSI: {refused}. Nothing changed.", EventLogEntryType.Warning);
        }

        return refused is null;
    }

    // Held for reading alone until msiexec is done, and checked again through that hold, since the file could change
    // between the download's close and the hold: the file msiexec installs is the one checked. Null when it changed.
    private async Task<int?> RunCheckedAsync(Package msi, string path, Func<string, Task<int>> msiexec, CancellationToken stop)
    {
        await using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (msi.Refusal(held.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(held, stop))) is not null)
        {
            log.Say("The machine MSI changed after its check, so thermi did not run it. Nothing changed.", EventLogEntryType.Error);
            return null;
        }

        // As an update's catalog: in a release build, the manifest's key alone never runs code.
        if (build.Signers is { Count: > 0 } signers && !Authenticode.SignedBy(path, signers))
        {
            log.Say("The machine MSI is not signed by thermi's certificate, so thermi did not run it. Nothing changed.", EventLogEntryType.Error);
            return null;
        }

        var code = await msiexec(path);
        log.Say($"msiexec installed thermi for everyone with exit code {code}.", EventLogEntryType.Information);
        return code;
    }

    // Never under a switch's watchdog; a write that fails is said, and the window, which waits for it, gives up in time.
    private async Task WriteAsync(MachineInstallOutcome outcome, CancellationToken stop)
    {
        try
        {
            await using var held = await new SwitchLock(folder, time).HoldAsync(LockTries, stop);
            if (held is null)
            {
                log.Say($"thermi could not write that installing for everyone came to {outcome}: a switch holds the state.", EventLogEntryType.Warning);
                return;
            }

            folder.Write(folder.Read() with { MachineInstall = new(outcome, time.GetUtcNow()) });
        }
        catch (Exception ex) when (Failed(ex))
        {
            log.Say($"thermi could not write that installing for everyone came to {outcome}: {ex.Message}", EventLogEntryType.Warning);
        }
    }
}
