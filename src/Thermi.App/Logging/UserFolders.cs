namespace Thermi.App.Logging;

/// <summary>
/// The user's application data folders, the environment's first, so a test or a person trying a build gives that thermi a
/// profile of its own and never touches the installed thermi's settings or logs. A Mac keeps both in the home's
/// <c>Library/Application Support</c>, where .NET would answer Linux's folders.
/// </summary>
internal static class UserFolders
{
    public static string Roaming => Of("APPDATA", Environment.SpecialFolder.ApplicationData);

    public static string Local => Of("LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Where Linux keeps what a program leaves of its own, as logs: <c>$XDG_STATE_HOME</c>, or <c>~/.local/state</c>.</summary>
    public static string State => Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } named
        ? named
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");

    private static string Of(string variable, Environment.SpecialFolder folder) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } named ? named
        : OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(folder);
}
