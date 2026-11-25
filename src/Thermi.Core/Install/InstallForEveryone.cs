using Thermi.Core.Updates;

namespace Thermi.Core.Install;

public enum MachineInstallOutcome
{
    Running,
    Installed,

    /// <summary>The UAC prompt was refused: nothing changed.</summary>
    Refused,

    /// <summary>The download, its check or the install failed: nothing changed.</summary>
    Failed,

    /// <summary>No machine MSI to trust: the window opens thermi's download page.</summary>
    DownloadPage,
}

/// <summary>Where the last "install for everyone" of this per-user install is, and since when, as the window reads it.</summary>
public sealed record MachineInstall(MachineInstallOutcome Outcome, DateTimeOffset At);

/// <summary>
/// The one prompt that turns a per-user install into the machine's, with the service and the driver
/// (docs/adr/0016-admin-on-demand.md): which MSI it runs, and what its exit says.
/// </summary>
public static class InstallForEveryone
{
    public const string MachineUpgradeCode = "{DB4B32AF-CD73-400D-BFC1-1A144C2BE80C}";

    public const string PerUserUpgradeCode = "{E64A26FA-E001-4EDF-A31C-40957EC23FAB}";

    /// <summary>The machine MSI's property that adds the driver on its first install.</summary>
    public const string DriverProperty = "ADDDRIVER";

    /// <summary>The user's host's event that asks it to install for everyone, beside ADR 0008's codes.</summary>
    public const int Code = 131;

    // ERROR_CANCELLED, as the UAC prompt's no, and ERROR_INSTALL_USEREXIT, as Windows Installer's own cancel.
    private const int Cancelled = 1223;
    private const int UserExit = 1602;

    // ERROR_SUCCESS_REBOOT_REQUIRED: installed, with the driver loading once Windows restarts.
    private const int RestartNeeded = 3010;

    public static MachineInstallOutcome OutcomeOf(int exitCode) => exitCode switch
    {
        0 or RestartNeeded => MachineInstallOutcome.Installed,
        Cancelled or UserExit => MachineInstallOutcome.Refused,
        _ => MachineInstallOutcome.Failed,
    };

    /// <summary>
    /// The machine MSI to run: that of the version <paramref name="manifest"/> offers when it is <paramref name="running"/>
    /// or later and lists one for <paramref name="processor"/>; null, as for a channel turned off or a running version
    /// withdrawn, leaves none to trust, and the fix opens the download page.
    /// </summary>
    public static Package? Msi(Manifest manifest, ReleaseVersion running, string processor) =>
        manifest is { Disabled: false, Version: { } offered }
        && offered.CompareTo(running) >= 0
        && !manifest.Withdrawn.Contains(running)
        && manifest.Msis.TryGetValue(processor, out var msi)
            ? msi
            : null;
}
