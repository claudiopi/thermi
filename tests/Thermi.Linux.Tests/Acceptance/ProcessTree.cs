using System.Globalization;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>The processes a process started, and theirs, which a window's own end has to take with it.</summary>
internal static class ProcessTree
{
    /// <summary>
    /// Every process under <paramref name="root"/>, as the kernel's table says each one's parent: the field after the name
    /// in /proc/&lt;pid&gt;/stat, whose name may hold spaces and ends at its last parenthesis.
    /// </summary>
    public static List<int> Under(int root)
    {
        var parents = new Dictionary<int, int>();
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(folder), out var pid) && File.Exists(Path.Combine(folder, "stat")))
            {
                try
                {
                    var stat = File.ReadAllText(Path.Combine(folder, "stat"));
                    parents[pid] = int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1], CultureInfo.InvariantCulture);
                }
                catch (IOException)
                {
                    // It ended while the table was read.
                }
            }
        }

        var found = new List<int>();
        var next = new Queue<int>([root]);
        while (next.TryDequeue(out var parent))
        {
            foreach (var child in parents.Where(pair => pair.Value == parent).Select(pair => pair.Key))
            {
                found.Add(child);
                next.Enqueue(child);
            }
        }

        return found;
    }
}
