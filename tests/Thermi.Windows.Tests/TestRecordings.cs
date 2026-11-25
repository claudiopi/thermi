using Thermi.Core.Sensors;

namespace Thermi.Windows.Tests;

/// <summary>
/// The snapshots recorded on the author's PC, with admin rights and without, and on an Intel desktop, which the test
/// projects link from <c>tests/Thermi.Tests/Recordings</c> into their own output.
/// </summary>
internal static class TestRecordings
{
    public const string Admin = "ryzen-9950x-rtx-5060-ti-admin.json";

    /// <summary>
    /// An Intel desktop as administrator: an i9-12900K, whose cores report how far each is under its limit, and an ASUS
    /// board's Nuvoton NCT6798D, whose CPU fan is its second input.
    /// </summary>
    public const string IntelAdmin = "i9-12900k-rtx-5050-admin.json";

    /// <summary>A snapshot a second on the Intel desktop, as administrator, while the CPU ran a 30 s load the board's fan answers.</summary>
    public const string IntelCpuLoad = "i9-12900k-rtx-5050-cpu-load-fans.jsonl";

    public const string NoAdmin = "ryzen-9950x-rtx-5060-ti-no-admin.json";

    /// <summary>With admin rights while a file downloaded and another was written to the drive, so the adapters and the drive read.</summary>
    public const string NetDisk = "ryzen-9950x-rtx-5060-ti-admin-net-disk.json";

    /// <summary>A snapshot a second, as administrator, while the CPU ran a 30 s load.</summary>
    public const string CpuLoad = "ryzen-9950x-rtx-5060-ti-cpu-load-fan-software.jsonl";

    /// <summary>
    /// The drives' activity and rates, a snapshot a second as the pipe sent them, across one of the service's scans for new
    /// hardware, before it took a drive's first update for its first sample.
    /// </summary>
    public const string DiskRescan = "ryzen-9950x-rtx-5060-ti-disk-rescan.jsonl";

    /// <summary>
    /// Every present for 8 s, as administrator, while Zwift, an OpenGL game, ran beside a terminal and an agent that presents
    /// through Direct3D 9, as <c>thermi-sensors --frames</c> records them.
    /// </summary>
    public const string ZwiftFrames = "ryzen-9950x-rtx-5060-ti-zwift-frames.jsonl";

    /// <summary>Zwift's presents for 8 s, each with when the trace delivered it, from 0.42 s after it at the median to 0.74 s.</summary>
    public const string ZwiftArrived = "ryzen-9950x-rtx-5060-ti-zwift-arrived.jsonl";

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Recordings", name);

    public static Snapshot Read(string name) =>
        SnapshotJson.Read(File.ReadAllText(Path(name)).ReplaceLineEndings(""))
        ?? throw new InvalidOperationException($"{name} is not a valid snapshot.");

    public static IReadOnlyList<Snapshot> Series(string name) =>
        [.. File.ReadAllText(Path(name)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => SnapshotJson.Read(line) ?? throw new InvalidOperationException($"{name} has a line that is not a valid snapshot."))];

    /// <summary>The repo's root, the folder above the test output that holds <c>thermi.slnx</c>, for the files the tests read in place.</summary>
    public static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(System.IO.Path.Combine(folder.FullName, "thermi.slnx")))
        {
            folder = folder.Parent ?? throw new DirectoryNotFoundException("thermi.slnx is above no test output.");
        }

        return folder.FullName;
    }
}
