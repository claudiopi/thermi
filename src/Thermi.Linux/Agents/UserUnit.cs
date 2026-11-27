using Thermi.Cli.Agents;

namespace Thermi.Linux.Agents;

/// <summary>
/// The start at sign-in on Linux: the overlay's systemd user unit, <c>thermi.service</c>, enabled for the graphical session
/// as <c>systemctl --user enable</c> enables it, by the link in the user's own units' folder, and disabled by removing it.
/// It never starts or stops the unit, and never touches a system unit.
/// </summary>
internal sealed class UserUnit : IStartAtSignIn
{
    public const string Name = "thermi.service";

    // The target the unit's [Install] section names, which a graphical session starts.
    private const string Target = "graphical-session.target";

    private readonly string _own;

    public UserUnit(string config, string data)
    {
        _own = Path.Combine(config, "systemd", "user");
        // The user's own folders first, then the system's, in the order systemd looks.
        Folders = [_own, Path.Combine(data, "systemd", "user"), "/etc/systemd/user", "/usr/local/lib/systemd/user", "/usr/lib/systemd/user"];
    }

    public IReadOnlyList<string> Folders { get; }

    /// <summary>The unit's file, where systemd would find it, or null when no package or test put one.</summary>
    public string? File => Folders.Select(folder => Path.Combine(folder, Name)).FirstOrDefault(System.IO.File.Exists);

    public bool On => new FileInfo(Link).LinkTarget is not null;

    private string Link => Path.Combine(_own, $"{Target}.wants", Name);

    /// <summary>The user's unit, in the folders <c>XDG_CONFIG_HOME</c> and <c>XDG_DATA_HOME</c> name, or their defaults.</summary>
    public static UserUnit Of() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public void Set(bool on)
    {
        if (System.IO.File.Exists(Link) || On)
        {
            System.IO.File.Delete(Link);
        }

        if (on)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Link)!);
            System.IO.File.CreateSymbolicLink(Link, File ?? Path.Combine(Folders[^1], Name));
        }
    }
}
