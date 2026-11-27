using System.Globalization;

namespace Thermi.Sensors.Linux.Kernel;

/// <summary>
/// A tree <c>thermi-sensors --tree</c> recorded: a folder for each second, from 0, holding the files under <c>/sys</c> and
/// <c>/proc</c> it read then, at their paths, and that second's answers that are no file (<see cref="KernelJson"/>). It
/// replays its seconds in turn, and from the first again after the last, so a test's service serves as long as it is asked.
/// </summary>
internal sealed class RecordedKernel : IKernel
{
    private readonly int _seconds;
    private int _second = -1;
    private Dictionary<string, string> _links = new(StringComparer.Ordinal);

    public RecordedKernel(string tree)
    {
        Tree = tree;
        _seconds = Directory.EnumerateDirectories(tree)
            .Count(folder => int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out _));
        if (_seconds == 0)
        {
            throw new ArgumentException($"{tree} holds no recorded second: record one with thermi-sensors --tree.", nameof(tree));
        }
    }

    private string Tree { get; }

    private string Second => Path.Combine(Tree, _second.ToString(CultureInfo.InvariantCulture));

    public string? Read(string path) => IKernel.Holds(path) ? Text(InTree(path)) : null;

    // An entry the readers only resolved, as a drive of /sys/block they found virtual, is a link of the second's and no file
    // of its folder, and is listed as the kernel listed it.
    public IReadOnlyList<string> List(string folder)
    {
        var inTree = InTree(folder);
        var files = Directory.Exists(inTree) ? Directory.EnumerateFileSystemEntries(inTree).Select(Path.GetFileName).OfType<string>().Select(Unescaped) : [];
        var within = $"{folder.TrimEnd('/')}/";
        var linked = _links.Keys.Where(path => path.StartsWith(within, StringComparison.Ordinal))
            .Select(path => path[within.Length..])
            .Where(name => !name.Contains('/', StringComparison.Ordinal));
        return [.. files.Union(linked, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    public string? Resolve(string path) => _links.GetValueOrDefault(path);

    public Moment? Now() => KernelJson.ReadMoment(Answer(KernelJson.MomentFile));

    public IReadOnlyList<GpuAnswer>? Nvml() => KernelJson.ReadNvml(Answer(KernelJson.NvmlFile));

    public void Next()
    {
        _second = (_second + 1) % _seconds;
        _links = KernelJson.ReadLinks(Answer(KernelJson.LinksFile));
    }

    private static string? Text(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? Answer(string file) => Text(InTree(file));

    /// <summary>
    /// A kernel's path as a tree keeps it, under its second's folder: a colon, as in <c>intel-rapl:0</c>, which Windows
    /// forbids in a file's name, as <c>%3A</c>, and so a percent sign as <c>%25</c>.
    /// </summary>
    public static string Escaped(string path) =>
        path.TrimStart('/').Replace("%", "%25", StringComparison.Ordinal).Replace(":", "%3A", StringComparison.Ordinal);

    private static string Unescaped(string name) => name.Replace("%3A", ":", StringComparison.Ordinal).Replace("%25", "%", StringComparison.Ordinal);

    private string InTree(string path) => Path.Combine(Second, Escaped(path));
}
