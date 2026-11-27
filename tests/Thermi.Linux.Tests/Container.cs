namespace Thermi.Linux.Tests;

/// <summary>
/// A clean container of a distribution, root inside it and no one on the PC, for what a test cannot do as the person:
/// other users, systemd, installing a package. The build's output is in it, read only, at <see cref="Output"/>, and a
/// folder of the test's at <see cref="Work"/>. Ending it removes it.
/// </summary>
internal sealed class Container(OwnSession session, string id) : IAsyncDisposable
{
    public const string Output = "/thermi";

    public const string Work = "/work";

    public const string Ubuntu2404 = "ubuntu:24.04";

    public const string Ubuntu2604 = "ubuntu:26.04";

    public const string Debian13 = "debian:13";

    public const string Fedora = "fedora:latest";

    public const string Arch = "archlinux:latest";

    /// <summary>A container of <paramref name="image"/> as it comes, idle until the test runs something in it.</summary>
    public static Task<Container> StartAsync(OwnSession session, string image, string work, CancellationToken cancel) =>
        RunAsync(session, image, work, ["sleep", "infinity"], cancel);

    /// <summary>
    /// A container of <paramref name="image"/> with systemd as its first process, as a PC boots, once systemd says the boot
    /// is done. The distribution's images ship no systemd, so it is installed first, from the distribution's own packages.
    /// </summary>
    public static async Task<Container> BootAsync(OwnSession session, string image, string work, CancellationToken cancel)
    {
        var booting = $"thermi-tests/{image.Replace(':', '-')}-systemd";
        await using (var plain = await StartAsync(session, image, work, cancel).ConfigureAwait(false))
        {
            await plain.MustAsync(Installs(image, "systemd"), cancel).ConfigureAwait(false);
            await plain.CommitAsync(booting, cancel).ConfigureAwait(false);
        }

        // systemd runs in a container with a cgroup of its own and /run in memory, with no privilege of the PC's.
        var booted = await RunAsync(session, booting, work, ["--cgroupns=private", "--tmpfs", "/run", "--tmpfs", "/run/lock"], ["/lib/systemd/systemd"], cancel)
            .ConfigureAwait(false);
        await booted.RunAsync("systemctl is-system-running --wait", cancel).ConfigureAwait(false);
        return booted;
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
        session.RunAsync("docker", ["exec", id, "bash", "-euo", "pipefail", "-c", script], cancel: cancel);

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
        var cut = await session.RunAsync("docker", ["network", "disconnect", "bridge", id], cancel: cancel).ConfigureAwait(false);
        if (cut.Exit != 0)
        {
            throw new InvalidOperationException($"The container stayed on its network: {cut.Error}");
        }
    }

    public async ValueTask DisposeAsync() => await session.RunAsync("docker", ["rm", "--force", id]).ConfigureAwait(false);

    private async Task CommitAsync(string tag, CancellationToken cancel)
    {
        var committed = await session.RunAsync("docker", ["commit", id, tag], cancel: cancel).ConfigureAwait(false);
        if (committed.Exit != 0)
        {
            throw new InvalidOperationException($"The container was not kept as {tag}: {committed.Error}");
        }
    }

    private static Task<Container> RunAsync(OwnSession session, string image, string work, string[] command, CancellationToken cancel) =>
        RunAsync(session, image, work, [], command, cancel);

    private static async Task<Container> RunAsync(OwnSession session, string image, string work, string[] options, string[] command, CancellationToken cancel)
    {
        var started = await session.RunAsync(
            "docker",
            ["run", "--detach", "--rm", .. options, "--volume", $"{AppContext.BaseDirectory}:{Output}:ro", "--volume", $"{work}:{Work}", image, .. command],
            cancel: cancel).ConfigureAwait(false);
        return started.Exit == 0
            ? new Container(session, started.Out.Trim())
            : throw new InvalidOperationException($"{image} did not start: {started.Error}");
    }
}
