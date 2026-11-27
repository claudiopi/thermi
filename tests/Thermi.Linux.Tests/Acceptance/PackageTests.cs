using System.Text.Json.Nodes;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// thermi's packages and their updates (<c>docs/specs/006-linux/spec.md</c>, R1, R25 and R26), in clean containers of each
/// distribution, from the packages one build packed, in <see cref="Built.Packages"/>.
/// </summary>
public sealed class PackageTests
{
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

    // Holds a connection to the service, so it serves until the package restarts it; it says so once served a line.
    private const string Holder = """
        import signal, socket
        client = socket.socket(socket.AF_UNIX)
        client.connect('/run/thermi/sensors.sock')
        client.makefile().readline()
        print('held', flush=True)
        signal.pause()
        """;

    // A21's last two steps as written: refusing a foreign key, apt keeps what the PC holds and ends with 0, as apt and dnf
    // both do refusing an older repository; dnf alone fails on a foreign key.
    private const string Undecided = "Waiting for a decision: refused, a foreign or an older repository ends with 0";

    public enum Distribution
    {
        Ubuntu2404,
        Ubuntu2604,
        Debian13,
        Fedora,
        Arch,
    }

    [Theory]
    [InlineData(Distribution.Ubuntu2404)]
    [InlineData(Distribution.Ubuntu2604)]
    [InlineData(Distribution.Debian13)]
    [InlineData(Distribution.Fedora)]
    [InlineData(Distribution.Arch)]
    public async Task ThePackagesInstallWhatTheySay(Distribution row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var tool = Tool.Of(row);
        await using var pc = await Container.BootAsync(session, tool.Image, session.Home, cancel);
        await pc.MustAsync(tool.Prepare(), cancel);

        // thermi-cli reads the service, which its socket starts: what the package started, which its removal stops.
        await pc.MustAsync(tool.Install("thermi-sensors"), cancel);
        var cli = await pc.RunAsync("useradd -m person && su person -c 'thermi-cli status --schema 1'", cancel);
        await pc.MustAsync(tool.Install("thermi"), cancel);
        var files = (await pc.MustAsync($"{tool.Files("thermi-sensors")} && {tool.Files("thermi")}", cancel)).Split('\n');
        var signed = await pc.RunAsync(tool.SignedRepository(), cancel);
        var arm64 = await pc.RunAsync(tool.Arm64(), cancel);
        // Installed again with the network cut, once its libraries are on the PC, it downloads nothing.
        await pc.MustAsync(tool.Remove(), cancel);
        var left = await pc.RunAsync("systemctl is-active thermi-sensors.socket thermi-sensors.service || true; pgrep -x thermi-sensors || true", cancel);
        await pc.OfflineAsync(cancel);
        await pc.MustAsync($"{tool.Install("thermi-sensors", offline: true)} && {tool.Install("thermi", offline: true)}", cancel);
        await pc.MustAsync("mkdir -p /home/person/.config/thermi && echo '{}' > /home/person/.config/thermi/config.json", cancel);
        await pc.MustAsync(tool.Remove(), cancel);
        var kept = await pc.RunAsync("cat /home/person/.config/thermi/config.json", cancel);

        Assert.Equal(0, cli.Exit);
        // Each file spec.md lists is among the package's; the rest, the libraries it holds and the docs a distribution
        // asks of every package, may be there too.
        Assert.Empty(SensorsFiles.Concat(ThermiFiles).Except(files, StringComparer.Ordinal));
        // Nothing the packages started runs after their removal: neither unit, nor any process of the service's.
        Assert.Equal(["inactive", "inactive"], left.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.True(signed.Exit == 0, $"No signed repository of thermi's: {signed.Error}");
        Assert.True(arm64.Exit == 0, $"No arm64 package of the same version: {arm64.Error}");
        Assert.Equal("{}", kept.Out.Trim());
    }

    [Theory]
    [InlineData(Distribution.Ubuntu2604, Skip = Undecided)]
    [InlineData(Distribution.Fedora, Skip = Undecided)]
    public async Task UpdatesComeFromThermisRepository(Distribution row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var tool = Tool.Of(row);
        var channel = Path.Combine(session.Home, "channel");
        await File.WriteAllTextAsync(Path.Combine(session.Home, "hold.py"), Holder, cancel);
        await using var pc = await Container.BootAsync(session, tool.Image, session.Home, cancel);
        var levers = new Levers(session, channel);
        // The build and a newer one, each published as the release workflow publishes a version from the packages it packed.
        var (first, next) = (Built.PackedVersion(Built.Packages), Built.PackedVersion(Built.Newer));
        await levers.RunAsync(["publish", first, "--channel", "stable"], cancel);
        // The first version from the channel's own files, which adds the repository, then pointed at the channel the test serves.
        await pc.MustAsync($"{tool.Prepare()} && {tool.InstallFromChannel("thermi-sensors", first)} && {tool.Served()}", cancel);
        const string Serving = "systemctl show -p MainPID --value thermi-sensors.service";
        // The holder alone runs in the background, its output to the pipe the shell reads once, so the shell ends.
        await pc.MustAsync(
            $"{Container.Installs(tool.Image, tool.Python)} && mkfifo /tmp/held && (python3 {Container.Work}/hold.py > /tmp/held 2>/dev/null < /dev/null &)"
                + " && read -r _ < /tmp/held",
            cancel);
        var before = (await pc.MustAsync(Serving, cancel)).Trim();

        await levers.RunAsync(["publish", next, "--channel", "stable"], cancel, Built.Newer);
        var newer = await UpdatedAsync(pc, tool, cancel);
        // The old service is gone, though a client held it, and the one that serves now runs the new files.
        var restarted = await pc.MustAsync(
            $"[ ! -e /proc/{before} ] && systemctl start thermi-sensors.service && readlink /proc/$({Serving})/exe", cancel);
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

        Assert.StartsWith(next, newer, StringComparison.Ordinal);
        Assert.Equal("/usr/lib/thermi/thermi-sensors", restarted.Trim());
        Assert.NotEqual(newer, rolledBack);
        Assert.StartsWith(first, runs.Trim(), StringComparison.Ordinal);
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
        Built.Copy(AppContext.BaseDirectory, installed);
        var units = await UserUnits.StartAsync(session, cancel, Path.Combine(installed, "thermi"));
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);
        var started = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        await units.SystemctlAsync(["start", "thermi.service"], cancel);
        await started;
        var first = await units.SystemctlAsync(["show", "-p", "MainPID", "--value", "thermi.service"], cancel);
        var window = await session.StartAsync(Path.Combine(installed, "thermi"), ["settings", "--trace"], cancel: cancel);
        await Traced.FirstAsync(window.Error, Traced.WindowShown, cancel);
        // The new window is the old one's successor, and writes where it did.
        var overlay = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        var successor = Traced.NextAsync(window.Error, Traced.WindowShown, cancel);
        Built.Copy(Path.Combine(Built.Packages, "newer"), installed, replace: true);

        // Quiet, with no input and nothing changing on screen: each hands over to its new version.
        await window.WaitForExitAsync(cancel);
        await successor;
        await overlay;
        var windows = (await Accessible.WindowsAsync(session, cancel)).Where(shown => shown.Program == "thermi").ToList();
        var cli = Path.Combine(installed, "thermi-cli");
        var version = (await session.RunAsync(cli, ["--version"], cancel: cancel)).Out.Trim();
        var about = JsonNode.Parse((await session.RunAsync(cli, ["status", "--schema", "1"], cancel: cancel)).Out)!;

        Assert.NotEqual(first, await units.SystemctlAsync(["show", "-p", "MainPID", "--value", "thermi.service"], cancel));
        Assert.Single(await units.ProcessesAsync(cancel));
        Assert.NotEqual(window.Id, Assert.Single(windows).Process);
        // About and status say the same of the version, the channel and the updates, as both read them from Core.
        Assert.Equal(version, (string?)about["version"]);
        Assert.Equal("stable", (string?)about["channel"]);
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
        // Arch's packages are built from the AUR's PKGBUILD, as a person builds them, by a user of their own: makepkg
        // never runs as root.
        private const string Aur = "/home/builder/aur";

        public static Tool Of(Distribution row) => row switch
        {
            Distribution.Ubuntu2404 => new(Container.Ubuntu2404, "deb"),
            Distribution.Ubuntu2604 => new(Container.Ubuntu2604, "deb"),
            Distribution.Debian13 => new(Container.Debian13, "deb"),
            Distribution.Fedora => new(Container.Fedora, "rpm"),
            Distribution.Arch => new(Container.Arch, "pkg.tar.zst"),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown distribution."),
        };

        /// <summary>
        /// A package's name as the distribution knows it: on Arch, the AUR's <c>thermi-bin</c>, which R1 names, packs both,
        /// <c>thermi-bin</c> and <c>thermi-sensors-bin</c>, as the AUR names a package of a build's own files.
        /// </summary>
        public string Package(string name) => Format == "pkg.tar.zst" ? $"{name}-bin" : name;

        /// <summary>The distribution's package of Python, which a test's own client runs on.</summary>
        public string Python => Format == "deb" ? "python3-minimal" : "python3";

        /// <summary>
        /// What a PC holds before thermi's packages: apt's lists; su, which Fedora's image lacks and a PC has; or on Arch
        /// the packages built from the PKGBUILD.
        /// </summary>
        public string Prepare() => Format switch
        {
            "pkg.tar.zst" => $"{Container.Installs(Image, "base-devel")} && useradd -m builder"
                + $" && cp -r {Container.Packages}/aur {Aur} && chown -R builder {Aur}"
                + $" && su builder -c 'cd {Aur} && makepkg --nodeps --noconfirm'",
            "deb" => "apt-get update",
            _ => Container.Installs(Image, "util-linux"),
        };

        // Debian names a package's architecture amd64, Fedora x86_64; a file, with its libraries from the distribution's
        // repositories, or from what the PC already holds once its network is cut.
        public string Install(string package, bool offline = false) => Format switch
        {
            "deb" => $"DEBIAN_FRONTEND=noninteractive apt-get install -y $(ls {Container.Packages}/{package}_[0-9]*_amd64.deb)",
            "rpm" => $"dnf install -y {(offline ? "--disablerepo='*' " : "")}$(ls {Container.Packages}/{package}-[0-9]*.x86_64.rpm)",
            _ => $"pacman -U --noconfirm $(ls {Aur}/{Package(package)}-[0-9]*-x86_64.pkg.tar.zst)",
        };

        /// <summary>A version of a package from the files the levers put in the channel, as the first install from thermi's site.</summary>
        public string InstallFromChannel(string package, string version) => Format == "deb"
            ? $"DEBIAN_FRONTEND=noninteractive apt-get install -y $(find {Container.Work}/channel -name '{package}_{version}*_amd64.deb' | head -n 1)"
            : $"dnf install -y $(find {Container.Work}/channel -name '{package}-{version}*.x86_64.rpm' | head -n 1)";

        // A removal that leaves the libraries the packages brought, as apt's and pacman's do, so they install again offline.
        public string Remove() => Format switch
        {
            "deb" => "apt-get remove -y thermi thermi-sensors",
            "rpm" => "dnf remove -y --setopt=clean_requirements_on_remove=False thermi thermi-sensors",
            _ => $"pacman -R --noconfirm {Package("thermi")} {Package("thermi-sensors")}",
        };

        // The files a package installed, its folders aside, which the tools list too.
        public string Files(string package) => Format switch
        {
            "deb" => $"dpkg -L {package}",
            "rpm" => $"rpm -ql {package}",
            _ => $"pacman -Qlq {Package(package)}",
        } + " | while read -r file; do if [ -f \"$file\" ]; then echo \"$file\"; fi; done";

        // The repository a package added names a key a package installed, which the system checks each update against;
        // Arch's updates come from the AUR instead, which adds none.
        public string SignedRepository() => Format switch
        {
            "deb" => "key=$(sed -n 's/^Signed-By: *//p' /etc/apt/sources.list.d/thermi.sources) && dpkg -S \"$key\"",
            "rpm" => "grep -qx 'gpgcheck=1' /etc/yum.repos.d/thermi.repo && rpm -qf \"$(sed -n 's|^gpgkey=file://||p' /etc/yum.repos.d/thermi.repo)\"",
            _ => "! ls /etc/pacman.d/thermi* 2>/dev/null",
        };

        // The same build packed for arm64 too, at the same version; Arch's PKGBUILD builds for both.
        public string Arm64() => Format switch
        {
            "deb" => $"for p in thermi thermi-sensors; do [ \"$(dpkg-deb -f {Container.Packages}/${{p}}_*_arm64.deb Architecture Version)\" = "
                + "\"$(printf 'Architecture: arm64\\nVersion: %s' $(dpkg-query -W -f '${Version}' $p))\" ] || exit 1; done",
            "rpm" => $"for p in thermi thermi-sensors; do [ \"$(rpm -qp --qf '%{{ARCH}} %{{VERSION}}' {Container.Packages}/$p-[0-9]*.aarch64.rpm)\" = "
                + "\"aarch64 $(rpm -q --qf '%{VERSION}' $p)\" ] || exit 1; done",
            _ => $"grep -q aarch64 {Container.Packages}/aur/PKGBUILD",
        };

        public string Version(string package) => Format == "deb" ? $"dpkg-query -W -f '${{Version}}' {package}" : $"rpm -q --qf '%{{VERSION}}' {package}";

        public string Update() => Format == "deb" ? "apt-get update && apt-get upgrade -y" : "dnf upgrade -y --refresh";

        // The repository the package added, pointed at the levers' channel the test serves.
        public string Served() => Format == "deb"
            ? $"sed -i 's|^URIs:.*|URIs: file://{Container.Work}/channel/apt|' /etc/apt/sources.list.d/thermi.sources"
            : $"sed -i 's|^baseurl=.*|baseurl=file://{Container.Work}/channel/rpm/stable|' /etc/yum.repos.d/thermi.repo";
    }

