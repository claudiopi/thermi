using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// A chip of the kernel's <c>hwmon</c>: its driver's name, the device it measures, as a path under <c>/sys/devices</c>
/// that stays the same across boots, which <c>hwmonN</c>'s number does not, and its inputs.
/// </summary>
internal sealed record Chip(string Name, string Device, IReadOnlyList<ChipInput> Inputs)
{
    public ChipKind Kind => ChipKinds.KindOf(Name);
}

/// <summary>An input of a chip: the file it reads, its sensor's type, its index from 0, its name and what scales it to Core's unit.</summary>
internal sealed record ChipInput(string File, string Type, int Index, string Name, double Scale);

/// <summary>
/// Finds the chips of <c>/sys/class/hwmon</c>, and reads their inputs. An input is a chip's file as
/// <c>temp1_input</c>, named by its <c>temp1_label</c> or, unlabeled, as LibreHardwareMonitor numbers it, <c>fan2</c> as
/// "Fan" and its number; one whose label is there but whose value is gone still is a sensor, which reads missing.
/// </summary>
internal static partial class Hwmon
{
    private const string Root = "/sys/class/hwmon";

    private const string Devices = "/sys/devices/";

    // Each kind of input: its sensor's type, its unnumbered name, the number its files start from, and its scale.
    private static readonly Dictionary<string, (string Type, string Name, int First, double Scale)> Kinds = new(StringComparer.Ordinal)
    {
        ["temp"] = ("Temperature", "Temperature", 1, 1e-3),
        ["fan"] = ("Fan", "Fan", 1, 1),
        ["in"] = ("Voltage", "Voltage", 0, 1e-3),
        ["power"] = ("Power", "Power", 1, 1e-6),
        ["curr"] = ("Current", "Current", 1, 1e-3),
        ["freq"] = ("Clock", "Clock", 1, 1e-6),
        ["pwm"] = ("Control", "Fan Control", 1, 100d / 255),
    };

    /// <summary>
    /// Each chip's folder and the device it measures, as <c>/sys/class/hwmon</c> holds them now: a USB chip plugged again
    /// may take the number another chip had, under a device of its own.
    /// </summary>
    public static string Listing(IKernel kernel) =>
        string.Join('\n', kernel.List(Root).Select(folder => $"{folder} {kernel.Resolve($"{Root}/{folder}/device") ?? kernel.Resolve($"{Root}/{folder}")}"));

    public static IReadOnlyList<Chip> Find(IKernel kernel) =>
        [.. kernel.List(Root).Select(folder => ChipIn(kernel, $"{Root}/{folder}")).OfType<Chip>().OrderBy(chip => chip.Device, StringComparer.Ordinal)];

    /// <summary>
    /// The chips of one device, as a CPU of two sockets has a <c>k10temp</c> for each, with each chip's inputs numbered on
    /// from the chips before it, type by type, so no two share an id.
    /// </summary>
    public static IReadOnlyList<Chip> NumberedOn(IReadOnlyList<Chip> chips)
    {
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        return [.. chips.Select(chip =>
        {
            var first = chip.Inputs.GroupBy(input => input.Type).ToDictionary(group => group.Key, group => next.GetValueOrDefault(group.Key));
            foreach (var group in chip.Inputs.GroupBy(input => input.Type))
            {
                next[group.Key] = first[group.Key] + group.Max(input => input.Index) + 1;
            }

            return chip with { Inputs = [.. chip.Inputs.Select(input => input with { Index = first[input.Type] + input.Index })] };
        })];
    }

    /// <summary>The path of a device under <c>/sys/devices</c>, as a sensor's id holds it.</summary>
    public static string DevicePath(string resolved) =>
        resolved.StartsWith(Devices, StringComparison.Ordinal) ? resolved[Devices.Length..] : resolved.TrimStart('/');

    /// <summary>
    /// Each input's sensor, under <paramref name="device"/>'s id, as <paramref name="hardwareType"/> and
    /// <paramref name="hardware"/>; a board's temperature of a sensor not connected reads missing, as Core says, and so does
    /// every input of a device <paramref name="asleep"/>, whose files are left unread.
    /// </summary>
    public static IEnumerable<RawSensor> Read(IKernel kernel, Chip chip, string device, string hardwareType, string hardware, bool asleep = false) =>
        chip.Inputs.Select(input =>
        {
            var sensor = new RawSensor(
                string.Create(CultureInfo.InvariantCulture, $"{device}/{input.Type.ToLowerInvariant()}/{input.Index}"),
                hardwareType,
                hardware,
                input.Type,
                input.Name,
                asleep ? null : Counters.Number(kernel.Read(input.File)) * input.Scale);
            return SensorMap.NotConnected(sensor) ? sensor with { Value = null } : sensor;
        });

    private static Chip? ChipIn(IKernel kernel, string folder)
    {
        // A chip of no device, as a thermal zone's, is named by its folder's parent, which holds no hwmon number.
        var device = kernel.Resolve($"{folder}/device") ?? (kernel.Resolve(folder) is { } own ? own[..own.LastIndexOf('/')] : null);
        if (kernel.Read($"{folder}/name")?.Trim() is not { Length: > 0 } name || device is null)
        {
            return null;
        }

        var files = kernel.List(folder);
        var inputs = files.Select(file => InputFile().Match(file)).Where(match => match.Success)
            .Select(match => (Kind: match.Groups["kind"].Value, Number: int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)))
            .Distinct()
            .Order()
            .Select(input => InputOf(kernel, folder, files, input.Kind, input.Number))
            .ToList();
        return inputs.Count == 0 ? null : new Chip(name, DevicePath(device), inputs);
    }

    private static ChipInput InputOf(IKernel kernel, string folder, IReadOnlyList<string> files, string kind, int number)
    {
        var (type, unnamed, first, scale) = Kinds[kind];
        var stem = kind == "pwm" ? $"pwm{number}" : $"{kind}{number}";
        // A power is an average over the chip's own interval on some drivers, as amdgpu's, and an input on others.
        var value = kind == "pwm" ? stem : kind == "power" && files.Contains($"{stem}_average") ? $"{stem}_average" : $"{stem}_input";
        var label = kernel.Read($"{folder}/{stem}_label")?.Trim();
        var name = label is { Length: > 0 } ? label : string.Create(CultureInfo.InvariantCulture, $"{unnamed} #{number - first + 1}");
        return new ChipInput($"{folder}/{value}", type, number - first, name, scale);
    }

    [GeneratedRegex(
        @"^(?:(?<kind>temp|fan|in|power|curr|freq)(?<number>\d{1,4})_(?:input|label|average)|(?<kind>pwm)(?<number>\d{1,4}))$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex InputFile();
}
