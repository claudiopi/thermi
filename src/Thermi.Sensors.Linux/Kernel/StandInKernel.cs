using System.Text.RegularExpressions;

namespace Thermi.Sensors.Linux.Kernel;

/// <summary>
/// The kernel as a recording or a dump sees it: a name of a folder that holds a device's address, as a USB adapter's
/// <c>enx…</c> or a Bluetooth mouse battery's <c>hid-…-battery</c>, has a made-up stand-in, <c>device1</c> on, the same
/// each time it comes back, so neither a tree's paths nor a dump's ids name a network or a device. A path of stand-ins
/// leads to the real files.
/// </summary>
internal sealed partial class StandInKernel(IKernel kernel) : IKernel
{
    private readonly Dictionary<string, string> _standIns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _reals = new(StringComparer.Ordinal);

    public string? Read(string path) => kernel.Read(Real(path));

    public IReadOnlyList<string> List(string folder) => [.. kernel.List(Real(folder)).Select(StandIn)];

    public string? Resolve(string path) => kernel.Resolve(Real(path)) is { } target ? string.Join('/', target.Split('/').Select(StandIn)) : null;

    public Moment? Now() => kernel.Now();

    public IReadOnlyList<GpuAnswer>? Nvml() => kernel.Nvml();

    public void Next() => kernel.Next();

    private string StandIn(string name)
    {
        if (!Address().IsMatch(name))
        {
            return name;
        }

        if (!_standIns.TryGetValue(name, out var standIn))
        {
            standIn = Address().Replace(name, $"device{_standIns.Count + 1}");
            _standIns[name] = standIn;
            _reals[standIn] = name;
        }

        return standIn;
    }

    private string Real(string path) => string.Join('/', path.Split('/').Select(part => _reals.GetValueOrDefault(part, part)));

    // An address with its separators, as Bluetooth's, or the twelve digits an adapter's name holds after enx or wlx.
    [GeneratedRegex(@"(?:[0-9a-f]{2}[:_-]){5}[0-9a-f]{2}|(?<=^(?:enx|wlx))[0-9a-f]{12}$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Address();
}