    /// <summary>The owner's levers of spec 004 on a channel's repositories, as the release workflow pulls them.</summary>
    private sealed class Levers(OwnSession session, string channel)
    {
        public async Task RunAsync(IEnumerable<string> args, CancellationToken cancel, string? packages = null)
        {
            var pulled = await session.RunAsync(
                "pwsh",
                ["-NoProfile", "-File", Path.Combine(Built.Root, "scripts", "updates.ps1"), .. args, "--packages", packages ?? Built.Packages,
                    "--repository", channel],
                cancel: cancel);
            Assert.True(pulled.Exit == 0, pulled.Error);
        }

        /// <summary>The channel signed by a key that is not thermi's: an OpenPGP key of the test's own, made for it.</summary>
        public async Task ServeForeignAsync(CancellationToken cancel)
        {
            var keys = Directory.CreateDirectory(Path.Combine(session.Home, "foreign-gpg")).FullName;
            var foreign = Path.Combine(session.Home, "foreign.asc");
            string[][] steps =
            [
                ["--homedir", keys, "--batch", "--passphrase", "", "--quick-generate-key", "not thermi <foreign@example.org>", "default", "default", "never"],
                ["--homedir", keys, "--batch", "--armor", "--output", foreign, "--export-secret-keys"],
            ];
            foreach (var step in steps)
            {
                var made = await session.RunAsync("gpg", step, cancel: cancel);
                Assert.True(made.Exit == 0, made.Error);
            }

            // gpg starts an agent of its own for the key's folder, which would outlive the test.
            await session.RunAsync("gpgconf", ["--homedir", keys, "--kill", "all"], cancel: cancel);
            await RunAsync(["sign", "--key", foreign, "--channel", "stable"], cancel);
        }

        /// <summary>The channel as it was before the last lever, older than the one the PC holds.</summary>
        public Task ServeOlderAsync(CancellationToken cancel) => RunAsync(["restore", "--before-last", "--channel", "stable"], cancel);
    }
}
