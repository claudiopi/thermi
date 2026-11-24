extern alias sensors;

using sensors::Thermi.SensorHost.Install;
using sensors::Thermi.SensorHost.Updates;
using Thermi.Core.Updates;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// <c>thermi-sensors --adopt</c>, <c>--undo</c> and <c>--remove</c>, which the MSI and scripts/install.ps1 run as SYSTEM
/// or an administrator, on a PC that runs <see cref="UpdatePc.Running"/>. The service manager and the registry are faked,
/// so no service on this PC is touched.
/// </summary>
public sealed class InstallationTests : IAsyncDisposable
{
    private static readonly ReleaseVersion Later = new(0, 9, 5);

    private readonly UpdatePc _pc = new();
    private readonly FakeSystem _system = new();
    private readonly List<string> _said = [];

    public InstallationTests()
    {
        _pc.Lay(UpdatePc.Built);
        Directory.Move(Path.Combine(_pc.Top, UpdatePc.Built.ToString()), Setup);
        _system.Images[Sensors] = Service(UpdatePc.Running);
        _system.Images[Elevated] = Service(UpdatePc.Running) + " --elevated";
        _system.Shown = new InstalledApp("{OLD}", UpdatePc.Running.ToString());
        _system.Sources.Add(Sensors);
    }

    private static string Sensors => "thermi-sensors";

    private static string Elevated => "thermi-elevated";

    private string Setup => Path.Combine(_pc.Top, Installation.Setup);

    // What was there before: nothing, a build from before the version folders, a later version an update switched to,
    // this version built again with other files, or an earlier version ready, which the PC must not switch down to.
    [Theory]
    [InlineData("nothing", "0.9.0", "0.9.0")]
    [InlineData("an earlier version ready", "0.9.0", "0.9.0")]
    [InlineData("a flat build", "0.9.0", "0.9.0")]
    [InlineData("a later version", "0.9.5", "0.9.5")]
    [InlineData("this version built again", "0.9.0", "0.9.0")]
    public async Task AnInstallRunsItsVersionBesideTheOthers(string before, string runs, string shown)
    {
        var flat = Path.Combine(_pc.Top, "runtimes", "thermi.dll");
        switch (before)
        {
            case "nothing":
                Fresh();
                break;
            case "a flat build":
                Directory.CreateDirectory(Path.Combine(Setup, "runtimes"));
                await File.WriteAllTextAsync(Path.Combine(Setup, "runtimes", "thermi.dll"), "built", TestContext.Current.CancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(flat)!);
                await File.WriteAllTextAsync(flat, "flat", TestContext.Current.CancellationToken);
                _system.Images[Sensors] = $"\"{Path.Combine(_pc.Top, "thermi-sensors.exe")}\"";
                break;
            case "a later version":
                _pc.Lay(Later);
                _system.Images[Sensors] = Service(Later);
                break;
            case "an earlier version ready":
                _pc.Folder().Write(new UpdateState { Installed = UpdatePc.Running, Ready = new ReleaseVersion(0, 8, 5) });
                break;
            default:
                _pc.Lay(UpdatePc.Built);
                var built = Path.Combine(_pc.Top, UpdatePc.Built.ToString(), "thermi.exe");
                await File.WriteAllTextAsync(built, "built before", TestContext.Current.CancellationToken);
                break;
        }

        var exit = await AdoptAsync();

        var version = ReleaseVersion.Parse(runs)!;
        var laid = await File.ReadAllTextAsync(Path.Combine(_pc.Top, runs, "thermi.exe"), TestContext.Current.CancellationToken);
        Assert.Equal(
            (0, Service(version), UpdatePc.Launcher("thermi", version == UpdatePc.Built ? version : UpdatePc.Running), $"thermi {runs}", false, shown, null),
            (exit, _system.Images[Sensors], _pc.LauncherAtTop("thermi"), laid, File.Exists(flat), _system.Shown?.Version, _pc.Folder().Read().Ready));
    }

    // Each step that may fail, in the order the install takes them, over a thermi or on a PC with none: what was there
    // stays, services, launchers, versions, state and Installed apps' entry.
    [Theory]
    [InlineData("config", false)]
    [InlineData("failure", false)]
    [InlineData("privs", false)]
    [InlineData("environment", false)]
    [InlineData("sdset", false)]
    [InlineData("create", true)]
    [InlineData("failure", true)]
    public async Task AFailedStepLeavesWhatWasThere(string failing, bool fresh)
    {
        if (fresh)
        {
            Fresh();
        }

        var before = Snapshot();
        _system.Fails = failing;

        Assert.Equal((1, before), (await AdoptAsync(), Snapshot()));
    }

    // The MSI's rollback, after an install that went well, or that left a later version an update switched to running,
    // with what an install before it recorded still in update\.
    [Theory]
    [InlineData("a thermi")]
    [InlineData("nothing")]
    [InlineData("a later version")]
    public async Task AnInstallRolledBackLeavesWhatWasThere(string there)
    {
        if (there == "nothing")
        {
            Fresh();
        }
        else if (there == "a later version")
        {
            _pc.Lay(Later);
            _system.Images[Sensors] = Service(Later);
            _pc.Folder().PlaceLaunchers(Later);
            var earlier = new AdoptUndo(Service(UpdatePc.Running), null, VersionThere: true, State: null, App: null);
            Directory.CreateDirectory(_pc.Folder().Update);
            await File.WriteAllTextAsync(Path.Combine(_pc.Folder().Update, "adopt-undo.json"), earlier.ToJson(), TestContext.Current.CancellationToken);
        }

        var before = Snapshot();
        var installation = Install();
        var stop = TestContext.Current.CancellationToken;

        var adopted = await installation.AdoptAsync(UpdatePc.Built, Setup, "{PRODUCT}", stop);
        var undone = await installation.UndoAsync(UpdatePc.Built, Setup, stop);

        Assert.Equal((0, 0, before), (adopted, undone, Snapshot()));
    }

