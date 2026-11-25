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

    // The channel's offer, the file it serves and the policy; what msiexec answers; and what the install came to, with the
    // bytes msiexec got and whether anyone could change them while it ran.
    [Theory]
    [InlineData("offered", 0, MachineInstallOutcome.Installed, true)]
    [InlineData("offered", 3010, MachineInstallOutcome.Installed, true)]
    [InlineData("offered", 1223, MachineInstallOutcome.Refused, true)]
    [InlineData("offered", 1603, MachineInstallOutcome.Failed, true)]
    [InlineData("other bytes", 0, MachineInstallOutcome.Failed, false)]
    [InlineData("no msi", 0, MachineInstallOutcome.DownloadPage, false)]
    [InlineData("older version", 0, MachineInstallOutcome.DownloadPage, false)]
    [InlineData("policy off", 0, MachineInstallOutcome.DownloadPage, false)]
    public async Task InstallForEveryoneRunsOnlyTheCheckedMsi(string channel, int answer, MachineInstallOutcome outcome, bool ran)
    {
        await using var pc = new UpdatePc();
        var package = new Package(new Uri(pc.Channel.Address, "msis/thermi.msi"), Msi.Length, Convert.ToHexStringLower(SHA256.HashData(Msi)));
        pc.Channel.Serve("/v1/msis/thermi.msi", channel == "other bytes" ? [.. Msi.Reverse()] : Msi);
        var offered = channel == "older version" ? new ReleaseVersion(0, 1, 0) : UpdatePc.Built;
        pc.Publish(pc.Offer(offered, pc.Zip(offered)) with
        {
            Msis = channel == "no msi" ? new Dictionary<string, Package>() : new Dictionary<string, Package> { ["x64"] = package },
        });
        pc.Policy = channel == "policy off" ? (0, null) : (null, null);
        using var client = new ManifestClient(pc.Clock);
        var host = new UpdateHost(() => pc.Policy, () => false, _ => { });
        var log = new FileLog(Path.Combine(pc.Top, "thermi-sensors.log"), pc.Clock);
        var install = new MachineInstaller(pc.Build(), pc.Folder(), client, host, pc.Clock, log);
        byte[]? seen = null;
        var changed = true;

        await install.RunAsync(
            async path =>
            {
                seen = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
                changed = Writable(path);
                return answer;
            },
            TestContext.Current.CancellationToken);

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
