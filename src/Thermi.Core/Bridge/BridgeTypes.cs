using System.Text.Json;
using System.Text.Json.Serialization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Core.Bridge;

/// <summary>
/// An edit as the page asks for it, by what it names: thermi's, an overlay's, its look's or an item's field, or a change
/// of the overlays and their items. Core's rules decide it; a null value takes a field out.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ThermiFieldEdit), "thermi")]
[JsonDerivedType(typeof(OverlayFieldEdit), "overlay")]
[JsonDerivedType(typeof(StyleFieldEdit), "style")]
[JsonDerivedType(typeof(ItemFieldEdit), "item")]
[JsonDerivedType(typeof(LookEdit), "look")]
[JsonDerivedType(typeof(AddItemEdit), "addItem")]
[JsonDerivedType(typeof(RemoveItemEdit), "removeItem")]
[JsonDerivedType(typeof(MoveItemEdit), "moveItem")]
[JsonDerivedType(typeof(NewOverlayEdit), "newOverlay")]
[JsonDerivedType(typeof(DuplicateOverlayEdit), "duplicateOverlay")]
[JsonDerivedType(typeof(MergeOverlayEdit), "mergeOverlay")]
[JsonDerivedType(typeof(DeleteOverlayEdit), "deleteOverlay")]
[JsonDerivedType(typeof(NudgeOverlayEdit), "nudgeOverlay")]
[JsonDerivedType(typeof(ResetEdit), "reset")]
[JsonDerivedType(typeof(LineEdit), "line")]
[JsonDerivedType(typeof(TypedLineEdit), "typedLine")]
[JsonDerivedType(typeof(AddLineEdit), "addLine")]
[JsonDerivedType(typeof(ResetLinesEdit), "resetLines")]
[JsonDerivedType(typeof(SensorNameEdit), "sensorName")]
public abstract record PageEdit;

public sealed record ThermiFieldEdit(string Field, JsonElement Value) : PageEdit;

public sealed record OverlayFieldEdit(int Overlay, string Field, JsonElement Value) : PageEdit;

public sealed record StyleFieldEdit(int Overlay, string Field, JsonElement Value) : PageEdit;

public sealed record ItemFieldEdit(string Item, string Field, JsonElement Value) : PageEdit;