    // A watchdog switching versions holds the lock: the install waits for it rather than change the services under it.
    [Fact]
    public async Task AnInstallWaitsForASwitchUnderWay()
    {
        var before = Snapshot();
        var watchdog = await new SwitchLock(_pc.Folder(), _pc.Clock).HoldAsync(1, TestContext.Current.CancellationToken);
        var install = AdoptAsync();
        await _pc.Clock.StepAsync(TimeSpan.FromSeconds(1));
        var meanwhile = Snapshot();

        await watchdog!.DisposeAsync();
        await _pc.Clock.StepAsync(TimeSpan.FromSeconds(1));

        Assert.Equal((before, 0, Service(UpdatePc.Built)), (meanwhile, await install, _system.Images[Sensors]));
    }

    // From the MSI's own folder, and from a version's, as scripts/install.ps1 runs it; anything else is refused whole.
    [Theory]
    [InlineData("setup", "setup", 0)]
    [InlineData("0.8.0", "0.8.0 setup", 0)]
    [InlineData("elsewhere", "0.8.0 0.9.5 elsewhere setup thermi-sensors.exe thermi.exe update", 1)]
    public async Task ARemoveTakesThermiOffButTheFolderItRunsFrom(string own, string left, int exit)
    {
        _pc.Lay(Later);
        Directory.CreateDirectory(Path.Combine(_pc.Top, own));

        var removed = await Install().RemoveAsync(Path.Combine(_pc.Top, own), TestContext.Current.CancellationToken);

        var entries = string.Join(" ", Directory.EnumerateFileSystemEntries(_pc.Top).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal((exit, left), (removed, entries));
        Assert.Equal(exit == 0, _system.Images.Count == 0 && _system.Sources.Count == 0 && _system.Shown is null && _system.Ended);
    }

    public ValueTask DisposeAsync() => _pc.DisposeAsync();

    private Installation Install() => new(_pc.Folder(), _system, _pc.Clock, _said.Add);

    private Task<int> AdoptAsync() => Install().AdoptAsync(UpdatePc.Built, Setup, "{PRODUCT}", TestContext.Current.CancellationToken);

    // A PC thermi never ran on: no services, launchers, versions, state or entry.
    private void Fresh()
    {
        _system.Images.Clear();
        _system.Sources.Clear();
        _system.Shown = null;
        foreach (var entry in Directory.EnumerateFileSystemEntries(_pc.Top).Where(entry => entry != Setup))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private string Snapshot()
    {
        var images = string.Join(", ", _system.Images.OrderBy(image => image.Key, StringComparer.Ordinal).Select(image => $"{image.Key}={image.Value}"));
        var launcher = File.Exists(Path.Combine(_pc.Top, "thermi.exe")) ? _pc.LauncherAtTop("thermi") : "no launcher";
        var state = _pc.Folder().StateText() ?? "no state";
        var running = _system.Running && _system.Images.ContainsKey(Sensors);
        return $"{images} | {launcher} | {string.Join(" ", _pc.Versions)} | {state} | {_system.Shown} | {running} | {_system.Sources.Count}";
    }

    private string Service(ReleaseVersion version) => $"\"{Path.Combine(_pc.Top, version.ToString(), "thermi-sensors.exe")}\"";

    /// <summary>The service manager and registry as the install changes them: an image path per service, set only when a step is taken.</summary>
    private sealed class FakeSystem : ISystemSetup
    {
        public Dictionary<string, string> Images { get; } = [];

        public HashSet<string> Sources { get; } = [];

        public string? Fails { get; set; }

        public InstalledApp? Shown { get; set; }

        public bool Running { get; private set; } = true;

        public bool Ended { get; private set; }

        public string? ImagePath(string service) => Images.GetValueOrDefault(service);

        public void Sc(params string[] arguments)
        {
            if (arguments[0] == Fails)
            {
                throw new SetupRefusedException($"{arguments[0]} refused");
            }

            if (arguments[0] is "create" or "config")
            {
                Images[arguments[1]] = arguments[3];
            }
            else if (arguments[0] == "delete")
            {
                Images.Remove(arguments[1]);
            }
        }

        public void Stop(string service) => Running = false;

        public void Start(string service) => Running = true;

        public void Environment(string service, IReadOnlyList<string> variables)
        {
            if (Fails == "environment")
            {
                throw new UnauthorizedAccessException("the service's key refused");
            }
        }

        public void EventSource(string service, bool registered)
        {
            if (registered)
            {
                Sources.Add(service);
            }
            else
            {
                Sources.Remove(service);
            }
        }

        public InstalledApp? App() => Shown;

        public void App(InstalledApp? app, string top) => Shown = app;

        public void EndPrograms(string top) => Ended = true;
    }
}
