namespace Thermi.Core.Translations;

/// <summary>What the overlay itself says: its one pill while the sensors are stopped, its values' tooltips, and its desktop gestures' undo.</summary>
[Translated]
public enum OverlayText
{
    /// <summary>The one pill an overlay shows while the sensor service is stopped.</summary>
    SensorsStopped,

    /// <summary>A value's tooltip's first line: the item's name, then its value.</summary>
    NameAndValue,

    /// <summary>The RAM in use in its tooltip: how much, then the RAM there is, with its unit.</summary>
    OfTotal,

    /// <summary>A value's tooltip: the item's minimum, then its maximum.</summary>
    MinAndMax,

    DoubleClickToEdit,

    /// <summary>A missing value's tooltip when a CPU temperature reads zero, with the item's name.</summary>
    NeedsDriver,

    /// <summary>A value's tooltip while the sensor service is stopped or its readings are stale.</summary>
    SensorsStoppedHint,

    /// <summary>A missing value's tooltip, with the item's name.</summary>
    ReadsNothing,

    /// <summary>A value's tooltip until the window has opened once.</summary>
    SetUp,

    /// <summary>The CPU fan's tooltip while thermi picked it by its input's name: only likely, and how to check.</summary>
    FanUnconfirmed,

    /// <summary>The overlay's menu entry that takes back a pill or an overlay moved on the desktop.</summary>
    UndoMove,

    /// <summary>The overlay's menu entry that takes back an overlay resized from its corner.</summary>
    UndoResize,

    /// <summary>The overlay's menu entry that takes back a pill dragged off to an overlay of its own.</summary>
    UndoDragOff,

    /// <summary>The overlay's menu entry that takes back an overlay dropped into another.</summary>
    UndoMerge,

    /// <summary>The overlay's menu entry that takes back each edit chosen from a menu, by what the edit was.</summary>
    UndoSeparate,
    UndoGraph,
    UndoOrientation,
    UndoLock,
    UndoClickThrough,
    UndoLook,
    UndoHide,
    UndoShow,

    /// <summary>The label of the overlay's entry that says the settings file is broken.</summary>
    ConfigLabel,

    /// <summary>The value of that entry.</summary>
    ConfigError,

    /// <summary>In place of a CPU value that reads nothing while thermi's driver may be missing.</summary>
    Driver,

    /// <summary>A value's tooltip while the CPU throttles: its clock, its base clock and its temperature.</summary>
    Throttling,

    /// <summary>A critical CPU value's tooltip: the busiest process, then its share of the CPU.</summary>
    BusiestCpu,

    /// <summary>A critical GPU value's tooltip: the busiest process, then its share of the GPU's busiest engine.</summary>
    BusiestGpu,

    /// <summary>What the overlay and the window say small after the value of an item that shows its minimum.</summary>
    MinTag,

    /// <summary>As <see cref="MinTag"/>, for its maximum.</summary>
    MaxTag,

    /// <summary>As <see cref="MinTag"/>, for its average since the overlay started.</summary>
    AverageTag,

    /// <summary>As <see cref="MinTag"/>, for its value now smoothed over its last seconds, with how many.</summary>
    SmoothTag,
}
