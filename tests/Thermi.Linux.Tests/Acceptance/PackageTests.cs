using System.Text.Json.Nodes;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// thermi's packages and their updates (<c>docs/specs/006-linux/spec.md</c>, R1, R25 and R26), in clean containers of each
/// distribution, from the packages one build packed, in <see cref="Packages"/>.
/// </summary>
public sealed class PackageTests
{
    /// <summary>Where the build puts the packages it packed, and the repositories the levers wrote.</summary>
    public static readonly string Packages = Path.Combine(Built.Root, "artifacts", "linux");

    // What each package puts on the PC, as spec.md lists it: the sensors' service, its units and rule, and thermi-cli on
    // every PATH; then the overlay, its window, its user unit, its launcher's entry and the GNOME extension.
    private static readonly string[] SensorsFiles =
    [
        "/usr/bin/thermi-cli", "/usr/lib/thermi/thermi-sensors", "/usr/lib/systemd/system/thermi-sensors.service",
        "/usr/lib/systemd/system/thermi-sensors.socket", "/usr/lib/udev/rules.d/60-thermi-sensors.rules", "/usr/lib/sysusers.d/thermi-sensors.conf",
    ];

    private static readonly string[] ThermiFiles =
    [
        "/usr/bin/thermi", "/usr/lib/systemd/user/thermi.service", "/usr/share/applications/io.github.claudiopi.thermi.desktop",
        "/usr/share/gnome-shell/extensions/thermi@claudiopi.github.io/extension.js",
        "/usr/share/gnome-shell/extensions/thermi@claudiopi.github.io/metadata.json",
    ];

    public enum Distribution
    {
        Ubuntu2404,
        Ubuntu2604,
        Debian13,
        Fedora,
        Arch,
    }

