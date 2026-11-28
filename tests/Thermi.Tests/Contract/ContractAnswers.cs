using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Contract;

/// <summary>The contract's documents as Core answers them, from every recording, which the schemas and the SDK must both read.</summary>
internal static class ContractAnswers
{
    // Every answer of status, read, sensors and watch the recordings make, one snapshot or many, and every way status ends.
    public static IEnumerable<(string Schema, string Json)> All()
    {
        string[] recordings = [TestRecordings.Admin, TestRecordings.NoAdmin, TestRecordings.IntelAdmin, TestRecordings.NetDisk];
        var config = new ThermiConfig();
        foreach (var snapshot in recordings.Select(TestRecordings.Read))
        {
            var withFan = Done(ItemEdits.UseCpuFan(config, 0, snapshot, snapshot.Sensors.Last(sensor => sensor.Type == "Fan").Id));
            yield return ("read", AgentJson.Write(AgentReads.Read([snapshot], withFan, []) with { At = DateTimeOffset.UnixEpoch }));
            yield return ("read", AgentJson.Write(AgentReads.Read([snapshot], withFan, ["cpu.temperature", "item:nobody", "no.such.metric"])));
            yield return ("sensors", AgentJson.Write(AgentViews.Sensors(snapshot, config, search: null, kinds: [])));
            yield return ("sensors", AgentJson.Write(AgentViews.Sensors(snapshot, config, "cpu", [SensorKind.Temperature, SensorKind.Fan])));
            var running = new AgentFacts(ServiceState.Running, Installed: true, Taken: false, snapshot, ConfigProblem.Missing);
            yield return ("status", AgentJson.Write(AgentStatus.Of(running)));
            yield return ("status", AgentJson.Write(AgentStatus.Of(running with { Settings = ConfigProblem.Locked("locked"), SensorsOnly = true })));
            yield return ("status", AgentJson.Write(AgentStatus.Of(running with { Os = HostOs.Linux, OverlayCanShow = false })));
        }

        foreach (var series in new[] { TestRecordings.CpuLoad, TestRecordings.IntelCpuLoad }.Select(TestFiles.Series))
        {
            yield return ("read", AgentJson.Write(AgentReads.Read([.. series.Take(10)], config, [])));
        }

        // Each problem the service can have, with a settings file that cannot be used, an overlay and a sensors-only install,
        // on Windows and on Linux, and a per-user install that is not running.
        (ServiceState State, bool Installed, bool Taken, bool SensorsOnly)[] services =
        [
            (ServiceState.Stopped, true, false, false), (ServiceState.Stale, true, false, false), (ServiceState.Stopped, true, true, false),
            (ServiceState.Stopped, false, false, true), (ServiceState.Stopped, true, false, true),
        ];
        foreach (var (state, installed, taken, sensorsOnly) in services)
        {
            var facts = new AgentFacts(state, installed, taken, Latest: null, ConfigProblem.Locked("locked"))
            {
                Overlay = !sensorsOnly,
                SensorsOnly = sensorsOnly,
            };
            yield return ("status", AgentJson.Write(AgentStatus.Of(facts)));
            yield return ("status", AgentJson.Write(AgentStatus.Of(facts with { Os = HostOs.Linux })));
        }

        var perUser = new AgentFacts(ServiceState.Stopped, Installed: false, Taken: false, Latest: null, ConfigProblem.Missing) { PerUser = true };
        yield return ("status", AgentJson.Write(AgentStatus.Of(perUser)));
    }
}