/// <summary>A saved look worn by one overlay, or by every overlay when none is named.</summary>
public sealed record LookEdit([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? Overlay, string Name) : PageEdit;

public sealed record AddItemEdit(int Overlay, string Sensor) : PageEdit;

public sealed record RemoveItemEdit(string Item) : PageEdit;

/// <summary>An item moved to a place in an overlay, or to a new overlay of its own when none is named.</summary>
public sealed record MoveItemEdit(string Item, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? Overlay, int Index) : PageEdit;

public sealed record NewOverlayEdit : PageEdit;

public sealed record DuplicateOverlayEdit(int Overlay) : PageEdit;

public sealed record MergeOverlayEdit(int Overlay, int Into) : PageEdit;

public sealed record DeleteOverlayEdit(int Overlay) : PageEdit;

/// <summary>A floating overlay moved by Alt+arrows on its row, by the pixels given.</summary>
public sealed record NudgeOverlayEdit(int Overlay, int Dx, int Dy) : PageEdit;

public sealed record ResetEdit : PageEdit;

/// <summary>A meter's line moved to a value, or taken out with none.</summary>
public sealed record LineEdit(string Item, ChartLine Line, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Value) : PageEdit;

/// <summary>A meter's line as typed in its box, which Core reads in the window's number format.</summary>
public sealed record TypedLineEdit(string Item, ChartLine Line, string Typed) : PageEdit;

public sealed record AddLineEdit(string Item, ChartLine Line) : PageEdit;

/// <summary>A meter's lines back to their defaults.</summary>
public sealed record ResetLinesEdit(string Item) : PageEdit;

/// <summary>A sensor named by the user, by the id an item names it by; an empty name gives back the library's.</summary>
public sealed record SensorNameEdit(string Sensor, string Name) : PageEdit;

/// <summary>
/// What the service reads now, and whether it runs: no sensors while it is stopped, and whether another program holds
/// its pipe then.
/// </summary>
public sealed record SensorView(ServiceState Service, IReadOnlyList<RawSensor> Sensors, bool Taken = false)
{
    /// <summary>
    /// The snapshot's sensors with the names the catalog shows, the user's <paramref name="own"/> or a probable one, so a
    /// sensor has one name everywhere; a value that is no number as null, since the page's JSON has no NaN. A constant, as
    /// a memory module's limits, is no sensor to pick, so it is left out.
    /// </summary>
    public static SensorView Of(ServiceState service, Snapshot? snapshot, IReadOnlyDictionary<string, string>? own, Texts texts, bool taken = false) =>
        new(
            service,
            service == ServiceState.Stopped || snapshot is null
                ? []
                : [.. SensorNames.Named(snapshot, own, texts).Where(sensor => !SensorCatalog.Constant(sensor))
                    .Select(sensor => sensor.Value is { } value && !double.IsFinite(value) ? sensor with { Value = null } : sensor)],
            taken);
}

public sealed record Rect(int X, int Y, int Width, int Height);

/// <summary>
/// A monitor by its EDID id, its whole area and the part windows take, whether a taskbar sits on it,
/// and why its taskbar spots float, if they do.
/// </summary>
public sealed record MonitorView(string Id, bool Primary, Rect Bounds, Rect WorkArea, double Scale, bool Taskbar, TaskbarFloat? Floats = null);

public enum FinderPhase
{
    Idle,
    Running,
    Done,
    Cancelled,
}

/// <summary>
/// Where "Find it" stands: the fan it picked once done; while it runs, how far, from 0 to 1; what it says, worded, as
/// "Waiting for fresh readings…" or the fan rising, and once done the fan it found or why none; what that adds, as how
/// much the fan rose; and while idle, why it cannot start, or null when it can.
/// </summary>
public sealed record FinderView(
    FinderPhase Phase,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Pick,
    double? Progress = null,
    string? Text = null,
    string? Detail = null,
    FanFinderRefusal? Refusal = null);

/// <summary>A value of an overlay, counted from 0, or the overlay's edge when no item is named, reached on the desktop.</summary>
public sealed class DesktopPickEventArgs(int overlay, string? item, SelectHow how) : EventArgs
{
    public int Overlay { get; } = overlay;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Item { get; } = item;

    public SelectHow How { get; } = how;
}

/// <summary>
/// What a change may have touched: the sensors, which the page reads again only then, when they came or went or the
/// service started or stopped, or when it sees their names or the language change in the settings.
/// </summary>
public sealed class WindowChangeEventArgs(bool sensors) : EventArgs
{
    public bool Sensors { get; } = sensors;
}

/// <summary>
/// A second's new readings with what they change of the view and the status, sent with the event so the page need not
/// ask for them: each call across WebView2 costs more than the reading.
/// </summary>
public sealed class WindowReadingsEventArgs(WindowView view, WindowStatus status) : EventArgs
{
    public WindowView View { get; } = view;

    public WindowStatus Status { get; } = status;
}

/// <summary>The settings as the page reads them.</summary>
public static class PageSettings
{
    /// <summary>
    /// The settings, every overlay without a name the user typed named from where it sits, and every item without one
    /// named as a new item of its sensor, which <paramref name="known"/> names.
    /// </summary>
    public static ThermiConfig Of(ThermiConfig config, Texts texts, Snapshot? known) =>
        config.Overlays.All(overlay => overlay.Name is not null && overlay.Items.All(item => item.Name is not null)) ? config
        : config with
        {
            Overlays = [.. config.Overlays.Select((overlay, at) => overlay with
            {
                Name = Names.OfOverlays(config, texts)[at],
                Items = [.. overlay.Items.Select(item => item.Name is null ? item with { Name = Names.OfUnnamed(item, known, texts) } : item)],
            })],
        };
}
