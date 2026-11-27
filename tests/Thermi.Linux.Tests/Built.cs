namespace Thermi.Linux.Tests;

/// <summary>What the build and the repo put where the tests read it: thermi's programs, and the recorded Linux PCs.</summary>
internal static class Built
{
    /// <summary>The repo's root, the folder above the test output that holds <c>thermi.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>One of thermi's programs, <c>thermi</c>, <c>thermi-cli</c> or <c>thermi-sensors</c>, as the build puts it beside the tests.</summary>
    public static string Program(string name) => Path.Combine(AppContext.BaseDirectory, name);

    /// <summary>
    /// A tree of the files under <c>/sys</c> and <c>/proc</c> that <c>thermi-sensors --tree</c> read on a Linux PC; its
    /// snapshots, as <c>--dump</c> saved them, are beside it, in its name's <c>.json</c>.
    /// </summary>
    public static string Tree(string name) => Path.Combine(Root, "tests", "Thermi.Tests", "Recordings", "Trees", name);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "thermi.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("thermi.slnx not found above the test output folder.");
    }
}
