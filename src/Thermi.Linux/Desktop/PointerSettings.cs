namespace Thermi.Linux.Desktop;

/// <summary>
/// The person's drag threshold, double-click time and whether things animate, as GNOME keeps them for every program, read
/// as each press or paint asks; GNOME's defaults where its schema is not installed, as on another desktop.
/// </summary>
internal sealed class PointerSettings
{
    private const string Schema = "org.gnome.desktop.peripherals.mouse";
    private const string Interface = "org.gnome.desktop.interface";

    // GLib ends the process on a schema it does not have, so it is looked up first.
    private readonly Gio.Settings? _mouse = Of(Schema);
    private readonly Gio.Settings? _interface = Of(Interface);

    /// <summary>How far a press moves, in logical pixels, before it is a drag.</summary>
    public int DragThreshold => _mouse?.GetInt("drag-threshold") is > 0 and var pixels ? pixels : 8;

    /// <summary>How soon a second click, in milliseconds, makes a double-click.</summary>
    public uint DoubleClick => _mouse?.GetInt("double-click") is > 0 and var time ? (uint)time : 400;

    /// <summary>Whether the desktop animates, which a critical value's glow keeps to, as Windows' own setting.</summary>
    public bool Animations => _interface?.GetBoolean("enable-animations") ?? true;

    private static Gio.Settings? Of(string schema) =>
        Gio.SettingsSchemaSource.GetDefault()?.Lookup(schema, true) is null ? null : Gio.Settings.New(schema);
}
