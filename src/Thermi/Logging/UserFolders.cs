namespace Thermi.Logging;

/// <summary>
/// The user's application data folders as the process's environment names them, else as Windows knows them, so a test or
/// a person trying a build gives that thermi a profile of its own and never touches the installed thermi's settings or logs.
/// </summary>
internal static class UserFolders
{
    public static string Roaming => Of("APPDATA", Environment.SpecialFolder.ApplicationData);

    public static string Local => Of("LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData);

    private static string Of(string variable, Environment.SpecialFolder folder) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } named ? named : Environment.GetFolderPath(folder);
}
