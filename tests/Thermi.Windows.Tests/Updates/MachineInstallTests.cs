extern alias sensors;

using System.Security.Cryptography;
using sensors::Thermi.SensorHost.Updates;
using sensors::Thermi.SensorHost.User;
using Thermi.Core.Install;
using Thermi.Core.Updates;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// Spec 004, A22: "install for everyone" from a per-user install, on a channel served from this PC and signed with the
/// test's keys; msiexec is the test's own, which sees the file it would install and answers as Windows' would.
/// </summary>
public sealed class MachineInstallTests
{
    private static readonly byte[] Msi = RandomNumberGenerator.GetBytes(4096);

    // The channel, the file it serves and the policy; what msiexec answers; and what the install came to, with the bytes
    // msiexec got and whether anyone could change them while it ran, no outcome written while a switch holds the state, and
    // in a release build none run that thermi's certificate did not sign.
    // Which MSI a manifest offers is Core's, and its tests'.
    [Theory]
    [InlineData("offered", 0, MachineInstallOutcome.Installed, true)]
    [InlineData("offered", 1223, MachineInstallOutcome.Refused, true)]
    [InlineData("other bytes", 0, MachineInstallOutcome.Failed, false)]
    [InlineData("no manifest", 0, MachineInstallOutcome.Failed, false)]
    [InlineData("policy off", 0, MachineInstallOutcome.DownloadPage, false)]
    [InlineData("switch held", 0, null, true)]
    [InlineData("unsigned", 0, MachineInstallOutcome.Failed, false)]
    public async Task InstallForEveryoneRunsOnlyTheCheckedMsi(string channel, int answer, MachineInstallOutcome? outcome, bool ran)
    {
        await using var pc = new UpdatePc();
        var package = new Package(new Uri(pc.Channel.Address, "msis/thermi.msi"), Msi.Length, Convert.ToHexStringLower(SHA256.HashData(Msi)));
        pc.Channel.Serve("/v1/msis/thermi.msi", channel == "other bytes" ? [.. Msi.Reverse()] : Msi);
        if (channel != "no manifest")
        {
            pc.Publish(pc.Offer(UpdatePc.Built, pc.Zip(UpdatePc.Built)) with { Msis = new Dictionary<string, Package> { ["x64"] = package } });
        }

        pc.Policy = channel == "policy off" ? (0, null) : (null, null);
        using var client = new ManifestClient(pc.Clock);
        var host = new UpdateHost(() => pc.Policy, () => false, _ => { });
        var log = new FileLog(Path.Combine(pc.Top, "thermi-sensors.log"), pc.Clock);
        using var busy = new SemaphoreSlim(1, 1);
        var install = new MachineInstaller(pc.Build(catalog: channel == "unsigned"), pc.Folder(), client, host, pc.Clock, log, busy);
        byte[]? seen = null;
        var changed = true;

        // A switch's watchdog holding the lock throughout: the install runs, and writes nothing over the switch's state.
        await using var watchdog = channel == "switch held"
            ? new FileStream(Path.Combine(pc.Folder().Update, "switch.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None)
            : null;

        var running = install.RunAsync(
            async path =>
            {
                seen = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
                changed = Writable(path);
                return answer;
            },
            TestContext.Current.CancellationToken);

        // The clock steps on while the install waits for the lock, as the time it gives each try passes.
        while (!running.IsCompleted)
        {
            pc.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(running, Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, TestContext.Current.CancellationToken));
        }

        await running;

        Assert.Equal((outcome, ran, ran), (pc.State.MachineInstall?.Outcome, seen?.SequenceEqual(Msi) == true, ran && !changed));
        Assert.Empty(Directory.EnumerateFiles(pc.Folder().Update, "machine-*"));
    }

    private static bool Writable(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
