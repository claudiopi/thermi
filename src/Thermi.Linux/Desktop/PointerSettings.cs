namespace Thermi.Linux.Desktop;

/// <summary>
/// The person's drag threshold and double-click time, as GNOME keeps them for every program, read as each press asks;
/// GNOME's defaults where its schema is not installed, as on another desktop.
/// </summary>
internal sealed class PointerSettings
{
    private const string Schema = "org.gnome.desktop.peripherals.mouse";

    // GLib ends the process on a schema it does not have, so it is looked up first.
    private readonly Gio.Settings? _mouse = Gio.SettingsSchemaSource.GetDefault()?.Lookup(Schema, true) is null ? null : Gio.Settings.New(Schema);

    /// <summary>How far a press moves, in logical pixels, before it is a drag.</summary>
    public int DragThreshold => _mouse?.GetInt("drag-threshold") is > 0 and var pixels ? pixels : 8;

    /// <summary>How soon a second click, in milliseconds, makes a double-click.</summary>
    public uint DoubleClick => _mouse?.GetInt("double-click") is > 0 and var time ? (uint)time : 400;
}
