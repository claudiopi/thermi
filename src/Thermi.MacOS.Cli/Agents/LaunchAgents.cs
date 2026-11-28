using System.Xml;
using System.Xml.Linq;
using Thermi.Core.Sensors;

namespace Thermi.MacOS.Cli.Agents;

/// <summary>
/// thermi's two LaunchAgents, plists in the person's <c>~/Library/LaunchAgents</c> that launchd loads as the person, with no
/// administrator (docs/adr/0021-macos.md): the service's, in their background session, so it serves their desktop and
/// their SSH sessions alike, started by its socket's first client and every 4 hours to check for updates; and the
/// overlay's, in their graphical session, started at login while Open at login is on, and again when it fails, but not
/// once it exits with 0, as Exit thermi does. <c>thermi-cli</c> only writes them, as Open at login does; <c>thermi
/// register</c> writes them and loads them into launchd, which no agent's command does.
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

    public static string PlistOf(string label) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{label}.plist");

    /// <summary>
    /// Writes both agents: the service's, to run the <c>thermi-sensors</c> beside this program on the socket the Mac's
    /// clients read, and the overlay's, started at login or not.
    /// </summary>
    public static void WriteBoth(bool openAtLogin)
    {
        var socket = SnapshotJson.MacSocketPath();
        Directory.CreateDirectory(Path.GetDirectoryName(socket)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Write(ServiceLabel, Service(Path.Combine(Programs, "thermi-sensors"), socket));
        SetOpenAtLogin(openAtLogin);
    }

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
    public static void SetOpenAtLogin(bool on) => Write(OverlayLabel, Overlay(Path.Combine(Programs, "thermi"), on));

    // thermi, thermi-cli and thermi-sensors sit side by side in the app.
    private static string Programs => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

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
        // launchd and plutil read a plist with no document type, which only names a DTD nothing fetches.
        var plist = new XDocument(new XElement("plist", new XAttribute("version", "1.0"), dictionary));
        var written = $"{path}.new";
        using (var writer = XmlWriter.Create(written, new XmlWriterSettings { Indent = true, IndentChars = "  " }))
        {
            plist.Save(writer);
        }

        File.Move(written, path, overwrite: true);
    }
}
