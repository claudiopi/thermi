namespace Thermi.Linux.Desktop;

/// <summary>
/// GDK's token for a program thermi starts on a click, on layer shell and X11: on Wayland an xdg-activation token, which
/// the compositor grants for the input the overlay last took, and on X11 a startup id that carries the click's time. The
/// window that program shows with it comes in front, as one the desktop's own launcher started. Sway, by its default
/// <c>focus_on_window_activation urgent</c>, only marks that window urgent.
/// </summary>
internal static class LaunchTokens
{
    // Where a program reads the token it was started with, as a launcher sets them: Wayland's and X11's. GIO, GTK and Qt
    // read both.
    private static readonly string[] Variables = ["XDG_ACTIVATION_TOKEN", "DESKTOP_STARTUP_ID"];

    /// <summary>The token this process was started with, by a launcher or by a desktop's click; null for none.</summary>
    public static string? Given() =>
        Variables.Select(Environment.GetEnvironmentVariable).FirstOrDefault(token => !string.IsNullOrEmpty(token));

    /// <summary>
    /// Starts a program with <paramref name="token"/>, or with none for an empty one, never with the token this process
    /// was started with, long spent.
    /// </summary>
    public static void Set(IDictionary<string, string?> environment, string token)
    {
        foreach (var name in Variables)
        {
            if (token.Length > 0)
            {
                environment[name] = token;
            }
            else
            {
                environment.Remove(name);
            }
        }
    }

    /// <summary>The token for a click at <paramref name="time"/>, the event's, or now for 0; empty where GDK gives none.</summary>
    public static Task<string> Of(uint time)
    {
        using var context = Gdk.Display.GetDefault()?.GetAppLaunchContext();
        if (context is null)
        {
            return Task.FromResult("");
        }

        context.SetTimestamp(time);
        using var thermi = Gio.AppInfoHelper.CreateFromCommandline("thermi", "thermi", Gio.AppInfoCreateFlags.None);
        return Task.FromResult(context.GetStartupNotifyId(thermi, null) ?? "");
    }
}
