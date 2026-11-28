using System.Diagnostics;
using System.Globalization;
using Thermi.MacOS.Cli.Agents;
using Thermi.MacOS.Cli.Native;

namespace Thermi.MacOS.Agents;

/// <summary>
/// <c>thermi register</c>, which the app's first start and the cask run, and <c>thermi unregister</c>: they write thermi's
/// two LaunchAgents (<see cref="LaunchAgents"/>) and load them into the person's launchd domains, or take them out, with
/// <c>launchctl</c>, which works over SSH too, where <c>SMAppService</c> does not (docs/adr/0021-macos.md). It runs before
/// AppKit starts, so it needs no window server.
/// </summary>
internal static class Registration
{
    // launchd takes an agent out after bootout answers, so loading it again right after may be refused for a moment.
    private const int Tries = 20;
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// What <c>thermi register [--open-at-login on|off]</c> or <c>thermi unregister</c> does with <paramref name="args"/>, or
    /// null for any other command; arguments that make no sense say why and exit with 2.
    /// </summary>
    public static async Task<int?> RunAsync(IReadOnlyList<string> args, TextWriter error, TimeProvider time, CancellationToken cancel)
    {
        switch (args)
        {
            case ["register"] or ["register", "--open-at-login", "on"]:
                return await RegisterAsync(openAtLogin: true, error, time, cancel);
            case ["register", "--open-at-login", "off"]:
                return await RegisterAsync(openAtLogin: false, error, time, cancel);
            case ["unregister"]:
                return await UnregisterAsync(cancel);
            case ["register" or "unregister", ..]:
                await error.WriteLineAsync("Usage: thermi register [--open-at-login on|off], or thermi unregister.");
                return 2;
            default:
                return null;
        }
    }

    private static string UserDomain => string.Create(CultureInfo.InvariantCulture, $"user/{User.Id}");

    private static string GuiDomain => string.Create(CultureInfo.InvariantCulture, $"gui/{User.Id}");

    // A Mac reached over SSH has no graphical session to load the overlay's agent into, which then loads at the next login:
    // it says so, and registers all the same.
    private static async Task<int> RegisterAsync(bool openAtLogin, TextWriter error, TimeProvider time, CancellationToken cancel)
    {
        LaunchAgents.WriteBoth(openAtLogin);
        if (await LoadAsync(UserDomain, LaunchAgents.ServiceLabel, time, cancel) is { } service)
        {
            await error.WriteLineAsync($"launchd did not load thermi's sensor service: {service}");
            return 1;
        }

        if (await LoadAsync(GuiDomain, LaunchAgents.OverlayLabel, time, cancel) is { } overlay)
        {
            await error.WriteLineAsync($"thermi's overlay starts at the next login, as this session has no desktop to start it in now: {overlay}");
        }

        return 0;
    }

    // Both agents out of launchd and their plists away; the person's settings stay.
    private static async Task<int> UnregisterAsync(CancellationToken cancel)
    {
        foreach (var (domain, label) in new[] { (GuiDomain, LaunchAgents.OverlayLabel), (UserDomain, LaunchAgents.ServiceLabel) })
        {
            await LaunchctlAsync(["bootout", $"{domain}/{label}"], cancel);
            File.Delete(LaunchAgents.PlistOf(label));
        }

        return 0;
    }

    // What launchd said when it did not load the agent, or null when it did; one an earlier register loaded goes first.
    private static async Task<string?> LoadAsync(string domain, string label, TimeProvider time, CancellationToken cancel)
    {
        var (loaded, _) = await LaunchctlAsync(["print", $"{domain}/{label}"], cancel);
        if (loaded == 0)
        {
            await LaunchctlAsync(["bootout", $"{domain}/{label}"], cancel);
        }

        var said = "";
        for (var tried = 0; tried < Tries; tried++)
        {
            (var exit, said) = await LaunchctlAsync(["bootstrap", domain, LaunchAgents.PlistOf(label)], cancel);
            if (exit == 0)
            {
                return null;
            }

            if (loaded != 0)
            {
                break;
            }

            await Task.Delay(Retry, time, cancel);
        }

        return said.Trim();
    }

    private static async Task<(int Exit, string Said)> LaunchctlAsync(IEnumerable<string> args, CancellationToken cancel)
    {
        var start = new ProcessStartInfo("/bin/launchctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var launchctl = Process.Start(start) ?? throw new InvalidOperationException("launchctl did not start.");
        var output = launchctl.StandardOutput.ReadToEndAsync(cancel);
        var error = launchctl.StandardError.ReadToEndAsync(cancel);
        await launchctl.WaitForExitAsync(cancel);
        return (launchctl.ExitCode, await output + await error);
    }
}
