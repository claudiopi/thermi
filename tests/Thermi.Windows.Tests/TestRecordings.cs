using Thermi.Core.Sensors;

namespace Thermi.Windows.Tests;

/// <summary>
/// The snapshots recorded on the author's PC, with admin rights and without, which the test projects link from
/// <c>tests/Thermi.Tests/Recordings</c> into their own output.
/// </summary>
internal static class TestRecordings
{
    public const string Admin = "ryzen-9950x-rtx-5060-ti-admin.json";

    public const string NoAdmin = "ryzen-9950x-rtx-5060-ti-no-admin.json";

    /// <summary>With admin rights while a file downloaded and another was written to the drive, so the adapters and the drive read.</summary>
    public const string NetDisk = "ryzen-9950x-rtx-5060-ti-admin-net-disk.json";

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Recordings", name);

    public static Snapshot Read(string name) =>
        SnapshotJson.Read(File.ReadAllText(Path(name)).ReplaceLineEndings(""))
        ?? throw new InvalidOperationException($"{name} is not a valid snapshot.");

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
