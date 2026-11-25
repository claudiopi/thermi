using Thermi.Core.Agents;
using Thermi.Core.Editing;
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
            yield return ("sensors", AgentJson.Write(AgentViews.Sensors(snapshot, config, search: null, kinds: [])));
            var running = new AgentFacts(ServiceState.Running, Installed: true, Taken: false, snapshot, Settings: null);
            yield return ("status", AgentJson.Write(AgentStatus.Of(running)));
        }

        foreach (var series in new[] { TestRecordings.CpuLoad, TestRecordings.IntelCpuLoad }.Select(TestFiles.Series))
        {
            yield return ("read", AgentJson.Write(AgentReads.Read([.. series.Take(10)], config, [])));
        }

        foreach (var state in Enum.GetValues<ServiceState>())
        {
            var facts = new AgentFacts(
                state, Installed: state != ServiceState.Stale, Taken: state == ServiceState.Stopped, Latest: null, ConfigProblem.Locked("locked"));
            yield return ("status", AgentJson.Write(AgentStatus.Of(facts with { Overlay = true, SensorsOnly = state == ServiceState.Running })));
        }
    }
}
