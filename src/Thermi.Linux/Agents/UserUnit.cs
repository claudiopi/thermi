using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Agents;

/// <summary>
/// Start at sign-in on Linux, for the window's switch and thermi-cli's <c>start-with-windows</c> alike: the overlay's user
/// unit, <c>thermi.service</c>, as the person's own <c>systemctl --user</c> enables, disables and reads it, so an enable of
/// systemd's, theirs or an administrator's for everyone, reads the same in both. The package enables it for no one
/// (docs/adr/0019-linux.md); thermi's first start in each person's session does, once (<see cref="EnableOnceAsync"/>). A switch
/// systemctl refused is an error, never one that only looks set. Hyprland started without uwsm brings no graphical session
/// for systemd and runs no autostart, so nothing starts the overlay at sign-in there: it reads off, and refuses on.
/// </summary>
/// <param name="time">The clock <see cref="Answers"/> runs on.</param>
/// <param name="systemctl">The program asked, by its full path, never one found on the PATH.</param>
/// <param name="desktops">The session's desktops, as <c>XDG_CURRENT_DESKTOP</c> names them; the process's own for null.</param>
internal sealed class UserUnit(TimeProvider time, string systemctl = UserUnit.Systemctl, string? desktops = null) : IStartAtSignIn
{
    public const string Name = "thermi.service";

    private const string GraphicalSession = "graphical-session.target";

    private readonly bool _hyprland = (desktops ?? Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
        .Split(':').Contains("Hyprland", StringComparer.OrdinalIgnoreCase);

    public const string Systemctl = "/usr/bin/systemctl";

    /// <summary>The unit's file, where systemd would find it, or null when no package or test put one.</summary>
    public static string? File
    {
        get
        {
            var config = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // In the order systemd looks: the user's settings, the system's, the user's own units, then the packages'.
            string[] folders = [Path.Combine(config, "systemd", "user"), "/etc/systemd/user", Path.Combine(data, "systemd", "user"),
                "/usr/local/lib/systemd/user", "/usr/lib/systemd/user"];
            return folders.Select(folder => Path.Combine(folder, Name)).FirstOrDefault(System.IO.File.Exists);
        }
    }

    /// <summary>Where <see cref="EnableOnceAsync"/> marks that it turned start at sign-in on: beside the settings.</summary>
    public static string Marker { get; } = Path.Combine(Path.GetDirectoryName(ConfigStore.DefaultPath)!, "start-at-sign-in");

    /// <summary>
    /// How long systemctl has to answer, as it does in milliseconds: one whose user manager hangs, as while it shuts down,
    /// reads off and refuses a switch, and the window's switch and thermi-cli answer.
    /// </summary>
    public static readonly TimeSpan Answers = TimeSpan.FromSeconds(10);

    public async Task<bool> OnAsync(CancellationToken cancel) => !await NothingStartsAsync(cancel) && await RunAsync("is-enabled", cancel) == 0;

    /// <summary>
    /// Turns start at sign-in on at thermi's first start in a person's session, as Windows' installer does: once, so a
    /// person who turned it off finds it off at every start after. <paramref name="marker"/> says it was done; a user
    /// manager that would not enable it leaves none, and the next start tries again.
    /// </summary>
    public async Task EnableOnceAsync(string marker, ILogger log, CancellationToken cancel)
    {
        if (System.IO.File.Exists(marker))
        {
            return;
        }

        try
        {
            await SetAsync(on: true, cancel);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            await System.IO.File.WriteAllBytesAsync(marker, [], cancel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.SignInNotEnabled(ex);
        }
    }

    public async Task SetAsync(bool on, CancellationToken cancel)
    {
        if (on && await NothingStartsAsync(cancel))
        {
            throw new IOException("Hyprland started without uwsm starts no graphical session for systemd, so nothing starts thermi at sign-in.");
        }

        var verb = on ? "enable" : "disable";
        if (await RunAsync(verb, cancel) is var exit and not 0)
        {
            throw new IOException(exit is { } code
                ? string.Create(CultureInfo.InvariantCulture, $"systemctl --user {verb} {Name} ended with {code}.")
                : string.Create(CultureInfo.InvariantCulture, $"systemctl --user {verb} {Name} did not answer in {Answers.TotalSeconds} s."));
        }
    }

    /// <summary>
    /// Whether the person's graphical session is up for systemd, which the unit needs to start, within
    /// <paramref name="bound"/>: GNOME and Plasma start graphical-session.target at sign-in, maybe a moment after their
    /// autostart's programs; Xfce, MATE, Cinnamon and Sway never do, and are waited for that long, once.
    /// </summary>
    public async Task<bool> SessionUpAsync(TimeSpan bound, CancellationToken cancel)
    {
        var since = time.GetTimestamp();
        while (await RunAsync("is-active", cancel, GraphicalSession) != 0)
        {
            if (time.GetElapsedTime(since) >= bound)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), time, cancel);
        }

        return true;
    }

    // Hyprland's session, with uwsm, is systemd's, and brings the target; without, it brings none.
    private async Task<bool> NothingStartsAsync(CancellationToken cancel) => _hyprland && await RunAsync("is-active", cancel, GraphicalSession) != 0;

    // Null when systemctl did not answer in time.
    private async Task<int?> RunAsync(string verb, CancellationToken cancel, string unit = Name)
    {
        using var bound = new CancellationTokenSource(Answers, time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancel, bound.Token);
        try
        {
            return await SystemProgram.RunAsync(systemctl, ["--user", "--quiet", verb, unit], either.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return null;
        }
    }
}
