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

    // The MSI offered; one while a switch holds the state, when no outcome is written over it; one whose prompt was
    // refused, 1223; a manifest that cannot be read and the policy's Updates = 0, as the host reads them; a file of other
    // bytes than the manifest signed; and one thermi's certificate did not sign, in a release build: what the install came
    // to, with the bytes msiexec got and whether anyone could change them while it ran. msiexec runs under the updater's
    // lock, and what follows only a good install runs before it is let go. Which MSI a manifest offers, and what each exit
    // code comes to, are Core's InstallForEveryone rows.
    [Theory]
    [InlineData("offered", MachineInstallOutcome.Installed, true)]
    [InlineData("switch held", null, true)]
    [InlineData("refused", MachineInstallOutcome.Refused, true)]
    [InlineData("no manifest", MachineInstallOutcome.Failed, false)]
    [InlineData("policy off", MachineInstallOutcome.DownloadPage, false)]
    [InlineData("other bytes", MachineInstallOutcome.Failed, false)]
    [InlineData("unsigned", MachineInstallOutcome.Failed, false)]
    public async Task InstallForEveryoneRunsOnlyTheCheckedMsi(string channel, MachineInstallOutcome? outcome, bool ran)
    {
        await using var pc = new UpdatePc();
        var package = new Package(new Uri(pc.Channel.Address, "msis/thermi.msi"), Msi.Length, Convert.ToHexStringLower(SHA256.HashData(Msi)));
        pc.Channel.Serve("/v1/msis/thermi.msi", channel == "other bytes" ? [.. Msi.Reverse()] : Msi);
        if (channel == "policy off")
        {
            pc.Policy = (0, null);
        }

        if (channel != "no manifest")
        {
            pc.Publish(pc.Offer(UpdatePc.Built, pc.Zip(UpdatePc.Built)) with { Msis = new Dictionary<string, Package> { ["x64"] = package } });
        }
        using var client = new ManifestClient(pc.Clock);
        var host = new UpdateHost(() => pc.Policy, () => false, _ => { });
        var log = new FileLog(Path.Combine(pc.Top, "thermi-sensors.log"), pc.Clock);
        using var busy = new SemaphoreSlim(1, 1);
        var install = new MachineInstaller(pc.Build(catalog: channel == "unsigned"), pc.Folder(), client, host, pc.Clock, log, busy);
        byte[]? seen = null;
        var changed = true;
        var held = -1;
        var worked = false;

        // A switch's watchdog holding the lock throughout: the install runs, and writes nothing over the switch's state.
        await using var watchdog = channel == "switch held"
            ? new FileStream(Path.Combine(pc.Folder().Update, "switch.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None)
            : null;

        var running = install.RunAsync(
            async path =>
            {
                seen = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
                changed = Writable(path);
                held = busy.CurrentCount;
                return channel == "refused" ? 1223 : 0;
            },
            TestContext.Current.CancellationToken,
            worked: () => worked = busy.CurrentCount == 0);

        // The clock steps on while the install waits for the lock, as the time it gives each try passes.
        while (!running.IsCompleted)
        {
            pc.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(running, Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, TestContext.Current.CancellationToken));
        }

        var returned = await running;

        Assert.Equal((outcome, ran, ran), (pc.State.MachineInstall?.Outcome, seen?.SequenceEqual(Msi) == true, ran && !changed));

        // msiexec runs under the updater's lock, and what follows a good install runs before it is let go.
        Assert.Equal((outcome ?? MachineInstallOutcome.Installed, ran ? 0 : -1, returned == MachineInstallOutcome.Installed), (returned, held, worked));
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
