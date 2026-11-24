using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Thermi.Commands;

namespace Thermi.Windows.Tests.Driver;

/// <summary>
/// The driver's installer as Windows runs it, here as the test's user: real processes, with the wait on the test's clock.
/// </summary>
public sealed class WindowsInstallerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("thermi-installer-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // The installer runs as administrator from what the user's environment would say: a PATH or TEMP of the user's
    // finds the user's programs and DLLs first. It gets Windows' own folders, and its own folder for temp files.
    [Fact]
    public async Task TheInstallerSeesNoneOfTheUsersEnvironment()
    {
        var seen = Path.Combine(_folder, "environment.txt");
        var cmd = Path.Combine(_folder, "setup.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd);

        var exit = await new WindowsInstaller().RunAsync(cmd, ["/c", "set>" + seen], TimeSpan.FromMinutes(1), TimeProvider.System);

        var variables = (await File.ReadAllLinesAsync(seen, TestContext.Current.CancellationToken))
            .Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, exit);
        Assert.Equal(Environment.SystemDirectory, variables["PATH"]);
        Assert.Equal((_folder, _folder), (variables["TEMP"], variables["TMP"]));
        Assert.DoesNotContain(
            variables.Keys,
            name => name is "USERPROFILE" or "APPDATA" or "LOCALAPPDATA" || name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase));
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
