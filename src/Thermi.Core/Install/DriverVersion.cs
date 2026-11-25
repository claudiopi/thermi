namespace Thermi.Core.Install;

/// <summary>Which sensor driver installed already the elevated mode's "install driver" leaves alone.</summary>
public static class DriverVersion
{
    /// <summary>
    /// Whether a driver installed as <paramref name="displayVersion"/> is <paramref name="pinned"/> or a later one, which is
    /// never installed over; an older one, or one whose version cannot be read, is.
    /// </summary>
    public static bool Covers(Version pinned, string? displayVersion) => Version.TryParse(displayVersion, out var installed) && installed >= pinned;
}
