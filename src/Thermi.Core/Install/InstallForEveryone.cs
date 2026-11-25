using Thermi.Core.Updates;

namespace Thermi.Core.Install;

public enum FixOffer
{
    /// <summary>A machine install: the fix is ADR 0010's, unchanged.</summary>
    None,

    MachineMsi,

    /// <summary>No machine MSI to trust: the fix opens thermi's download page.</summary>
    DownloadPage,
}

public enum ProductContext
{
    Machine,
    UserManaged,
    UserUnmanaged,
}

/// <summary>A product Windows Installer lists: its code, its UpgradeCode, and whose it is.</summary>
public sealed record InstalledProduct(string Code, string UpgradeCode, ProductContext Context, string? User);

/// <summary>
/// The one prompt that turns a per-user install into the machine's, with the service and the driver
/// (docs/adr/0016-admin-on-demand.md): which MSI it runs, and which per-user product goes after.
/// </summary>
public static class InstallForEveryone
{
    public const string MachineUpgradeCode = "{DB4B32AF-CD73-400D-BFC1-1A144C2BE80C}";

    public const string PerUserUpgradeCode = "{E64A26FA-E001-4EDF-A31C-40957EC23FAB}";

    /// <summary>The machine MSI's property that adds the driver on its first install.</summary>
    public const string DriverProperty = "ADDDRIVER";

    /// <summary>
    /// What the fix offers: in a per-user install, the machine MSI of the version <paramref name="manifest"/> offers when it
    /// is <paramref name="running"/> or later and lists one for <paramref name="processor"/>; a channel turned off, a
    /// running version withdrawn or the policy's no leave none to trust, and the fix opens the download page.
    /// </summary>
    public static (FixOffer Offer, Package? Msi) Offer(bool perUser, bool policyOff, Manifest? manifest, ReleaseVersion running, string processor)
    {
        if (!perUser)
        {
            return (FixOffer.None, null);
        }

        return !policyOff
            && manifest is { Disabled: false, Version: { } offered }
            && offered.CompareTo(running) >= 0
            && !manifest.Withdrawn.Contains(running)
            && manifest.Msis.TryGetValue(processor, out var msi)
            ? (FixOffer.MachineMsi, msi)
            : (FixOffer.DownloadPage, null);
    }

    /// <summary>
    /// The per-user thermi of <paramref name="user"/> the machine's overlay removes: found by its own UpgradeCode, in that
    /// user's unmanaged context alone, so neither the machine's thermi nor anyone else's product is ever taken.
    /// </summary>
    public static string? PerUserProduct(IEnumerable<InstalledProduct> products, string user) =>
        products.FirstOrDefault(product =>
            product.Context == ProductContext.UserUnmanaged
            && string.Equals(product.UpgradeCode, PerUserUpgradeCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(product.User, user, StringComparison.OrdinalIgnoreCase))?.Code;
}
