extern alias sensors;

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost.Updates;
using Thermi.Core.Updates;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// A PC as the updater sees it: an install folder of the test's own under the test output, running <see cref="Running"/>,
/// a channel served from this PC's loopback and signed with keys of the test's own, the policy an administrator left, the
/// network, and what the service wrote to its event log and the watchdogs it started.
/// </summary>
internal sealed class UpdatePc : IAsyncDisposable
{
    public static readonly ReleaseVersion Running = new(0, 8, 0);

    /// <summary>The version the test's own build of thermi's programs carries, so a zip of them passes the version check.</summary>
    public static readonly ReleaseVersion Built =
        ReleaseVersion.Parse(FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "thermi.dll")).ProductVersion)!;

    // A user's folder, which only the folder checks of the real PC would refuse: the tests run without admin rights.
    private static readonly FolderAccess Anyone = new(
        Directory.Exists, (parent, prefix) => Directory.CreateDirectory(Path.Combine(parent, prefix + Guid.NewGuid().ToString("N"))).FullName, () => null);

    private readonly ECDsa _release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _backup = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly List<IDisposable> _owned = [];
    private readonly List<(string Message, EventLogEntryType Type)> _events = [];
    private readonly Lock _lock = new();

    /// <summary>A PC that runs <see cref="Running"/>, with its launchers at the top; a fresh one has no <c>update\</c> yet, as after an install.</summary>
    public UpdatePc(bool fresh = false)
    {
        Lay(Running);
        Folder().PlaceLaunchers(Running);
        if (!fresh)
        {
            Directory.CreateDirectory(Path.Combine(Top, "update"));
            Folder().Write(new UpdateState { Installed = Running });
        }
    }

    public string Top { get; } = Path.Combine(AppContext.BaseDirectory, "update-pcs", Guid.NewGuid().ToString("N"));

    public LocalChannel Channel { get; } = new();

    public WaitingClock Clock { get; } = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

    public (object? Updates, object? Channel) Policy { get; set; }

    public bool Metered { get; set; }

    /// <summary>The program of each watchdog the service would have started; none starts.</summary>
    public List<string> Watchdogs { get; } = [];

    public string ReleaseKeyHash => Convert.ToHexStringLower(SHA256.HashData(_release.ExportSubjectPublicKeyInfo()));

    public IReadOnlyList<(string Message, EventLogEntryType Type)> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    public UpdateState State => Folder().Read();

    /// <summary>The versions with a folder at the top.</summary>
    public IReadOnlyList<ReleaseVersion> Versions =>
        [.. Directory.EnumerateDirectories(Top).Select(folder => ReleaseVersion.Parse(Path.GetFileName(folder))).OfType<ReleaseVersion>().Order()];

    /// <summary>Every file outside <c>update\</c>, with its bytes' hash: what is installed.</summary>
    public IReadOnlyList<string> Installed =>
    [
        .. Directory.EnumerateFiles(Top, "*", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(Path.Combine(Top, "update") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(Top, file)} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))}"),
    ];

    /// <summary>A version's folder as an update unpacks it, its programs and launchers each saying which version they are.</summary>
    public void Lay(ReleaseVersion version)
    {
        Directory.CreateDirectory(Path.Combine(Top, version.ToString(), "launchers"));
        foreach (var program in Unpacker.Programs)
        {
            File.WriteAllText(Path.Combine(Top, version.ToString(), program + ".exe"), $"{program} {version}");
            File.WriteAllText(Path.Combine(Top, version.ToString(), "launchers", program + ".exe"), Launcher(program, version));
        }
    }

    public static string Launcher(string program, ReleaseVersion version) => $"{program} launcher {version}";

    /// <summary>The launcher of <paramref name="program"/> at the top, as its text.</summary>
    public string LauncherAtTop(string program) => File.ReadAllText(Path.Combine(Top, program + ".exe"));

    /// <summary>The folder as the updater sees it, trusted unless it is to meet the real check of a folder a user can write.</summary>
    public InstallFolder Folder(bool trusted = true) => new(Top, trusted ? Access : null);

    /// <summary>How the folder is checked: as anyone's by default, since the tests run without admin rights.</summary>
    public FolderAccess Access { get; set; } = Anyone;

    /// <summary>What a switch stops and starts: the services unless a test says otherwise, as a per-user install's host.</summary>
    public Func<InstallFolder, ISwitchTarget>? Target { get; set; }

    /// <summary>The arguments of each watchdog the service would have started, joined by spaces.</summary>
    public List<string> WatchdogArguments { get; } = [];

    /// <summary>A build with the test's keys built in, or one without, which never updates itself.</summary>
    public UpdateBuild Build(bool keys = true, bool catalog = false, ReleaseVersion? version = null) => keys
        ? new UpdateBuild(
            Channel.Address,
            _release.ExportSubjectPublicKeyInfo(),
            _backup.ExportSubjectPublicKeyInfo(),
            catalog ? [Thumbprint] : null,
            version ?? Running,
            "x64")
        : new UpdateBuild(null, null, [], Signers: null, version ?? Running, "x64");

    /// <summary>A certificate's thumbprint that signed nothing here.</summary>
    public const string Thumbprint = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";

    public Updater Updater(UpdateBuild? build = null, bool trusted = true)
    {
        // A clock of its own, never stepped: the updater's counts each wait, and a download's idle limit is no wait of its.
        var client = new ManifestClient(new FakeTimeProvider());
        var host = new UpdateHost(() => Policy, () => Metered, watchdog =>
        {
            Watchdogs.Add(watchdog.FileName);
            WatchdogArguments.Add(string.Join(' ', watchdog.ArgumentList));
        });
        var updater = new Updater(build ?? Build(), Folder(trusted), client, Clock, Say, host, Target);
        _owned.Add(updater);
        _owned.Add(client);
        return updater;
    }

    /// <summary>A zip of <paramref name="version"/> made of the test build's programs, served under <c>zips\</c>, and its package.</summary>
    public Package Zip(ReleaseVersion version, Action<ZipArchive>? change = null)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var program in Unpacker.Programs)
            {
                zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, program + ".dll"), program + ".dll");
                Write(zip, program + ".exe", $"{program} {version}");
                Write(zip, $"launchers/{program}.exe", Launcher(program, version));
            }

            change?.Invoke(zip);
        }

        var path = $"/v1/zips/{version}/thermi-{version}-x64.zip";
        Channel.Serve(path, bytes.ToArray());
        return new Package(new Uri(Channel.Address, path), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
    }

    /// <summary>A manifest of the stable channel offering <paramref name="version"/>, expiring in 30 days.</summary>
    public Manifest Offer(ReleaseVersion version, Package package, long sequence = 2) => new()
    {
        Channel = Manifest.Stable,
        Sequence = sequence,
        Expires = Clock.GetUtcNow().AddDays(30),
        Version = version,
        Packages = new Dictionary<string, Package>(StringComparer.Ordinal) { ["x64"] = package },
    };

    /// <summary>Serves <paramref name="manifest"/> as its channel's envelope, sealed with the release key, the backup or <paramref name="other"/>.</summary>
    public void Publish(Manifest manifest, bool backup = false, ECDsa? other = null, string? at = null) =>
        Channel.Serve($"/v1/{at ?? manifest.Channel}.json", ManifestEnvelope.Seal(manifest, other ?? (backup ? _backup : _release)));

    public async ValueTask DisposeAsync()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }

        await Channel.DisposeAsync();
        Clock.Dispose();
        _release.Dispose();
        _backup.Dispose();
        Directory.Delete(Top, recursive: true);
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    private void Say(string message, EventLogEntryType type)
    {
        lock (_lock)
        {
            _events.Add((message, type));
        }
    }
}
