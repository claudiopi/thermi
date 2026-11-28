using System.Globalization;
using System.Text.Json.Nodes;

namespace Thermi.Sensors.MacOS.Source;

/// <summary>
/// The answers of <paramref name="mac"/>, kept as <see cref="RecordedMac"/> replays them: each second's folder of
/// <paramref name="recording"/> holds exactly what the readers asked that second, so a recording holds nothing they do not
/// read, as the registry's other properties, which name the Mac, its serial number and its adapters' addresses. The
/// address is moved into the range kept for documentation before the readers read it (<see cref="Moment.Scrubbed"/>), so a
/// replay of the recording reads as the recording did. What the readers asked of the registry and sysctl once, which they
/// ask only when they look for the hardware, every second holds again, so a second replays whole when a test breaks one.
/// </summary>
internal sealed class RecordingMac(IMac mac, string recording) : IMac, IDisposable
{
    private readonly JsonObject _registry = [];
    private JsonObject _smc = [];
    private IReadOnlyList<HidReading> _hid = [];
    private IReadOnlyList<ReportChannel> _report = [];
    private IReadOnlyList<Adapter> _adapters = [];
    private SystemAnswers _system = new();
    private int _second = -1;

    public IReadOnlyList<string> SmcKeys() => mac.SmcKeys();

    public double? Smc(string key)
    {
        var value = mac.Smc(key);
        _smc[key] = value;
        return value;
    }

    public IReadOnlyList<HidReading> Hid() => _hid = mac.Hid();

    public IReadOnlyList<ReportChannel> Report() => _report = mac.Report();

    public IReadOnlyList<JsonObject> Registry(string className, IReadOnlyList<string> keys)
    {
        var entries = mac.Registry(className, keys);
        _registry[className] = new JsonArray([.. entries.Select(entry => entry.DeepClone())]);
        return entries;
    }

    public string? Sysctl(string name)
    {
        var text = mac.Sysctl(name);
        _system.Sysctl[name] = text;
        return text;
    }

    public long? SysctlNumber(string name)
    {
        var number = mac.SysctlNumber(name);
        _system.Sysctl[name] = number;
        return number;
    }

    public IReadOnlyList<long[]>? CpuTicks()
    {
        var ticks = mac.CpuTicks();
        _system = _system with { CpuTicks = ticks?.ToArray() };
        return ticks;
    }

    public MemoryPages? Memory()
    {
        var pages = mac.Memory();
        _system = _system with { Memory = pages };
        return pages;
    }

    public IReadOnlyList<Adapter> Adapters() => _adapters = mac.Adapters();

    public Moment? Now()
    {
        var moment = mac.Now()?.Scrubbed();
        _system = _system with { Moment = moment };
        return moment;
    }

    public void Next()
    {
        Flush();
        _second++;
        _smc = [];
        _hid = [];
        _report = [];
        _adapters = [];
        _system = _system with { Moment = null, CpuTicks = null, Memory = null };
        mac.Next();
    }

    public void Dispose() => Flush();

    private void Flush()
    {
        if (_second < 0)
        {
            return;
        }

        var folder = Path.Combine(recording, _second.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, MacJson.SmcFile), MacJson.Write(_smc));
        File.WriteAllText(Path.Combine(folder, MacJson.HidFile), MacJson.Write(_hid));
        File.WriteAllText(Path.Combine(folder, MacJson.ReportFile), MacJson.Write(_report));
        File.WriteAllText(Path.Combine(folder, MacJson.RegistryFile), MacJson.Write(_registry));
        File.WriteAllText(Path.Combine(folder, MacJson.NetworkFile), MacJson.Write(_adapters));
        File.WriteAllText(Path.Combine(folder, MacJson.SystemFile), MacJson.Write(_system));
    }
}