    [Theory]
    [InlineData(Distribution.Ubuntu2404, Skip = "Waiting for its code")]
    [InlineData(Distribution.Ubuntu2604, Skip = "Waiting for its code")]
    [InlineData(Distribution.Debian13, Skip = "Waiting for its code")]
    [InlineData(Distribution.Fedora, Skip = "Waiting for its code")]
    [InlineData(Distribution.Arch, Skip = "Waiting for its code")]
    public async Task ThePackagesInstallWhatTheySay(Distribution row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var tool = Tool.Of(row);
        await using var pc = await Container.StartAsync(session, tool.Image, Packages, cancel);

        await pc.MustAsync(tool.Install("thermi-sensors"), cancel);
        var cli = await pc.RunAsync("useradd -m person && su person -c 'thermi-cli status --schema 1'", cancel);
        await pc.MustAsync(tool.Install("thermi"), cancel);
        var files = (await pc.MustAsync($"{tool.Files("thermi-sensors")} && {tool.Files("thermi")}", cancel)).Split('\n');
        var repository = await pc.RunAsync("ls /etc/apt/sources.list.d/thermi.sources /etc/yum.repos.d/thermi.repo 2>/dev/null", cancel);
        // Installed again with the network cut, once its libraries are on the PC, it downloads nothing.
        await pc.MustAsync(tool.Remove(), cancel);
        await pc.OfflineAsync(cancel);
        await pc.MustAsync($"{tool.Install("thermi-sensors")} && {tool.Install("thermi")}", cancel);
        await pc.MustAsync("mkdir -p /home/person/.config/thermi && echo '{}' > /home/person/.config/thermi/config.json", cancel);
        await pc.MustAsync(tool.Remove(), cancel);
        var kept = await pc.RunAsync("cat /home/person/.config/thermi/config.json", cancel);

        Assert.Equal(3, cli.Exit);
        Assert.Superset(SensorsFiles.Concat(ThermiFiles).ToHashSet(StringComparer.Ordinal), files.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(row == Distribution.Arch ? 1 : 0, repository.Exit == 0 ? 0 : 1);
        Assert.Equal("{}", kept.Out.Trim());
    }

    [Theory]
    [InlineData(Distribution.Ubuntu2604, Skip = "Waiting for its code")]
    [InlineData(Distribution.Fedora, Skip = "Waiting for its code")]
    public async Task UpdatesComeFromThermisRepository(Distribution row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var tool = Tool.Of(row);
        var channel = Path.Combine(session.Home, "channel");
        await using var pc = await Container.BootAsync(session, tool.Image, session.Home, cancel);
        var levers = new Levers(session, channel);
        await levers.RunAsync(["publish", "1.0.0", "--channel", "stable"], cancel);
        await pc.MustAsync($"{tool.Install("thermi-sensors")} && {tool.Served()} && systemctl start thermi-sensors.service", cancel);
        const string Serving = "systemctl show -p MainPID --value thermi-sensors.service";
        var before = await pc.MustAsync(Serving, cancel);

        await levers.RunAsync(["publish", "1.1.0", "--channel", "stable"], cancel);
        var newer = await UpdatedAsync(pc, tool, cancel);
        var restarted = await pc.MustAsync(Serving, cancel);
        await levers.RunAsync(["rollback", "--channel", "stable"], cancel);
        var rolledBack = await UpdatedAsync(pc, tool, cancel);
        // The version before, published under a higher number, so every PC takes it.
        var runs = await pc.MustAsync("/usr/lib/thermi/thermi-sensors --version", cancel);
        await levers.RunAsync(["disable", "--channel", "stable"], cancel);
        var disabled = await UpdatedAsync(pc, tool, cancel);
        await levers.ServeForeignAsync(cancel);
        var foreign = await pc.RunAsync(tool.Update(), cancel);
        await levers.ServeOlderAsync(cancel);
        var older = await pc.RunAsync(tool.Update(), cancel);

        Assert.StartsWith("1.1.0", newer, StringComparison.Ordinal);
        Assert.NotEqual(before, restarted);
        Assert.NotEqual(newer, rolledBack);
        Assert.StartsWith("1.0.0", runs.Trim(), StringComparison.Ordinal);
        Assert.Equal(rolledBack, disabled);
        Assert.NotEqual(0, foreign.Exit);
        Assert.NotEqual(0, older.Exit);
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task TheOverlayHandsOverAfterAnUpdateOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        // The build in a folder of the session's, as a package puts it; the newer version over it, as the package changes it.
        var installed = Path.Combine(session.Home, "installed");
        Directory.CreateDirectory(installed);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            File.Copy(file, Path.Combine(installed, Path.GetFileName(file)));
        }

        using var units = await UserUnits.StartAsync(session, cancel, Path.Combine(installed, "thermi"));
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);
        await units.SystemctlAsync(["start", "thermi.service"], cancel);
        await units.PaintedAsync(cancel);
        var first = await units.SystemctlAsync(["show", "-p", "MainPID", "--value", "thermi.service"], cancel);
        var window = await session.StartAsync(Path.Combine(installed, "thermi"), ["settings"], cancel: cancel);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Packages, "newer")))
        {
            File.Copy(file, Path.Combine(installed, Path.GetFileName(file)), overwrite: true);
        }

        // Quiet, with no input and nothing changing on screen: the window hands over and ends, and the new overlay paints.
        await window.WaitForExitAsync(cancel);
        await units.PaintedAsync(cancel);
        var about = JsonNode.Parse((await session.RunAsync(Built.Program("thermi-cli"), ["status", "--schema", "1"], cancel: cancel)).Out)!;

        Assert.NotEqual(first, await units.SystemctlAsync(["show", "-p", "MainPID", "--value", "thermi.service"], cancel));
        Assert.Single(await units.ProcessesAsync(cancel));
        // About and status say the same of the version, as both read it from Core.
        Assert.Equal("system", (string?)about["updates"]);
    }

    // The version the system's updater installed, after it looked for updates.
    private static async Task<string> UpdatedAsync(Container pc, Tool tool, CancellationToken cancel)
    {
        await pc.MustAsync(tool.Update(), cancel);
        return (await pc.MustAsync(tool.Version("thermi-sensors"), cancel)).Trim();
    }

    /// <summary>A distribution's own package tool, as a person runs it.</summary>
    private sealed record Tool(string Image, string Format)
    {
        public static Tool Of(Distribution row) => row switch
        {
            Distribution.Ubuntu2404 => new(Container.Ubuntu2404, "deb"),
            Distribution.Ubuntu2604 => new(Container.Ubuntu2604, "deb"),
            Distribution.Debian13 => new(Container.Debian13, "deb"),
            Distribution.Fedora => new(Container.Fedora, "rpm"),
            Distribution.Arch => new(Container.Arch, "pkg.tar.zst"),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown distribution."),
        };

        // Debian names a package's architecture amd64, Fedora and Arch x86_64; the shell's glob drops the one that is not there.
        public string Install(string package) =>
            Container.Installs(Image, $"$(ls {Container.Work}/{package}[-_][0-9]*{{amd64,x86_64}}.{Format} 2>/dev/null)");

        public string Remove() => Format switch
        {
            "deb" => "apt-get remove -y thermi thermi-sensors",
            "rpm" => "dnf remove -y thermi thermi-sensors",
            _ => "pacman -R --noconfirm thermi thermi-sensors",
        };

        public string Files(string package) => Format switch
        {
            "deb" => $"dpkg -L {package}",
            "rpm" => $"rpm -ql {package}",
            _ => $"pacman -Qlq {package}",
        };

        public string Version(string package) => Format == "deb" ? $"dpkg-query -W -f '${{Version}}' {package}" : $"rpm -q --qf '%{{VERSION}}' {package}";

        public string Update() => Format == "deb" ? "apt-get update && apt-get upgrade -y" : "dnf upgrade -y --refresh";

        // The repository the package added, pointed at the levers' channel the test serves.
        public string Served() => Format == "deb"
            ? $"sed -i 's|^URIs:.*|URIs: file://{Container.Work}/channel/apt|' /etc/apt/sources.list.d/thermi.sources"
            : $"sed -i 's|^baseurl=.*|baseurl=file://{Container.Work}/channel/rpm|' /etc/yum.repos.d/thermi.repo";
    }

    /// <summary>The owner's levers of spec 004 on a channel's repositories, as the release workflow pulls them.</summary>
    private sealed class Levers(OwnSession session, string channel)
    {
        public async Task RunAsync(IEnumerable<string> args, CancellationToken cancel)
        {
            var pulled = await session.RunAsync(
                "pwsh", ["-NoProfile", "-File", Path.Combine(Built.Root, "scripts", "updates.ps1"), .. args, "--packages", Packages, "--repository", channel],
                cancel: cancel);
            Assert.True(pulled.Exit == 0, pulled.Error);
        }

        /// <summary>The channel signed by a key that is not thermi's.</summary>
        public Task ServeForeignAsync(CancellationToken cancel) =>
            RunAsync(["sign", "--key", Path.Combine(session.Home, "foreign.pem"), "--channel", "stable"], cancel);

        /// <summary>The channel as it was before the last lever, older than the one the PC holds.</summary>
        public Task ServeOlderAsync(CancellationToken cancel) => RunAsync(["restore", "--before-last", "--channel", "stable"], cancel);
    }
}
