using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Thermi.Linux.Gnome;

/// <summary>
/// What the running extension offers thermi, as its object's introspection declares it: each method by its interface's
/// last name, its own and the shape of what it takes, as <c>Shell.Move(udd)</c>, each signal as <c>Shell.Ready!(u)</c>,
/// and the parts of a monitor it tells. The Shell runs an earlier protocol's extension until the next sign-in after an
/// update, so thermi calls only what it declares; the overlay's own calls kept their shape in every protocol, and from
/// protocol 4 on each protocol only adds to the last.
/// </summary>
internal sealed partial record ShellFace(IReadOnlySet<string> Calls, int MonitorParts)
{
    /// <summary>The parts of a monitor in every protocol: its box, scale, top bar, primary and full screen, and two of the bar's boxes.</summary>
    public const int FewestMonitorParts = 12;

    private const string Prefix = "io.github.claudiopi.thermi.";

    /// <summary>No extension runs, or none answers: every call goes, and fails as the bus says.</summary>
    public static readonly ShellFace None = new(new HashSet<string>(StringComparer.Ordinal), 0);

    // The overlay's own calls and signals, in the shape each protocol has had.
    private static readonly string[] Floor =
        ["Shell.Show(uayiiiddda(dddd))", "Shell.Move(udd)", "Shell.Hide(u)", "Shell.Monitors()", "Shell.Ready!(u)", "Shell.Pointer!(usdduuudd)"];

    public bool Shows => MonitorParts >= FewestMonitorParts && Floor.All(Calls.Contains);

    /// <summary>Whether a running extension lacks the method in <paramref name="call"/>'s shape, as <c>(udd)</c>; none declared means none lacks.</summary>
    public bool Lacks(string face, string method, string call) => Calls.Count > 0 && !Calls.Contains($"{face}.{method}{call}");

    public static ShellFace Of(string xml)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var faces = XDocument.Load(reader).Root?.Elements("interface")
            .Where(face => ((string?)face.Attribute("name"))?.StartsWith(Prefix, StringComparison.Ordinal) == true)
            .ToList() ?? [];
        var calls = faces.SelectMany(face => face.Elements().Where(member => member.Name.LocalName is "method" or "signal").Select(member =>
        {
            var signal = member.Name.LocalName == "signal";
            var shape = string.Concat(member.Elements("arg").Where(arg => signal || (string?)arg.Attribute("direction") != "out")
                .Select(arg => (string?)arg.Attribute("type")));
            return $"{((string)face.Attribute("name")!)[Prefix.Length..]}.{(string?)member.Attribute("name")}{(signal ? "!" : "")}({shape})";
        }));
        var monitors = faces.Elements("method").FirstOrDefault(method => (string?)method.Attribute("name") == "Monitors")
            ?.Elements("arg").FirstOrDefault(arg => (string?)arg.Attribute("direction") == "out")?.Attribute("type")?.Value;
        return new ShellFace(calls.ToHashSet(StringComparer.Ordinal), PartsOf(monitors));
    }

    /// <summary>
    /// The parts of each monitor in a shape as <c>a(ddddddbbdddd)</c>: those of <see cref="FewestMonitorParts"/> and the
    /// boxes of the top bar, two numbers each, that later protocols added at the end; 0 for any other shape.
    /// </summary>
    public static int PartsOf(string? shape) => shape is not null && MonitorShape().Match(shape) is { Success: true } match ? match.Groups["parts"].Length : 0;

    [GeneratedRegex(@"^a\((?<parts>ddddddbbdddd(?:dd)*)\)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MonitorShape();
}
