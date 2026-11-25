using Thermi.Core.Frames;
using Thermi.Core.Sensors;

namespace Thermi.Tests;

/// <summary>
/// The files committed beside the tests beyond the single snapshots of <see cref="TestRecordings"/>: series recorded on
/// real PCs, and settings files as each version wrote them.
/// </summary>
internal static class TestFiles
{
    /// <summary>A series: one snapshot a second, one line each, as <c>thermi-sensors --dump --seconds</c> records it.</summary>
    public static IReadOnlyList<Snapshot> Series(string name) =>
        [.. Lines(name).Select(line => SnapshotJson.Read(line) ?? throw new InvalidOperationException($"{name} has a line that is not a valid snapshot."))];

    /// <summary>Presents, one a line, as <c>thermi-sensors --frames</c> records them.</summary>
    public static IReadOnlyList<Present> Presents(string name) =>
        [.. Lines(name).Select(line => PresentJson.Read(line) ?? throw new InvalidOperationException($"{name} has a line that is not a present."))];

    /// <summary>The repo's root, the folder above the test output that holds <c>thermi.slnx</c>, for the files the tests read in place.</summary>
    public static string Root { get; } = FindRoot();

    public static string Config(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Configs", name));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "thermi.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("thermi.slnx not found above the test output folder.");
    }

    private static string[] Lines(string name) =>
        File.ReadAllText(TestRecordings.Path(name)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
