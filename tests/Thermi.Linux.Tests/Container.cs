namespace Thermi.Linux.Tests;

/// <summary>
/// A clean container of a distribution, root inside it and no one on the PC, for what a test cannot do as the person:
/// other users, systemd, installing a package. The packages the build packed are in it, read only, at <see cref="Packages"/>,
/// and a folder of the test's at <see cref="Work"/>. Ending it removes it, the image it booted from, and the distribution's
/// image when the test pulled it.
/// </summary>
internal sealed class Container(OwnSession session, string id, IReadOnlyList<string> images) : IAsyncDisposable
{
    public const string Packages = "/packages";

    public const string Work = "/work";

    public const string Ubuntu2404 = "ubuntu:24.04";

    public const string Ubuntu2604 = "ubuntu:26.04";

    public const string Debian13 = "debian:13";

    public const string Fedora = "fedora:latest";

    public const string Arch = "archlinux:latest";

    private static readonly string[] Idle = ["sleep", "infinity"];

    private string Id { get; } = id;

    /// <summary>A container of <paramref name="image"/> as it comes, idle until the test runs something in it.</summary>
    public static async Task<Container> StartAsync(OwnSession session, string image, string work, CancellationToken cancel)
    {
        var pulled = await PulledAsync(session, image, cancel).ConfigureAwait(false);
        return await RunAsync(session, image, work, [], Idle, pulled, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// A container of <paramref name="image"/> with systemd as its first process, as a PC boots, once systemd says the boot
    /// is done. The distribution's images ship no systemd, so it is installed first, from the distribution's own packages.
    /// </summary>
    public static async Task<Container> BootAsync(OwnSession session, string image, string work, CancellationToken cancel)
    {
        // An image of the test's own, which no other run shares, and which goes with the container, as does the
        // distribution's when the test pulled it; they go too when the boot fails.
        var booting = $"thermi-tests/{image.Replace(':', '-')}-systemd:{Guid.NewGuid():N}";
        string[] owned = [booting, .. await PulledAsync(session, image, cancel).ConfigureAwait(false)];
        try
        {
            await using (var plain = await RunAsync(session, image, work, [], Idle, [], cancel).ConfigureAwait(false))
            {
                await plain.MustAsync(Installs(image, "systemd"), cancel).ConfigureAwait(false);
                await plain.CommitAsync(booting, cancel).ConfigureAwait(false);
            }

            return await BootedAsync(session, booting, work, owned, cancel).ConfigureAwait(false);
        }
        catch
        {
            await RemoveAsync(session, owned).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The distribution's own command that installs <paramref name="packages"/> from its repositories.</summary>
    public static string Installs(string image, string packages) => image switch
    {
        _ when image.StartsWith("fedora", StringComparison.Ordinal) => $"dnf install -y {packages}",
        _ when image.StartsWith("archlinux", StringComparison.Ordinal) => $"pacman -Sy --noconfirm {packages}",
        _ => $"apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y {packages}",
    };

    /// <summary>Runs <paramref name="script"/> with bash as root in the container: its exit code, and what it wrote.</summary>
    public Task<Ran> RunAsync(string script, CancellationToken cancel) =>
        session.RunAsync("docker", ["exec", Id, "bash", "-euo", "pipefail", "-c", script], cancel: cancel);

    /// <summary>
    /// Runs <paramref name="script"/> as <see cref="RunAsync(string, CancellationToken)"/> does, and fails the test with what
    /// it wrote unless it ends with 0.
    /// </summary>
    public async Task<string> MustAsync(string script, CancellationToken cancel)
    {
        var ran = await RunAsync(script, cancel).ConfigureAwait(false);
        return ran.Exit == 0 ? ran.Out : throw new InvalidOperationException($"'{script}' ended with {ran.Exit}: {ran.Error}");
    }

    /// <summary>Cuts the container off every network, so what it installs next comes from its own files alone.</summary>
    public async Task OfflineAsync(CancellationToken cancel)
    {
        var cut = await session.RunAsync("docker", ["network", "disconnect", "bridge", Id], cancel: cancel).ConfigureAwait(false);
        if (cut.Exit != 0)
        {
            throw new InvalidOperationException($"The container stayed on its network: {cut.Error}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await session.RunAsync("docker", ["rm", "--force", Id]).ConfigureAwait(false);
        await RemoveAsync(session, images).ConfigureAwait(false);
    }

    // systemd runs in a container with a cgroup namespace of its own and /run in memory. Docker mounts that namespace's
    // cgroups read only, where systemd cannot make its units' groups, so the container mounts them again writable: only
    // its own groups, never the PC's, with no device or other privilege of the PC's. Booted, systemd says READY=1 on the
    // socket its environment names, as it tells any container manager.
    private static async Task<Container> BootedAsync(OwnSession session, string booting, string work, string[] owned, CancellationToken cancel)
    {
        var socket = $"ready-{Guid.NewGuid():N}";
        var ready = await Readiness.ListenAsync(session, Path.Combine(work, socket), cancel).ConfigureAwait(false);
        string[] options =
        [
            "--cgroupns=private", "--tmpfs", "/run", "--tmpfs", "/run/lock", "--cap-add", "SYS_ADMIN", "--security-opt", "apparmor=unconfined",
            "--env", $"NOTIFY_SOCKET={Work}/{socket}",
        ];
        string[] boot = ["/bin/sh", "-c", "mount -o remount,rw /sys/fs/cgroup && exec /lib/systemd/systemd"];
        var booted = await RunAsync(session, booting, work, options, boot, owned, cancel).ConfigureAwait(false);
        var ended = session.RunAsync("docker", ["wait", booted.Id], cancel: cancel);
        var came = await Readiness.BeforeEndAsync(ready, ended).ConfigureAwait(false);
        // Booted is running, or degraded when a unit a container has no use for failed; any other state, or a container
        // that ended first, is no boot.
        var state = !came ? "" : (await booted.RunAsync("systemctl is-system-running || true", cancel).ConfigureAwait(false)).Out.Trim();
        if (state is not ("running" or "degraded"))
        {
            var logs = await session.RunAsync("docker", ["logs", booted.Id], cancel: cancel).ConfigureAwait(false);
            await booted.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"{booting} did not boot: systemd says '{state}'.\n{logs.Out}{logs.Error}");
        }

        return booted;
    }

    // The distribution's image, when the PC holds none yet: the test pulls it, and removes it after.
    private static async Task<string[]> PulledAsync(OwnSession session, string image, CancellationToken cancel)
    {
        var held = await session.RunAsync("docker", ["image", "inspect", image], cancel: cancel).ConfigureAwait(false);
        if (held.Exit == 0)
        {
            return [];
        }

        var pulled = await session.RunAsync("docker", ["pull", "--quiet", image], cancel: cancel).ConfigureAwait(false);
        return pulled.Exit == 0 ? [image] : throw new InvalidOperationException($"{image} could not be pulled: {pulled.Error}");
    }

    // An image another run's container still uses stays: docker refuses to remove it, and that run removes it after.
    private static async Task RemoveAsync(OwnSession session, IReadOnlyList<string> images)
    {
        foreach (var image in images)
        {
            await session.RunAsync("docker", ["rmi", image]).ConfigureAwait(false);
        }
    }

    // Docker's --mount, not -v, which makes a missing folder instead of failing.
    private static string Mount(string from, string to) => $"--mount=type=bind,source={from},target={to},readonly";

    private static async Task<Container> RunAsync(
        OwnSession session, string image, string work, string[] options, string[] command, IReadOnlyList<string> images, CancellationToken cancel)
    {
        var started = await session.RunAsync(
            "docker",
            ["run", "--detach", "--rm", .. options, Mount(Built.Packages, Packages), "--volume", $"{work}:{Work}", image, .. command],
            cancel: cancel).ConfigureAwait(false);
        return started.Exit == 0
            ? new Container(session, started.Out.Trim(), images)
            : throw new InvalidOperationException($"{image} did not start: {started.Error}");
    }

    private async Task CommitAsync(string tag, CancellationToken cancel)
    {
        var committed = await session.RunAsync("docker", ["commit", Id, tag], cancel: cancel).ConfigureAwait(false);
        if (committed.Exit != 0)
        {
            throw new InvalidOperationException($"The container was not kept as {tag}: {committed.Error}");
        }
    }
}
