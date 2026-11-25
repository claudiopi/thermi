using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Thermi.Commands;

/// <summary>
/// The driver's official installer, as thermi's build pins it: its release's URL, size and SHA-256, the switches that
/// run it unattended, and its version. A new version is a change of all five together, after a person checks the release.
/// </summary>
internal sealed record DriverSetup(Uri Url, long Size, string Sha256, IReadOnlyList<string> Arguments, Version Version)
{
    public static DriverSetup Pinned { get; } = new(
#pragma warning disable S1075 // Pinned on purpose: a URL from anywhere else could be steered (docs/adr/0010-driver-and-service-from-the-window.md).
        new Uri("https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe"),
#pragma warning restore S1075
        3_410_960,
        "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032",
        ["-install", "-silent"],
        new Version(2, 2, 0));

    /// <summary>
    /// Whether a driver installed as <paramref name="displayVersion"/> is this one or a later one, which is never installed
    /// over; an older one, or one whose version cannot be read, is.
    /// </summary>
    public bool Covers(string? displayVersion) => Version.TryParse(displayVersion, out var installed) && installed >= Version;
}

/// <summary>The download, which tests replace: the file at the URL into the stream, false when it fails or runs past the size.</summary>
internal interface IDriverDownload
{
    Task<bool> DownloadAsync(Uri url, Stream into, long size, CancellationToken cancel);
}

/// <summary>The installer's file and process, which tests replace.</summary>
internal interface IDriverInstaller
{
    /// <summary>Whether the open file holds a valid Authenticode signature.</summary>
    bool Signed(FileStream file);

    /// <summary>Runs the file unattended and waits up to <paramref name="wait"/>: its exit code, or null when it did not end.</summary>
    Task<int?> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan wait, TimeProvider time);
}

/// <summary>
/// What the driver's install reaches: the pinned setup, a new folder only administrators can write, or null, the
/// download, the installer, the sensor service, and the version of the driver installed already, null when there is none.
/// </summary>
internal sealed record DriverParts(
    DriverSetup Setup, Func<string?> Folder, IDriverDownload Download, IDriverInstaller Installer, IServiceManager Services, Func<string?> Installed)
{
    // Where its setup lists it, as LibreHardwareMonitor finds it.
    private const string PawnIoKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public static DriverParts Windows(IServiceManager services, ILogger log) =>
        new(DriverSetup.Pinned, DriverFolder.Prepare, new HttpDownload(log), new WindowsInstaller(), services, InstalledVersion);

    private static string? InstalledVersion()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(PawnIoKey);
        return key?.GetValue("DisplayVersion") as string;
    }
}

/// <summary>
/// "Install driver", as administrator: the pinned installer, downloaded into a new folder only administrators can write,
/// alone there, checked for its size, then its SHA-256, then its signature, run unattended and deleted with its folder,
/// and then the sensor service started again so it opens the driver. Trust rests on the hash, never on the connection.
/// </summary>
internal static class InstallDriver
{
    public const string Verb = "install-driver";

    /// <summary>The exit code when the driver installed and the sensor service runs again.</summary>
    public const int Installed = 0;

    /// <summary>The exit code when the driver installed but the sensor service did not run again in time.</summary>
    public const int SensorsFailed = StartSensors.Failed;

    /// <summary>The exit code when the sensor service is not installed, which reinstalling thermi fixes.</summary>
    public const int ServiceMissing = StartSensors.Missing;

    public const int NotDownloaded = 4;

    /// <summary>The exit code when the file is not the pinned one: its size, its hash or its signature.</summary>
    public const int NotTrusted = 5;

    /// <summary>The exit code when the installer failed or did not end within <see cref="InstallWait"/>.</summary>
    public const int NotInstalled = 6;

    /// <summary>
    /// The exit code when a folder is a link, someone other than an administrator owns it or may write it, or the
    /// installer's holds anything else.
    /// </summary>
    public const int FolderUnsafe = 7;

    /// <summary>The exit code when the driver installed but loads only once Windows restarts.</summary>
    public const int RestartNeeded = 8;

    public const string FileName = "driver-setup.exe";

    public static readonly TimeSpan DownloadWait = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan InstallWait = TimeSpan.FromMinutes(2);

    // Installed, with Windows to restart for it to load.
    private const int RebootRequired = 3010;

    public static async Task<int> RunAsync(DriverParts parts, TimeProvider time)
    {
        // The pinned one or a later one, as another tool's, is never installed over, and an older one is, as the sensors may
        // not open it: the sensors only start again, as a per-user install made for everyone asks whatever the driver.
        if (parts.Setup.Covers(parts.Installed()))
        {
            return await StartSensors.RestartAsync(parts.Services, time).ConfigureAwait(false);
        }

        if (parts.Folder() is not { } folder)
        {
            return FolderUnsafe;
        }

        var path = Path.Combine(folder, FileName);
        int code;
        try
        {
            code = await InstallAsync(parts, path, time).ConfigureAwait(false);
        }
        finally
        {
            Delete(folder);
        }

        // A driver that loads only once Windows restarts gives the service nothing to open yet, so it is left alone.
        return code == Installed ? await StartSensors.RestartAsync(parts.Services, time).ConfigureAwait(false) : code;
    }

    private static async Task<int> InstallAsync(DriverParts parts, string path, TimeProvider time)
    {
        if (!await DownloadAsync(parts, path, time).ConfigureAwait(false))
        {
            return NotDownloaded;
        }

        // Held open, shared only for reading, from the checks to the installer's end, so what runs is what was checked.
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Windows loads DLLs from the installer's own folder first, as administrator, so it runs alone there.
        if (!Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).SequenceEqual([path], StringComparer.OrdinalIgnoreCase))
        {
            return FolderUnsafe;
        }

        if (held.Length != parts.Setup.Size || !Matches(held, parts.Setup.Sha256) || !parts.Installer.Signed(held))
        {
            return NotTrusted;
        }

        return await parts.Installer.RunAsync(path, parts.Setup.Arguments, InstallWait, time).ConfigureAwait(false) switch
        {
            0 => Installed,
            RebootRequired => RestartNeeded,
            _ => NotInstalled,
        };
    }

    private static async Task<bool> DownloadAsync(DriverParts parts, string path, TimeProvider time)
    {
        using var cancel = new CancellationTokenSource(DownloadWait, time);
        try
        {
            // Unbuffered: the download writes whole chunks, and closing then has nothing left to write.
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0);
            return await parts.Download.DownloadAsync(parts.Setup.Url, file, parts.Setup.Size, cancel.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    private static bool Matches(FileStream file, string sha256)
    {
        file.Position = 0;
        var hash = SHA256.HashData(file);
        file.Position = 0;
        return string.Equals(Convert.ToHexString(hash), sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What an installer's own children still hold stays, harmless where only administrators write.
        }
    }
}
