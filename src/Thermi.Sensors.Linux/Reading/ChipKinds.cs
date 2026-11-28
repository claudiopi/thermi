namespace Thermi.Sensors.Linux.Reading;

/// <summary>What a <c>hwmon</c> chip measures, by its driver's name, which decides the device its sensors join.</summary>
internal enum ChipKind
{
    Cpu,
    Storage,
    GpuAmd,
    GpuIntel,
    SuperIO,
    EmbeddedController,
    Motherboard,
}

/// <summary>
/// The kinds and names of the kernel's <c>hwmon</c> chips. A board's chip is named as its vendor writes it, <c>nct6798</c> as
/// "Nuvoton NCT6798", so Core's rules for a chip, as a Nuvoton NCT67's CPU fan on its second input, hold on Linux as on
/// Windows, where LibreHardwareMonitor names it so.
/// </summary>
internal static class ChipKinds
{
    // The prefixes of the board chips' drivers' names and their vendors, as the kernel's nct6775, it87, f71882fg and w83627ehf
    // drivers name each chip they find.
    private static readonly (string Prefix, string Vendor)[] Vendors =
    [
        ("nct", "Nuvoton"), ("it8", "ITE"), ("f71", "Fintek"), ("f81", "Fintek"), ("w83", "Winbond"), ("sch5", "SMSC"),
    ];

    private static readonly Dictionary<string, ChipKind> Kinds = new(StringComparer.Ordinal)
    {
        ["k10temp"] = ChipKind.Cpu,
        ["k8temp"] = ChipKind.Cpu,
        ["zenpower"] = ChipKind.Cpu,
        ["coretemp"] = ChipKind.Cpu,
        ["cpu_thermal"] = ChipKind.Cpu,
        ["nvme"] = ChipKind.Storage,
        ["drivetemp"] = ChipKind.Storage,
        ["amdgpu"] = ChipKind.GpuAmd,
        ["radeon"] = ChipKind.GpuAmd,
        ["i915"] = ChipKind.GpuIntel,
        ["xe"] = ChipKind.GpuIntel,
    };

    // The embedded controllers' drivers, by the maker of the board or laptop they read.
    private static readonly Dictionary<string, string> Controllers = new(StringComparer.Ordinal)
    {
        ["asusec"] = "ASUS EC",
        ["asus_wmi_sensors"] = "ASUS WMI",
        ["dell_smm"] = "Dell SMM",
        ["thinkpad"] = "ThinkPad EC",
        ["applesmc"] = "Apple SMC",
        ["hp"] = "HP EC",
        ["cros_ec"] = "Chrome EC",
    };

    public static ChipKind KindOf(string chip) =>
        Kinds.TryGetValue(chip, out var kind) ? kind
        : Controllers.ContainsKey(chip) ? ChipKind.EmbeddedController
        : Vendors.Any(vendor => chip.StartsWith(vendor.Prefix, StringComparison.Ordinal)) ? ChipKind.SuperIO
        : ChipKind.Motherboard;

    /// <summary>A board chip's name as its vendor writes it, or the driver's own for a chip of no vendor listed.</summary>
    public static string NameOf(string chip)
    {
        var vendor = Vendors.FirstOrDefault(vendor => chip.StartsWith(vendor.Prefix, StringComparison.Ordinal)).Vendor;
        return vendor is not null ? $"{vendor} {chip.ToUpperInvariant()}" : Controllers.GetValueOrDefault(chip, chip);
    }

    /// <summary>The type of sensor a snapshot gives each kind of device, as LibreHardwareMonitor names them and Core reads them.</summary>
    public static string HardwareTypeOf(ChipKind kind) => kind.ToString();
}
