using System.Globalization;
using System.Security;

namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// The person's launchd, as a session made for tests uses it: thermi's two agents by their labels, what launchd says of
/// each, and the service as an agent of the test's own, which reads a recording, under the label <c>thermi register</c>
/// gives the service, in the session's home, so the session takes it out of launchd when it ends.
/// </summary>
internal static class Launchd
{
    public const string ServiceLabel = "io.github.claudiopi.thermi.sensors";

    public const string OverlayLabel = "io.github.claudiopi.thermi";

    // As thermi register writes the service's agent: in the background session every SSH login shares with the desktop,
    // started at its socket's first client, which only its user may open.
    private const string Service = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>LABEL</string>
          <key>ProgramArguments</key>
          <array><string>PROGRAM</string><string>--replay</string><string>RECORDING</string></array>
          <key>LimitLoadToSessionType</key><string>Background</string>
          <key>Sockets</key>
          <dict><key>Listener</key><dict><key>SockPathName</key><string>SOCKET</string><key>SockPathMode</key><integer>384</integer></dict></dict>
        </dict>
        </plist>
        """;

    /// <summary>
    /// What <c>launchctl print</c> says of <paramref name="label"/> in <paramref name="domain"/>, or null when launchd has no
    /// such agent there.
    /// </summary>
    public static async Task<string?> PrintAsync(OwnSession session, string domain, string label, CancellationToken cancel)
    {
        var printed = await session.RunAsync("launchctl", ["print", $"{domain}/{label}"], cancel: cancel).ConfigureAwait(false);
        return printed.Exit == 0 ? printed.Out : null;
    }

    /// <summary>
    /// The pid of the process launchd runs for <paramref name="label"/> in the person's graphical session, once it is one
    /// other than <paramref name="other"/>, within a line's bound.
    /// </summary>
    public static async Task<int> PidAsync(OwnSession session, string label, int other, CancellationToken cancel)
    {
        var (gui, _) = await session.DomainsAsync(cancel).ConfigureAwait(false);
        // A look every tenth of a second, as many as the bound's seconds have tenths.
        var look = $"p=$(launchctl print {gui}/{label} 2>/dev/null | sed -n 's/^[[:space:]]*pid = //p'); [ -n \"$p\" ] && [ \"$p\" != {other} ]";
        string[] wait = ["-c", $"for i in $(seq {Bound.LineSeconds}0); do {look} && echo $p && exit 0; sleep 0.1; done; exit 1"];
        var ran = await session.RunAsync("/bin/sh", wait, cancel: cancel).ConfigureAwait(false);
        return ran.Exit == 0
            ? int.Parse(ran.Out.Trim(), CultureInfo.InvariantCulture)
            : throw new TimeoutException($"launchd ran no {label} but {other} in {Bound.Line.TotalSeconds:0} s.");
    }

    /// <summary>The service as an agent of the session's home, replaying <paramref name="recording"/>, loaded where <c>thermi register</c> loads it.</summary>
    public static async Task LoadServiceAsync(OwnSession session, string recording, CancellationToken cancel)
    {
        var plist = Path.Combine(session.Home, "Library", "LaunchAgents", $"{ServiceLabel}.plist");
        Directory.CreateDirectory(Path.GetDirectoryName(plist)!);
        var text = Service.Replace("LABEL", ServiceLabel, StringComparison.Ordinal)
            .Replace("PROGRAM", SecurityElement.Escape(Built.Program("thermi-sensors")), StringComparison.Ordinal)
            .Replace("RECORDING", SecurityElement.Escape(Built.Recording(recording)), StringComparison.Ordinal)
            .Replace("SOCKET", SecurityElement.Escape(session.Socket), StringComparison.Ordinal);
        await File.WriteAllTextAsync(plist, text, cancel).ConfigureAwait(false);
        var (_, user) = await session.DomainsAsync(cancel).ConfigureAwait(false);
        var loaded = await session.RunAsync("launchctl", ["bootstrap", user, plist], cancel: cancel).ConfigureAwait(false);
        if (loaded.Exit != 0)
        {
            throw new InvalidOperationException($"launchd did not load the service: {loaded.Error}");
        }
    }
}
