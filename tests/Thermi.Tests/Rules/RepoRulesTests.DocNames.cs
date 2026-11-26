using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

/// <summary>
/// The names a doc gives in backticks and in its Mermaid diagrams, held to the code as it is, so a rename that forgets a
/// doc fails here. ADRs and specs keep the names of when they were written, so they are not read.
/// </summary>
public sealed partial class RepoRulesTests
{
    private static readonly string[] SearchedCode = [".cs", ".ts", ".tsx", ".ps1", ".wxs", ".csproj", ".props", ".targets", ".resx", ".txt", ".yml"];
    private static readonly string[] NamedFiles = [".cs", ".ts", ".tsx", ".ps1", ".wxs", ".csproj", ".wixproj", ".props", ".targets", ".resx", ".md"];
    private static readonly string[] OutsideNames = ["System", "Microsoft", "Windows", "Serilog", "ModelContextProtocol", "LibreHardwareMonitor"];

    private static readonly Lazy<CodeNames> Code = new(CodeNames.Scan);

    [Fact]
    public void DocsNameOnlyWhatTheCodeHas()
    {
        var findings = LivingDocs().SelectMany(doc =>
        {
            var text = File.ReadAllText(doc);
            var spans = CodeSpan().Matches(MermaidBlock().Replace(text, ""))
                .Select(match => match.Groups["span"].Value)
                .Where(span => !SpanNames(span, Path.GetDirectoryName(doc)!))
                .Select(span => $"{Relative(doc)} names `{span}`, which the code does not have");
            var diagrams = MermaidBlock().Matches(text).SelectMany(block => DiagramFindings(block.Groups["body"].Value))
                .Select(finding => $"{Relative(doc)}'s diagram {finding}");
            return spans.Concat(diagrams).Distinct(StringComparer.Ordinal);
        });

        AssertNone(findings);
    }

    private static IEnumerable<string> LivingDocs() =>
        RepoFiles([".md"]).Where(doc => !Relative(doc).StartsWith(Path.Combine("docs", "adr"), StringComparison.Ordinal)
            && !Relative(doc).StartsWith(Path.Combine("docs", "specs"), StringComparison.Ordinal)
            && Path.GetFileName(doc) != "THIRD-PARTY-NOTICES.md");

    // A span is checked when it looks like code: a path or file of the repo, or a name in PascalCase, maybe dotted.
    private static bool SpanNames(string span, string folder)
    {
        if (!span.StartsWith('.') && FileLike().IsMatch(span)
            && Path.HasExtension(span) && NamedFiles.Contains(Path.GetExtension(span), StringComparer.OrdinalIgnoreCase))
        {
            return span.Contains('/', StringComparison.Ordinal)
                ? File.Exists(Path.Combine(folder, span)) || File.Exists(Path.Combine(Root, span))
                : Code.Value.Files.Contains(span);
        }

        return !Name().IsMatch(span) || Code.Value.Has(span.TrimEnd('(', ')'));
    }

