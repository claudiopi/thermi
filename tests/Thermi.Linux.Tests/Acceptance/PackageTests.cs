using System.Text.Json.Nodes;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// thermi's packages and their updates (<c>docs/specs/006-linux/spec.md</c>, R1, R25 and R26), in clean containers of each
/// distribution, from the packages one build packed, in <see cref="Built.Packages"/>.
/// </summary>
public sealed partial class PackageTests
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
        "/usr/share/gnome-shell/extensions/thermi@claudiopi.github.io/desktop.js",
        "/usr/share/gnome-shell/extensions/thermi@claudiopi.github.io/extension.js",
        "/usr/share/gnome-shell/extensions/thermi@claudiopi.github.io/metadata.json",
        "/usr/share/gnome-shell/extensions/thermi@claudiopi.github.io/pointerWatch.js",
        "/usr/share/gnome-shell/extensions/thermi@claudiopi.github.io/protocol.js",
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

    // A system user with a graphical session of its own, as GDM's login screen runs one as gdm: its user manager, kept
    // running, has graphical-session.target active, which a unit of its own pulls in. Its lines end as the shell reads them.
    private static readonly string Greeter = """
        useradd -r -m -d /var/lib/greeter greeter && mkdir -p /var/lib/greeter/.config/systemd/user
        printf '[Unit]\nWants=graphical-session.target\n[Service]\nType=oneshot\nRemainAfterExit=yes\nExecStart=/bin/true\n' \
            > /var/lib/greeter/.config/systemd/user/greeter-session.service
        chown -R greeter /var/lib/greeter && loginctl enable-linger greeter && systemctl start "user@$(id -u greeter).service"
        systemctl --user --machine=greeter@ start greeter-session.service
        systemctl --user --machine=greeter@ is-active graphical-session.target
        """.ReplaceLineEndings("\n");

    public enum Distribution
    {
        Ubuntu2404,
        Ubuntu2604,
        Debian13,
        Fedora,
        Arch,
    }

    [Theory(Timeout = Bound.Test)]
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
        await pc.MustAsync($"{tool.Sessions()} && {Greeter}", cancel);
        await pc.MustAsync(tool.Install("thermi"), cancel);
        // The overlay starts in a person's graphical session alone: a system user's never ran it.
        var greeter = await pc.RunAsync("systemctl --user --machine=greeter@ show -p ExecMainStartTimestampMonotonic --value thermi.service", cancel);
        // What the runtime loads by name, which no file of the packages names, the packages declare all the same.
        await File.WriteAllTextAsync(Path.Combine(session.Home, "asked.awk"), Tool.Asked, cancel);
        var loaded = await pc.RunAsync(Tool.Load(), cancel);
        var unclaimed = await pc.MustAsync(tool.Unclaimed(), cancel);
        var files = (await pc.MustAsync($"{tool.Files("thermi-sensors")} && {tool.Files("thermi")}", cancel)).Split('\n');
        var signed = await pc.RunAsync(tool.SignedRepository(), cancel);
        var arm64 = await pc.RunAsync(tool.Arm64(), cancel);
        // Installed again with the network cut, once its libraries are on the PC, it downloads nothing.
        await pc.MustAsync(tool.Remove(), cancel);
        var left = await pc.RunAsync("systemctl is-active thermi-sensors.socket thermi-sensors.service || true; pgrep -x thermi-sensors || true", cancel);
        await pc.OfflineAsync(cancel);
        await pc.MustAsync($"{tool.Install("thermi-sensors", offline: true)} && {tool.Install("thermi", offline: true)}", cancel);
        // Installed again after a removal, which left apt its files' record, the socket listens as on a first install.
        var listens = await pc.RunAsync("systemctl is-active thermi-sensors.socket", cancel);
        await pc.MustAsync("mkdir -p /home/person/.config/thermi && echo '{}' > /home/person/.config/thermi/config.json", cancel);
        await pc.MustAsync(tool.Purge(), cancel);
        var kept = await pc.RunAsync("cat /home/person/.config/thermi/config.json", cancel);
        var linked = await pc.RunAsync("ls /etc/systemd/system/sockets.target.wants/thermi-sensors.socket", cancel);

        Assert.Equal(0, cli.Exit);
        // Installed by a package, thermi updates with the system, on the stable channel the package added.
        var about = JsonNode.Parse(cli.Out)!;
        Assert.Equal(("stable", "system"), ((string?)about["channel"], (string?)about["updates"]));
        Assert.Equal("0", greeter.Out.Trim());
        Assert.True(loaded.Exit == 0, $"thermi-cli, over a settings file: {loaded.Out}{loaded.Error}");
        Assert.Empty(unclaimed.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        // Each file spec.md lists is among the package's; the rest, the libraries it holds and the docs a distribution
        // asks of every package, may be there too.
        Assert.Empty(SensorsFiles.Concat(ThermiFiles).Except(files, StringComparer.Ordinal));
        // Nothing the packages started runs after their removal: neither unit, nor any process of the service's.
        Assert.Equal(["inactive", "inactive"], left.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("active", listens.Out.Trim());
        Assert.True(signed.Exit == 0, $"No signed repository of thermi's: {signed.Error}");
        Assert.True(arm64.Exit == 0, $"No arm64 package of the same version: {arm64.Error}");
        // A purge, or a removal where the package manager has none, leaves each person's settings and no socket to listen.
        Assert.Equal("{}", kept.Out.Trim());
        Assert.NotEqual(0, linked.Exit);
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Distribution.Ubuntu2604)]
    [InlineData(Distribution.Fedora)]
    public async Task UpdatesComeFromThermisRepository(Distribution row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var tool = Tool.Of(row);
        var channel = Path.Combine(session.Home, "channel");
        await File.WriteAllTextAsync(Path.Combine(session.Home, "hold.py"), Holder, cancel);
        await using var pc = await Container.BootAsync(session, tool.Image, session.Home, cancel);
        var levers = new Levers(session, channel);
        // The build and a newer one, each published as the release workflow publishes a version from the packages it packed:
        // the build as a release is, to stable, and the newer one as a merge is, to beta, then promoted.
        var (first, next) = (Built.PackedVersion(Built.Packages), Built.PackedVersion(Built.Newer));
        await levers.RunAsync(["publish", first, "--channel", "stable"], cancel);
        levers.Snapshot();
        // The first version from the channel's own files, which adds the repository, then pointed at the channel the test serves.
        await pc.MustAsync($"{tool.Prepare()} && {tool.InstallFromChannel("thermi-sensors", first)} && {tool.Served()}", cancel);
        const string Serving = "systemctl show -p MainPID --value thermi-sensors.service";
        // The holder alone runs in the background, its output to the pipe the shell reads once, so the shell ends.
        await pc.MustAsync(
            $"{Container.Installs(tool.Image, tool.Python)} && mkfifo /tmp/held && (python3 {Container.Work}/hold.py > /tmp/held 2>/dev/null < /dev/null &)"
                + " && read -r _ < /tmp/held",
            cancel);
        var before = (await pc.MustAsync(Serving, cancel)).Trim();

        await levers.RunAsync(["publish", next, "--channel", "beta"], cancel, Built.Newer);
        levers.Unfetched(next);
        await levers.RunAsync(["promote", "--from", "beta", "--to", "stable"], cancel);
        var newer = await UpdatedAsync(pc, tool, cancel);
        // The old service is gone, though a client held it, and the process that serves now is the new version's.
        var serving = (await pc.MustAsync($"[ ! -e /proc/{before} ] && systemctl start thermi-sensors.service && {Serving}", cancel)).Trim();
        var serves = await pc.MustAsync($"/proc/{serving}/exe --version", cancel);
        await levers.RunAsync(["rollback", "--channel", "stable"], cancel);
        var rolledBack = await UpdatedAsync(pc, tool, cancel);
        // The version before, published under a higher number, so every PC takes it.
        var runs = await pc.MustAsync("/usr/lib/thermi/thermi-sensors --version", cancel);
        await levers.RunAsync(["disable", "--channel", "stable"], cancel);
        var disabled = await UpdatedAsync(pc, tool, cancel);
        // Refused, a repository leaves the version the PC holds, and the system's updater says why; whether it ends with 0
        // differs by tool.
        await levers.ServeForeignAsync(cancel);
        var foreign = await pc.RunAsync(tool.Update(), cancel);
        var keptFromForeign = (await pc.MustAsync(tool.Version("thermi-sensors"), cancel)).Trim();
        // The offer before the last, offered again: apt holds it as its newest Release, which it would not be if signed then.
        await levers.RunAsync(["restore", "--before-last", "--channel", "stable"], cancel);
        var restored = await UpdatedAsync(pc, tool, cancel);
        var heldRestored = tool.HeldRelease is { } read ? (await pc.MustAsync(read, cancel)).Trim() : null;
        // A mirror's copy of the channel as it first was, of a version older than the PC's: it never takes the PC back.
        var restoredDate = levers.ServedDate();
        levers.ServeSnapshot();
        await pc.RunAsync(tool.Update(), cancel);
        var keptFromOlder = (await pc.MustAsync(tool.Version("thermi-sensors"), cancel)).Trim();
        var heldOlder = tool.HeldRelease is { } again ? (await pc.MustAsync(again, cancel)).Trim() : null;

        Assert.StartsWith(next, newer, StringComparison.Ordinal);
        Assert.NotEqual(before, serving);
        Assert.StartsWith(next, serves.Trim(), StringComparison.Ordinal);
        Assert.NotEqual(newer, rolledBack);
        Assert.StartsWith(first, runs.Trim(), StringComparison.Ordinal);
        Assert.Equal(rolledBack, disabled);
        Assert.Equal(rolledBack, keptFromForeign);
        Assert.Matches(tool.Unsigned, foreign.Out + foreign.Error);
        Assert.Equal(rolledBack, restored);
        Assert.Equal(rolledBack, keptFromOlder);
        // apt keeps the newest Release it read; dnf, which has no such check, reads the older index but installs nothing older.
        Assert.Equal(tool.HeldRelease is null ? null : restoredDate, heldRestored);
        Assert.Equal(heldRestored, heldOlder);
    }

    // The version the system's updater installed, after it looked for updates.
    private static async Task<string> UpdatedAsync(Container pc, Tool tool, CancellationToken cancel)
    {
        await pc.MustAsync(tool.Update(), cancel);
        return (await pc.MustAsync(tool.Version("thermi-sensors"), cancel)).Trim();
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

        /// <summary>
        /// A version's tarballs gone from the channel's folder, as <c>scripts/channel-sync.ps1</c> leaves it for the release
        /// workflow's levers: it fetches only those a rollback would pack again.
        /// </summary>
        public void Unfetched(string version) => Directory.Delete(Path.Combine(channel, "tarballs", version), recursive: true);

        // A copy of the channel's indexes as served now, as a mirror that stops following it keeps.
        private string Copy => Path.Combine(session.Home, "snapshot");

        private static readonly string[] Indexes = [Path.Combine("apt", "dists"), Path.Combine("rpm", "stable", "repodata")];

        public void Snapshot()
        {
            foreach (var index in Indexes)
            {
                Built.Copy(Path.Combine(channel, index), Path.Combine(Copy, index));
            }
        }

        /// <summary>The indexes <see cref="Snapshot"/> copied, served over the channel's own, with the packages they name.</summary>
        public void ServeSnapshot()
        {
            foreach (var index in Indexes)
            {
                Built.Copy(Path.Combine(Copy, index), Path.Combine(channel, index), replace: true);
            }
        }

        /// <summary>The Date of the Release the channel serves.</summary>
        public string ServedDate() => File.ReadLines(Path.Combine(channel, "apt", "dists", "stable", "InRelease"))
            .First(line => line.StartsWith("Date: ", StringComparison.Ordinal))["Date: ".Length..];
    }
}
