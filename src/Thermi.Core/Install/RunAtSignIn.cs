namespace Thermi.Core.Install;

/// <summary>
/// The user's Run value that starts the overlay at sign-in, as the installers, the window's "Start with Windows" and the
/// move to a machine install all write it: one key, one name, one quoting, so each reads what the others wrote.
/// </summary>
public static class RunAtSignIn
{
    public const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string Name = "thermi";

    /// <summary>The value that starts <paramref name="launcher"/>, quoted for a path with spaces.</summary>
    public static string Of(string launcher) => $"\"{launcher}\"";
}
