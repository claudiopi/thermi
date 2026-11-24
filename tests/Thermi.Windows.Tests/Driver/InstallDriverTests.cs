using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Thermi.Commands;

namespace Thermi.Windows.Tests.Driver;

/// <summary>
/// "Install driver" as administrator, with the download, the installer and the service manager faked: only the pinned
/// file runs, checked for its size, hash and signature, and the sensor service starts again after it.
/// </summary>
public sealed class InstallDriverTests : IDisposable
{
    private static readonly byte[] Pinned = "the driver's official installer"u8.ToArray();

    private readonly string _folder = Directory.CreateTempSubdirectory("thermi-driver-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // Each row: what the download gives, whether it is signed, what the installer exits with (none when it never ends),
    // the service's states as the manager gives them, one a poll, and the mode's exit code.
    [Theory]
    [InlineData("pinned", true, 0, "RSSR", InstallDriver.Installed)]
    [InlineData("pinned", true, 3010, "RSSR", InstallDriver.Installed)]
    [InlineData("pinned", true, 0, "M", InstallDriver.ServiceMissing)]
    [InlineData("pinned", true, 0, "RSS", InstallDriver.SensorsFailed)]
    [InlineData("pinned", true, 0, "RC", InstallDriver.SensorsFailed)]
    [InlineData("longer", true, 0, "RSSR", InstallDriver.NotTrusted)]
    [InlineData("changed", true, 0, "RSSR", InstallDriver.NotTrusted)]
    [InlineData("pinned", false, 0, "RSSR", InstallDriver.NotTrusted)]
    [InlineData("none", true, 0, "RSSR", InstallDriver.NotDownloaded)]
    [InlineData("pinned", true, 1603, "RSSR", InstallDriver.NotInstalled)]
    [InlineData("pinned", true, null, "RSSR", InstallDriver.NotInstalled)]
    [InlineData("unsafe folder", true, 0, "RSSR", InstallDriver.FolderUnsafe)]
    public async Task OnlyThePinnedInstallerRunsAndTheSensorsStartAgain(string download, bool trusted, int? installer, string states, int exit)
    {
        var time = new FakeTimeProvider();
        var setup = new DriverSetup(new Uri("https://example.invalid/setup.exe"), Pinned.Length, Convert.ToHexString(SHA256.HashData(Pinned)), ["-install", "-silent"]);
        var ran = new FakeInstaller(trusted, installer);
        var parts = new DriverParts(setup, () => download == "unsafe folder" ? null : _folder, new FakeDownload(download), ran, new CommandLineTests.FakeServices(states, starts: true));

        var run = InstallDriver.RunAsync(parts, time);
        for (var waited = TimeSpan.Zero; !run.IsCompleted && waited <= TimeSpan.FromSeconds(30); waited += StartSensors.Poll)
        {
            time.Advance(StartSensors.Poll);
        }

        Assert.True(run.IsCompleted);
        Assert.Equal((exit, exit is InstallDriver.Installed or InstallDriver.ServiceMissing or InstallDriver.SensorsFailed or InstallDriver.NotInstalled), (await run, ran.Ran));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    /// <summary>The file at the URL as each row has it: the pinned one, one byte longer, changed in its last byte, or a failed download.</summary>
    // Written at once, so the run moves only as the test's clock does.
    private sealed class FakeDownload(string kind) : IDriverDownload
    {
        public Task<bool> DownloadAsync(Uri url, Stream into, long size, CancellationToken cancel)
        {
            if (kind == "none")
            {
                return Task.FromResult(false);
            }

            byte[] bytes = kind switch
            {
                "longer" => [.. Pinned, 0],
                "changed" => [.. Pinned[..^1], (byte)(Pinned[^1] ^ 1)],
                _ => Pinned,
            };
#pragma warning disable VSTHRD103 // Written at once: an asynchronous write would finish on a thread of its own, out of the test clock's step.
            into.Write(bytes);
#pragma warning restore VSTHRD103
            return Task.FromResult(true);
        }
    }

    /// <summary>The installer: signed or not, and its exit code, or none when it never ends; it runs only from the folder, with the file still there.</summary>
    private sealed class FakeInstaller(bool trusted, int? exit) : IDriverInstaller
    {
        public bool Ran { get; private set; }

        public bool Signed(FileStream file) => trusted;

        public Task<int?> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan wait, TimeProvider time)
        {
            Ran = File.Exists(path);
            return Task.FromResult(exit);
        }
    }
}
