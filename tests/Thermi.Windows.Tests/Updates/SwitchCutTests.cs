extern alias sensors;

using sensors::Thermi.SensorHost.Updates;
using Thermi.Core.Updates;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// A switch cut by a power cut or a crash at each of its steps, and the service that starts next: the version Windows
/// starts is the one the service's image path named at the cut, which the live run on a real service proves (A15); here,
/// what that service makes of the folder it finds.
/// </summary>
public sealed class SwitchCutTests
{
    // A lock file a cut left on disk holds nothing, so the host that finishes or undoes the switch still starts; one a
    // watchdog holds open does.
    [Fact]
    public async Task ASwitchLockACutLeftBehindHoldsNothing()
    {
        await using var pc = new UpdatePc();
        var folder = pc.Folder();
        var switchLock = new SwitchLock(folder, pc.Clock);
        var path = Path.Combine(folder.Update, "switch.lock");
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);

        var left = switchLock.Held();
        bool held;
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            held = switchLock.Held();
        }

        Assert.Equal((false, true), (left, held));
    }

    // A switch cut with one of the new version's launchers placed, and Windows starting the new version next or the old
    // one: the PC runs whole the version Windows started, each launcher replaced while an overlay runs from the other.
    // What the state makes of the cut, done or tried once more, is Core's SwitchHealth rows.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASwitchCutAtAnyStepLeavesOneVersionWhole(bool startsNew)
    {
        await using var pc = new UpdatePc();
        pc.Lay(UpdatePc.Built);
        pc.Folder().Write(SwitchHealth.Begin(pc.State with { Ready = UpdatePc.Built }, UpdatePc.Built));
        var placed = Unpacker.Programs[0];
        File.Copy(Path.Combine(pc.Top, UpdatePc.Built.ToString(), "launchers", placed + ".exe"), Path.Combine(pc.Top, placed + ".exe"), overwrite: true);

        // An overlay that runs from the launcher at the top, which Windows lets be renamed but not deleted or rewritten.
        await using var running = new FileStream(Path.Combine(pc.Top, "thermi.exe"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var before = Text(running);
        var started = startsNew ? UpdatePc.Built : UpdatePc.Running;
        using var updater = pc.Updater(pc.Build(version: started));

        updater.Started();

        Assert.Equal(started, pc.State.Installed);
        Assert.All(Unpacker.Programs, program => Assert.Equal(UpdatePc.Launcher(program, started), pc.LauncherAtTop(program)));
        Assert.Equal(before, Text(running));
        Assert.Equal([UpdatePc.Running, UpdatePc.Built], pc.Versions);
    }

    private static string Text(FileStream file)
    {
        file.Position = 0;
        using var reader = new StreamReader(file, leaveOpen: true);
        return reader.ReadToEnd();
    }

    // The service keeps what it runs, the installed version, the one before, the one ready and the one the launcher at the
    // top still starts, as a program holding it left it; a folder a process still runs from stays until a later prune.
    [Fact]
    public async Task OnlyFoldersOfVersionsNeitherKeptNorRunningAreDeleted()
    {
        await using var pc = new UpdatePc();
        ReleaseVersion[] laid = [new(0, 4, 0), new(0, 5, 0), new(0, 6, 0), new(0, 7, 0), UpdatePc.Built];
        foreach (var version in laid)
        {
            pc.Lay(version);
        }

        pc.Folder().Write(pc.State with { Previous = new ReleaseVersion(0, 7, 0), Ready = UpdatePc.Built });
        pc.Publish(pc.Offer(UpdatePc.Built, pc.Zip(UpdatePc.Built)));
        pc.Folder().PlaceLaunchers(new ReleaseVersion(0, 4, 0));
        using var top = new FileStream(Path.Combine(pc.Top, "thermi.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var held = File.OpenRead(Path.Combine(pc.Top, "0.6.0", "thermi.exe"));
        using var updater = pc.Updater();

        await updater.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new(0, 4, 0), new(0, 6, 0), new(0, 7, 0), UpdatePc.Running, UpdatePc.Built], pc.Versions);
    }
}
