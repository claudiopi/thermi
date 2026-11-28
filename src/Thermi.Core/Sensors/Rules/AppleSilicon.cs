using System.Globalization;
using System.Text.RegularExpressions;

namespace Thermi.Core.Sensors.Rules;

/// <summary>What an SMC key of a Mac with Apple silicon measures, as the service names its sensor.</summary>
public enum AppleRole
{
    /// <summary>A temperature of the CPU's performance cores.</summary>
    PerformanceCore,

    /// <summary>A temperature of the CPU's efficiency cores.</summary>
    EfficiencyCore,

    /// <summary>A temperature of the GPU's cores.</summary>
    GpuCore,

    /// <summary>A temperature beside the memory.</summary>
    Memory,

    /// <summary>A temperature elsewhere in the Mac, as its SSD's or its Wi-Fi's.</summary>
    Board,

    /// <summary>Watts the Mac draws, in all or from its DC input or battery.</summary>
    Power,

    /// <summary>Volts, as the DC input's.</summary>
    Voltage,
}

/// <summary>An SMC key's role and the name its sensor shows, which keeps the key so a report can tell which it was.</summary>
public sealed record AppleKey(AppleRole Role, string Name);

/// <summary>
/// The names Apple gives on its chips, which the macOS service reads and names its sensors by, and <see cref="SensorMap"/>
/// picks the metrics from. Each chip family has its own SMC keys for its cores, from Stats' tables (exelban/stats,
/// <c>Modules/Sensors/values.swift</c>) and the Mac mini M4's recording: an M4's <c>Te</c> keys are its efficiency cores'
/// where an M1's are none, and an M3's <c>Tf</c> keys are its performance cores' and its GPU's. A family past M4, with
/// no table yet, reads its cores by the keys' prefixes, as macmon does. Fans are <c>F0Ac</c> on, counted by <c>FNum</c>.
/// </summary>
public static partial class AppleSilicon
{
    /// <summary>The hardware type of the GPU of Apple's chips.</summary>
    public const string GpuHardware = "GpuApple";

    /// <summary>The CPU's hottest core, which the CPU's temperature is.</summary>
    public const string HottestCore = "Core Max";

    /// <summary>The GPU's hottest core, its temperature.</summary>
    public const string HottestGpuCore = "GPU Core";

    /// <summary>The CPU's power, from IOReport's CPU energy, as powermetrics names it.</summary>
    public const string CpuPower = "CPU Power";

    /// <summary>The GPU's power, from IOReport's GPU energy, as powermetrics names it.</summary>
    public const string GpuPower = "GPU Power";

    /// <summary>The GPU's load, as its accelerator's statistics name it.</summary>
    public const string GpuLoad = "Device Utilization";

    /// <summary>The SMC's count of fans.</summary>
    public const string FanCount = "FNum";

    private static readonly Dictionary<int, Family> Families = new()
    {
        [1] = new(
            Efficiency: ["Tp09", "Tp0T"],
            Performance: ["Tp01", "Tp05", "Tp0D", "Tp0H", "Tp0L", "Tp0P", "Tp0X", "Tp0b"],
            Gpu: ["Tg05", "Tg0D", "Tg0L", "Tg0T"],
            Memory: ["Tm02", "Tm06", "Tm08", "Tm09"]),
        [2] = new(
            Efficiency: ["Tp1h", "Tp1t", "Tp1p", "Tp1l"],
            Performance: ["Tp01", "Tp05", "Tp09", "Tp0D", "Tp0X", "Tp0b", "Tp0f", "Tp0j"],
            Gpu: ["Tg0f", "Tg0j"],
            Memory: []),
        [3] = new(
            Efficiency: ["Te05", "Te0L", "Te0P", "Te0S"],
            Performance: ["Tf04", "Tf09", "Tf0A", "Tf0B", "Tf0D", "Tf0E", "Tf44", "Tf49", "Tf4A", "Tf4B", "Tf4D", "Tf4E"],
            Gpu: ["Tf14", "Tf18", "Tf19", "Tf1A", "Tf24", "Tf28", "Tf29", "Tf2A"],
            Memory: []),
        // The base M4's GPU has Tg0G and Tg0H, a Pro's or a Max's Tg1U and Tg1k; a Mac mini M4 has none of them.
        [4] = new(
            Efficiency: ["Te05", "Te0S", "Te09", "Te0H"],
            Performance: ["Tp01", "Tp05", "Tp09", "Tp0D", "Tp0V", "Tp0Y", "Tp0b", "Tp0e"],
            Gpu: ["Tg0G", "Tg0H", "Tg1U", "Tg1k", "Tg0K", "Tg0L", "Tg0d", "Tg0e", "Tg0j", "Tg0k"],
            Memory: ["Tm0p", "Tm1p", "Tm2p"]),
    };

