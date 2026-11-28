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
/// systemctl refused is an error, never one that only looks set.
/// </summary>
/// <param name="time">The clock <see cref="Answers"/> runs on.</param>
/// <param name="systemctl">The program asked, by its full path, never one found on the PATH.</param>
internal sealed class UserUnit(TimeProvider time, string systemctl = UserUnit.Systemctl) : IStartAtSignIn
{
    public const string Name = "thermi.service";

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

    public async Task<bool> OnAsync(CancellationToken cancel) => await RunAsync("is-enabled", cancel) == 0;

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
        var verb = on ? "enable" : "disable";
        if (await RunAsync(verb, cancel) is var exit and not 0)
        {
            throw new IOException(exit is { } code
                ? string.Create(CultureInfo.InvariantCulture, $"systemctl --user {verb} {Name} ended with {code}.")
                : string.Create(CultureInfo.InvariantCulture, $"systemctl --user {verb} {Name} did not answer in {Answers.TotalSeconds} s."));
        }
    }

    // Null when systemctl did not answer in time.
    private async Task<int?> RunAsync(string verb, CancellationToken cancel)
    {
        using var bound = new CancellationTokenSource(Answers, time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancel, bound.Token);
        try
        {
            return await SystemProgram.RunAsync(systemctl, ["--user", "--quiet", verb, Name], either.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return null;
        }
    }
}
