namespace Thermi.Linux.Tests;

/// <summary>What the build and the repo put where the tests read it: thermi's programs, and the recorded Linux PCs.</summary>
internal static class Built
{
    /// <summary>The repo's root, the folder above the test output that holds <c>thermi.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>One of thermi's programs, <c>thermi</c>, <c>thermi-cli</c> or <c>thermi-sensors</c>, as the build puts it beside the tests.</summary>
    public static string Program(string name) => Path.Combine(AppContext.BaseDirectory, name);

    /// <summary>Where the build puts the packages it packed, the AUR's PKGBUILD with them, and a newer build in <c>newer</c>.</summary>
    public static string Packages { get; } = Path.Combine(Root, "artifacts", "linux");

    /// <summary>A build of a newer version than <see cref="Packages"/>', packed with the same key, as the next release.</summary>
    public static string Newer { get; } = Path.Combine(Packages, "newer");

    /// <summary>The version of the packages in <paramref name="folder"/>, as the sensors' .deb's name says it.</summary>
    public static string PackedVersion(string folder) =>
        Path.GetFileName(Directory.GetFiles(folder, "thermi-sensors_*_amd64.deb").Single())["thermi-sensors_".Length..^"_amd64.deb".Length];

    /// <summary>
    /// A tree of the files under <c>/sys</c> and <c>/proc</c> that <c>thermi-sensors --tree</c> read on a Linux PC; its
    /// snapshots, as <c>--dump</c> saved them, are beside it, in its name's <c>.json</c>.
    /// </summary>
    public static string Tree(string name) => Path.Combine(Root, "tests", "Thermi.Tests", "Recordings", "Trees", name);

    /// <summary>
    /// Every file of the folder <paramref name="from"/> copied to <paramref name="to"/>. Asked to replace what is there,
    /// each file is copied beside its old one and renamed over it, as a package manager does: a program running from the
    /// old file keeps it, where writing over it would fail or crash the program.
    /// </summary>
    public static void Copy(string from, string to, bool replace = false)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var copy = replace ? $"{target}.new-{Guid.NewGuid():N}" : target;
            File.Copy(file, copy);
            if (replace)
            {
                File.Move(copy, target, overwrite: true);
            }
        }
    }

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