    // Each participant shown by a single name is a type, and each call a message makes is a member of its receiver's type.
    private static IEnumerable<string> DiagramFindings(string body)
    {
        var participants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in body.Split('\n').Select(each => each.Trim()))
        {
            if (Participant().Match(line) is { Success: true } participant)
            {
                var label = participant.Groups["label"].Success ? participant.Groups["label"].Value.Trim() : participant.Groups["id"].Value;
                participants[participant.Groups["id"].Value] = label;
                if (Name().IsMatch(label) && !Code.Value.Types.ContainsKey(label))
                {
                    yield return $"has the participant {label}, which is no type of the code";
                }
            }
            else if (Message().Match(line) is { Success: true } message)
            {
                var to = participants.GetValueOrDefault(message.Groups["to"].Value, message.Groups["to"].Value);
                foreach (var finding in MessageFindings(to, message.Groups["text"].Value))
                {
                    yield return finding;
                }
            }
        }
    }

    private static IEnumerable<string> MessageFindings(string to, string text)
    {
        var calls = Call().Matches(text).Select(call => call.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var badCalls = calls.Where(call => !call.Contains('.', StringComparison.Ordinal) && Code.Value.Types.ContainsKey(to) && !Code.Value.Has($"{to}.{call}"))
            .Select(call => $"calls {to}.{call}, which {to} does not have");
        var badNames = NameInText().Matches(text).Select(name => name.Value)
            .Where(name => !calls.Contains(name) && !Code.Value.Has(name))
            .Select(name => $"names {name}, which the code does not have");
        return badCalls.Concat(badNames);
    }

    [GeneratedRegex(@"```mermaid\r?\n(?<body>.*?)```", RegexOptions.Singleline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MermaidBlock();

    [GeneratedRegex(@"(?<!`)`(?<span>[^`\r\n]+)`(?!`)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"^[\w.-]+(/[\w.-]+)*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FileLike();

    // A name in PascalCase with a small letter, maybe dotted, maybe called: `SensorMap`, `SensorMap.Read`, `Edit()`.
    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*[a-z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)*(\(\))?$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Name();

    [GeneratedRegex(@"\b[A-Z][a-z0-9]+[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)*\b", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NameInText();

    [GeneratedRegex(@"(?<name>[A-Z][\w.]*)\(", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Call();

    [GeneratedRegex(@"^participant\s+(?<id>\w+)(\s+as\s+(?<label>.+))?$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Participant();

    [GeneratedRegex(@"^(?<from>\w+)\s*(-|--)(>>|>|x|\))[+-]?\s*(?<to>\w+)\s*:(?<text>.*)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Message();

    /// <summary>What the code declares and says: its types and their files, every word it uses, and its files' names.</summary>
    private sealed partial class CodeNames
    {
        public required Dictionary<string, List<string>> Types { get; init; }

        public required HashSet<string> Words { get; init; }

        public required HashSet<string> Files { get; init; }

        public required HashSet<string> Namespaces { get; init; }

        public static CodeNames Scan()
        {
            var files = RepoFiles(SearchedCode).ToList();
            var types = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var words = new HashSet<string>(StringComparer.Ordinal);
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                words.UnionWith(Word().Matches(text).Select(word => word.Value));
                namespaces.UnionWith(Namespace().Matches(text).Select(match => match.Groups["name"].Value));
                foreach (Match declared in Declared().Matches(text))
                {
                    var type = declared.Groups["type"].Value;
                    types.TryAdd(type, []);
                    types[type].Add(BodyOf(text, declared.Index));
                }
            }

            var names = FilesUnder(Root).Select(Path.GetFileName).OfType<string>()
                .Concat(DirectoriesUnder(Root).Select(Path.GetFileName).OfType<string>());
            return new CodeNames { Types = types, Words = words, Namespaces = namespaces, Files = names.ToHashSet(StringComparer.OrdinalIgnoreCase) };
        }

        // A dotted name is a namespace or package, of thermi's or outside it, or a type and a member its own body declares:
        // the name a method, property, field, record parameter or enum value is given there, not one the type only uses.
        public bool Has(string name)
        {
            var parts = name.Split('.');
            if (parts.Length == 1)
            {
                return Words.Contains(name) || Files.Contains(name);
            }

            if (OutsideNames.Contains(parts[0], StringComparer.Ordinal) || Namespaces.Contains(name) || (parts[0] == "Thermi" && Files.Contains(name)))
            {
                return true;
            }

            if (!Types.TryGetValue(parts[^2], out var declaring))
            {
                return !Types.ContainsKey(parts[0]) && Array.TrueForAll(parts, Words.Contains);
            }

            var member = Regex.Escape(parts[^1]);
            var declares = $@"(?<=[\w>\]?)]\s+){member}\s*[(<{{=;,)]|^\s*{member}\s*[,=]?\s*$";
            return declaring.Exists(body => Regex.IsMatch(body, declares, RegexOptions.Multiline, TimeSpan.FromSeconds(1)));
        }

        // A type's own text: its declaration to the brace that closes its body, or to the semicolon of one without a body.
        private static string BodyOf(string text, int start)
        {
            var open = text.IndexOfAny(['{', ';'], start);
            if (open < 0 || text[open] == ';')
            {
                return text[start..(open < 0 ? text.Length : open + 1)];
            }

            var depth = 0;
            for (var at = open; at < text.Length; at++)
            {
                depth += text[at] switch { '{' => 1, '}' => -1, _ => 0 };
                if (depth == 0)
                {
                    return text[start..(at + 1)];
                }
            }

            return text[start..];
        }

        [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Word();

        [GeneratedRegex(@"^namespace (?<name>[\w.]+);", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Namespace();

        [GeneratedRegex(@"\b(class|record|struct|interface|enum|function|type|const)\s+(?<type>[A-Z]\w*)", RegexOptions.ExplicitCapture,
            matchTimeoutMilliseconds: 1000)]
        private static partial Regex Declared();
    }

    private static IEnumerable<string> DirectoriesUnder(string folder) =>
        Directory.EnumerateDirectories(folder)
            .Where(child => !SkippedFolders.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            .SelectMany(child => DirectoriesUnder(child).Prepend(child));
}
