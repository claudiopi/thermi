using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Thermi.Core.Sensors;
using Thermi.MacOS.Cli.Native;

namespace Thermi.MacOS.Cli.Agents;

/// <summary>
/// thermi's two LaunchAgents, plists in the person's <c>~/Library/LaunchAgents</c> that launchd loads as the person, with no
/// administrator (docs/adr/0021-macos.md): the service's, in their background session, so it serves their desktop and
/// their SSH sessions alike, started by its socket's first client and every 4 hours to check for updates; and the
/// overlay's, in their graphical session, started at login while Open at login is on, and again when it fails, but not
/// once it exits with 0, as Exit thermi does. <c>thermi register</c>, which the app's first start and the cask run,
/// writes and loads both; <c>thermi unregister</c> takes both out and leaves the settings. thermi and <c>thermi-cli</c>
/// share it, as both answer for the agents.
/// </summary>
internal static class LaunchAgents
{
    public const string ServiceLabel = "io.github.claudiopi.thermi.sensors";

    public const string OverlayLabel = "io.github.claudiopi.thermi";

    // The socket's name, which the service asks launchd for, and its mode, 0600: the person's alone.
    private const string Listener = "Listener";
    private const int PersonOnly = 384;

    // Every 4 hours, in seconds.
    private const int UpdateCheck = 4 * 60 * 60;

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

    /// <summary>Where an agent's plist is, by its label, in the person's home.</summary>
    public static string PlistOf(string label) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{label}.plist");

    /// <summary>
    /// Writes both agents, with the overlay's started at login or not, and loads each into its session's domain, after taking
    /// out one an earlier register loaded. A Mac reached over SSH has no graphical session to load the overlay's into, which
    /// loads at the next login; it says so, and registers all the same.
    /// </summary>
    public static async Task<int> RegisterAsync(bool openAtLogin, TextWriter error, TimeProvider time, CancellationToken cancel)
    {
        var programs = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var socket = SnapshotJson.MacSocketPath();
        Directory.CreateDirectory(Path.GetDirectoryName(socket)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Write(ServiceLabel, Service(Path.Combine(programs, "thermi-sensors"), socket));
        Write(OverlayLabel, Overlay(Path.Combine(programs, "thermi"), openAtLogin));
        var service = await LoadAsync(UserDomain, ServiceLabel, time, cancel);
        if (service is not null)
        {
            await error.WriteLineAsync($"launchd did not load thermi's sensor service: {service}");
            return 1;
        }

        if (await LoadAsync(GuiDomain, OverlayLabel, time, cancel) is { } overlay)
        {
            await error.WriteLineAsync($"thermi's overlay starts at the next login, as this session has no desktop to start it in now: {overlay}");
        }

        return 0;
    }

    /// <summary>Takes both agents out of launchd and their plists away; the person's settings stay.</summary>
    public static async Task<int> UnregisterAsync(CancellationToken cancel)
    {
        foreach (var (domain, label) in new[] { (GuiDomain, OverlayLabel), (UserDomain, ServiceLabel) })
        {
            await LaunchctlAsync(["bootout", $"{domain}/{label}"], cancel);
            File.Delete(PlistOf(label));
        }

        return 0;
    }

    /// <summary>Whether launchd starts the overlay's agent at login, as its plist says.</summary>
    public static bool OpensAtLogin()
    {
        try
        {
            using var reader = XmlReader.Create(PlistOf(OverlayLabel), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var plist = XDocument.Load(reader);
            return plist.Descendants("key").FirstOrDefault(key => key.Value == "RunAtLoad")?.ElementsAfterSelf().FirstOrDefault()?.Name == "true";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }
    }

    /// <summary>Starts the overlay at login, or not, from the next login on, as launchd reads the plists again then.</summary>
    public static void SetOpenAtLogin(bool on) => Write(OverlayLabel, Overlay(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "thermi"), on));

    private static string UserDomain => string.Create(CultureInfo.InvariantCulture, $"user/{User.Id}");

    private static string GuiDomain => string.Create(CultureInfo.InvariantCulture, $"gui/{User.Id}");

    private static XElement Service(string program, string socket) => Dictionary(
        ("Label", new XElement("string", ServiceLabel)),
        ("ProgramArguments", new XElement("array", new XElement("string", program))),
        ("LimitLoadToSessionType", new XElement("string", "Background")),
        ("ProcessType", new XElement("string", "Background")),
        ("StartInterval", new XElement("integer", UpdateCheck)),
        ("Sockets", Dictionary((Listener, Dictionary(
            ("SockPathName", new XElement("string", socket)),
            ("SockPathMode", new XElement("integer", PersonOnly)))))));

    private static XElement Overlay(string program, bool openAtLogin) => Dictionary(
        ("Label", new XElement("string", OverlayLabel)),
        ("ProgramArguments", new XElement("array", new XElement("string", program))),
        ("LimitLoadToSessionType", new XElement("string", "Aqua")),
        ("ProcessType", new XElement("string", "Interactive")),
        ("RunAtLoad", new XElement(openAtLogin ? "true" : "false")),
        ("KeepAlive", Dictionary(("SuccessfulExit", new XElement("false")))));

    private static XElement Dictionary(params (string Key, XElement Value)[] entries) =>
        new("dict", entries.SelectMany(entry => new[] { new XElement("key", entry.Key), entry.Value }));

    // A whole file in place of the last, so launchd never reads half of one.
    private static void Write(string label, XElement dictionary)
    {
        var path = PlistOf(label);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // launchd and plutil read a plist with no document type, which names a DTD nothing fetches.
        var plist = new XDocument(new XElement("plist", new XAttribute("version", "1.0"), dictionary));
        var written = $"{path}.new";
        using (var writer = XmlWriter.Create(written, new XmlWriterSettings { Indent = true, IndentChars = "  " }))
        {
            plist.Save(writer);
        }

        File.Move(written, path, overwrite: true);
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
            (var exit, said) = await LaunchctlAsync(["bootstrap", domain, PlistOf(label)], cancel);
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