    // What every chip names alike.
    private static readonly Dictionary<string, AppleKey> Named = new(StringComparer.Ordinal)
    {
        ["TH0x"] = new(AppleRole.Board, "NAND"),
        ["TW0P"] = new(AppleRole.Board, "Wi-Fi"),
        ["TaLP"] = new(AppleRole.Board, "Airflow Left"),
        ["TaRF"] = new(AppleRole.Board, "Airflow Right"),
        ["TB1T"] = new(AppleRole.Board, "Battery 1"),
        ["TB2T"] = new(AppleRole.Board, "Battery 2"),
        ["PSTR"] = new(AppleRole.Power, "System Total"),
        ["PDTR"] = new(AppleRole.Power, "DC In"),
        ["PPBR"] = new(AppleRole.Power, "Battery"),
        ["VD0R"] = new(AppleRole.Voltage, "DC In"),
    };

    /// <summary>The generation of a chip by the name the Mac gives it, as 4 for "Apple M4 Pro", or null for no M chip.</summary>
    public static int? Generation(string chip) =>
        Chip().Match(chip) is { Success: true } named ? int.Parse(named.Groups["generation"].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>What <paramref name="key"/> measures on <paramref name="chip"/>, or null for a key thermi does not show.</summary>
    public static AppleKey? KeyOf(string chip, string key)
    {
        if (Named.TryGetValue(key, out var named))
        {
            return named;
        }

        var generation = Generation(chip);
        var role = generation is null ? null
            : Families.TryGetValue(generation.Value, out var family) ? family.RoleOf(key)
            : ByPrefix(key);
        return role is { } found && Prefix(found) is { } prefix ? new AppleKey(found, $"{prefix} {key}") : null;
    }

    /// <summary>The SMC's keys of a fan, from 0: its speed now and its highest, in RPM.</summary>
    public static (string Speed, string Highest) FanKeys(int fan) =>
        (string.Create(CultureInfo.InvariantCulture, $"F{fan}Ac"), string.Create(CultureInfo.InvariantCulture, $"F{fan}Mx"));

    // The table's roles name their sensors by the key, after what it measures.
    private static string? Prefix(AppleRole role) => role switch
    {
        AppleRole.PerformanceCore => "P-Core",
        AppleRole.EfficiencyCore => "E-Core",
        AppleRole.GpuCore => "GPU Core",
        AppleRole.Memory => "Memory",
        AppleRole.Board or AppleRole.Power or AppleRole.Voltage => null,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    private static AppleRole? ByPrefix(string key) => key.Length == 4
        ? key[..2] switch
        {
            "Tp" => AppleRole.PerformanceCore,
            "Te" => AppleRole.EfficiencyCore,
            "Tg" => AppleRole.GpuCore,
            _ => null,
        }
        : null;

    [GeneratedRegex(@"^Apple M(?<generation>[1-9][0-9]?)\b", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Chip();

    private sealed record Family(string[] Efficiency, string[] Performance, string[] Gpu, string[] Memory)
    {
        public AppleRole? RoleOf(string key) =>
            Performance.Contains(key, StringComparer.Ordinal) ? AppleRole.PerformanceCore
            : Efficiency.Contains(key, StringComparer.Ordinal) ? AppleRole.EfficiencyCore
            : Gpu.Contains(key, StringComparer.Ordinal) ? AppleRole.GpuCore
            : Memory.Contains(key, StringComparer.Ordinal) ? AppleRole.Memory
            : null;
    }
}
