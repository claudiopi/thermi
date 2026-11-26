using System.Xml.Linq;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // What every OS's host shares runs on any OS (docs/adr/0018-shared-and-windows-projects.md): it targets plain net10.0,
    // and no project it builds on, however far down, targets Windows alone.
    [Fact]
    public void SharedProjectsNeedNoWindows()
    {
        string[] shared = ["Thermi.Core", "Thermi.App", "Thermi.Cli", "Thermi.Client"];

        AssertNone(shared.SelectMany(name => WindowsOnly(name, Path.Combine(Root, "src", name, $"{name}.csproj"))));
    }

    // The project and every project it references, each whose target framework is not plain .NET.
    private static IEnumerable<string> WindowsOnly(string shared, string project)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([Path.GetFullPath(project)]);
        while (pending.TryPop(out var next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            var xml = XDocument.Load(next);
            var target = string.Concat(xml.Descendants()
                .Where(node => node.Name.LocalName is "TargetFramework" or "TargetFrameworks").Select(node => node.Value));
            if (target.Length == 0 || target.Contains("-windows", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{shared} builds on {Relative(next)}, which targets {(target.Length == 0 ? "no framework of its own" : target)}";
            }

            foreach (var reference in xml.Descendants().Where(node => node.Name.LocalName == "ProjectReference"))
            {
                var path = reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar);
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(next)!, path)));
            }
        }
    }
}
