extern alias sensors;

using sensors::Thermi.SensorHost.Install;
using Thermi.Core.Updates;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// <c>thermi-sensors --adopt</c> and <c>--remove</c>, which the MSI and scripts/install.ps1 run as SYSTEM or an
/// administrator, on a PC that runs <see cref="UpdatePc.Running"/>. The service manager and the registry are faked, so no
/// service on this PC is touched.
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
    }

    private static string Sensors => "thermi-sensors";

    private static string Elevated => "thermi-elevated";

    private string Setup => Path.Combine(_pc.Top, Installation.Setup);

    // What was there before: nothing, a build from before the version folders, a later version an update switched to, or
    // this version built again with other files.
    [Theory]
    [InlineData("nothing", "0.9.0", "0.9.0")]
    [InlineData("a flat build", "0.9.0", "0.9.0")]
    [InlineData("a later version", "0.9.5", "0.9.5")]
    [InlineData("this version built again", "0.9.0", "0.9.0")]
    public void AnInstallRunsItsVersionBesideTheOthers(string before, string runs, string shown)
    {
        var flat = Path.Combine(_pc.Top, "runtimes", "thermi.dll");
        switch (before)
        {
            case "nothing":
                _system.Images.Clear();
                break;
            case "a flat build":
                Directory.CreateDirectory(Path.GetDirectoryName(flat)!);
                File.WriteAllText(flat, "flat");
                _system.Images[Sensors] = $"\"{Path.Combine(_pc.Top, "thermi-sensors.exe")}\"";
                break;
            case "a later version":
                _pc.Lay(Later);
                _system.Images[Sensors] = Service(Later);
                break;
            default:
                _pc.Lay(UpdatePc.Built);
                File.WriteAllText(Path.Combine(_pc.Top, UpdatePc.Built.ToString(), "thermi.exe"), "built before");
                break;
        }

        var exit = Adopt();

        var version = ReleaseVersion.Parse(runs)!;
        var laid = File.ReadAllText(Path.Combine(_pc.Top, runs, "thermi.exe"));
        Assert.Equal(
            (0, Service(version), UpdatePc.Launcher("thermi", version == UpdatePc.Built ? version : UpdatePc.Running), $"thermi {runs}", false, shown),
            (exit, _system.Images[Sensors], _pc.LauncherAtTop("thermi"), laid, File.Exists(flat), _system.Shown));
    }

    // Each step the service manager may refuse, in the order the install takes them: the thermi installed before keeps
    // running, its launchers at the top.
    [Theory]
    [InlineData("config")]
    [InlineData("failure")]
    [InlineData("failureflag")]
    [InlineData("privs")]
    [InlineData("sdset")]
    public void ARefusedStepLeavesTheThermiInstalledBefore(string refused)
    {
        _system.Refuses = refused;

        var exit = Adopt();

        Assert.Equal(
            (1, Service(UpdatePc.Running), UpdatePc.Launcher("thermi", UpdatePc.Running), true, refused),
            (exit, _system.Images[Sensors], _pc.LauncherAtTop("thermi"), _system.Running, _system.Refused));
    }

    [Fact]
    public void ARemoveTakesThermiOffButTheFolderItRunsFrom()
    {
        _system.Sources.Add(Sensors);
        _system.Shown = "0.8.0";

        new Installation(_pc.Folder(), _system, _said.Add).Remove(UpdatePc.Built, Setup);

        var left = string.Join(" ", Directory.EnumerateFileSystemEntries(_pc.Top).Select(Path.GetFileName));
        Assert.Equal((Installation.Setup, 0, 0, null, true), (left, _system.Images.Count, _system.Sources.Count, _system.Shown, _system.Ended));
    }

    public ValueTask DisposeAsync() => _pc.DisposeAsync();

    private int Adopt() => new Installation(_pc.Folder(), _system, _said.Add).Adopt(UpdatePc.Built, Setup, "{PRODUCT}");

    private string Service(ReleaseVersion version) => $"\"{Path.Combine(_pc.Top, version.ToString(), "thermi-sensors.exe")}\"";

    /// <summary>The service manager and registry as the install changes them: an image path per service, set only when a step is taken.</summary>
    private sealed class FakeSystem : ISystemSetup
    {
        public Dictionary<string, string> Images { get; } = [];

        public HashSet<string> Sources { get; } = [];

        public string? Refuses { get; set; }

        public string? Refused { get; private set; }

        public string? Shown { get; set; }

        public bool Running { get; private set; } = true;

        public bool Ended { get; private set; }

        public string? ImagePath(string service) => Images.GetValueOrDefault(service);

        public void Sc(params string[] arguments)
        {
            if (arguments[0] == Refuses)
            {
                Refused ??= arguments[0];
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

        public void InstalledApp(string? product, ReleaseVersion version, string top) => Shown = product is null ? null : version.ToString();

        public void EndOverlays(string top) => Ended = true;
    }
}
