using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Thermi.Windows.Commands;

namespace Thermi.Windows.Tests.Driver;

/// <summary>
/// The driver's installer as Windows runs it, here as the test's user: real processes, with the wait on the test's clock.
/// </summary>
public sealed class WindowsInstallerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("thermi-installer-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // The installer inherits the elevated mode's environment, which the thermi-elevated service cleaned
    // (ElevatedServiceTests), with its own folder, which only administrators write, for its temp files.
    [Fact]
    public async Task TheInstallerKeepsItsTempFilesInItsOwnFolder()
    {
        var seen = Path.Combine(_folder, "environment.txt");
        var cmd = Path.Combine(_folder, "setup.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd);

        var exit = await new WindowsInstaller().RunAsync(cmd, ["/c", "set>" + seen], TimeSpan.FromMinutes(1), TimeProvider.System);

        var variables = (await File.ReadAllLinesAsync(seen, TestContext.Current.CancellationToken))
            .Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);
        Assert.Equal((0, _folder, _folder), (exit, variables["TEMP"], variables["TMP"]));
    }

    // One that outlives its wait would go on as administrator, with the window saying it failed: it ends, children too.
    [Fact]
    public async Task AnInstallerPastItsWaitEndsWithItsChildren()
    {
        var child = $"thermi-test-child-{Guid.NewGuid():N}";
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), Path.Combine(_folder, child + ".exe"));
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var time = new FakeTimeProvider();

        string[] arguments = ["/c", Path.Combine(_folder, child + ".exe"), "-n", "600", "127.0.0.1"];
        var run = new WindowsInstaller().RunAsync(cmd, arguments, TimeSpan.FromMinutes(2), time);
        for (var tries = 0; Process.GetProcessesByName(child).Length == 0 && tries < 100; tries++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System, TestContext.Current.CancellationToken);
        }

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await run);
        Assert.All(Process.GetProcessesByName(child), process => Assert.True(process.WaitForExit(TimeSpan.FromSeconds(5))));
    }
}
