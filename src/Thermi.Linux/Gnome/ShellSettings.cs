namespace Thermi.Linux.Gnome;

/// <summary>Where thermi's extension stands in the person's GNOME Shell settings once thermi asked to turn it on.</summary>
internal enum ShellSwitch
{
    /// <summary>The person turned it off, or every extension of theirs: it stays off.</summary>
    Off,

    /// <summary>It was on already.</summary>
    On,

    /// <summary>thermi turned it on.</summary>
    TurnedOn,
}

/// <summary>
/// Whether thermi's extension is on in the person's GNOME Shell settings, which the Shell reads at each sign-in and follows
/// as they change. A package only puts the extension's files in place, which GNOME leaves off: thermi turns it on, unless
/// the person turned it off, or every extension of theirs.
/// </summary>
internal static class ShellSettings
{
    private const string Schema = "org.gnome.shell";
    private const string Enabled = "enabled-extensions";

    /// <summary>
    /// Adds the extension to those on, where the person did not turn it off: a Shell that read it turns it on at once, and
    /// one that did not yet at the next sign-in.
    /// </summary>
    public static ShellSwitch TurnOn()
    {
        // GLib ends the process on a schema it does not have, so it is looked up first.
        if (Gio.SettingsSchemaSource.GetDefault()?.Lookup(Schema, true) is null)
        {
            return ShellSwitch.Off;
        }

        using var settings = Gio.Settings.New(Schema);
        var enabled = settings.GetStrv(Enabled);
        if (settings.GetBoolean("disable-user-extensions") || settings.GetStrv("disabled-extensions").Contains(ShellLink.Uuid))
        {
            return ShellSwitch.Off;
        }

        if (enabled.Contains(ShellLink.Uuid))
        {
            return ShellSwitch.On;
        }

        settings.SetStrv(Enabled, [.. enabled, ShellLink.Uuid]);
        Gio.Settings.Sync();
        return ShellSwitch.TurnedOn;
    }
}
