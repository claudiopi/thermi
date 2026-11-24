namespace Thermi.Core.Translations;

/// <summary>The texts that are no reason, state or name of something else.</summary>
[Translated]
public enum UiText
{
    ClickThrough,

    LockPosition,
    Exit,

    /// <summary>"Overlay" and its number, counted from 1.</summary>
    OverlayName,

    /// <summary>An overlay's name, then where it sits.</summary>
    OverlayPlace,

    Catalog,
    CatalogSearch,
    Auto,
    AddItem,
    Combine,
    Cancel,

    /// <summary>The window's offer when no overlay runs, before the button that starts it.</summary>
    OverlayNotRunning,

    StartOverlay,
    GeneralTab,

    /// <summary>The title bar's undo button, with its keys.</summary>
    Undo,

    Redo,

    /// <summary>The name Narrator reads for the stage's items, as a list.</summary>
    StageItems,

    SensorField,

    LabelField,

    MeterField,

    MeterNone,

    /// <summary>A fan item's meter by its own speed, as its other meter is its duty.</summary>
    MeterSpeed,

    /// <summary>Under a chart whose lines, not the defaults, were reached for most of its ten minutes.</summary>
    FiresMostly,

    OwnPill,

    OwnTrayIcon,

    MoreOptions,

    WarningFrom,

    CriticalFrom,

    ResetThreshold,

    /// <summary>The item's own value color, among its options.</summary>
    ValueColor,

    /// <summary>Gives the item's value the style's color back.</summary>
    StyleColor,

    /// <summary>The value color's button while the value wears the style's color.</summary>
    StyleColorShown,

    /// <summary>The button of the Combine row, which opens the sensors to combine.</summary>
    CombineChoose,

    CatalogLive,

    Close,

    FindingCpuFan,

    /// <summary>The name Narrator reads for the map of the monitors.</summary>
    MonitorMap,

    SizeField,

    StartWithWindows,

    ResetToDefaults,

    LookField,

    LayoutField,

    UnitsField,

    DirectionField,

    LabelsField,

    ValuesField,

    /// <summary>The pills' background opacity.</summary>
    BackgroundField,

    TextColorsField,

    MeterColorsField,

    /// <summary>Gives the meter's three colors back their defaults.</summary>
    ResetColors,

    RenameStyle,

    DeleteStyle,

    ExportStyle,

    ImportStyle,

    StyleNameField,

    Save,

    SaveAsMyStyle,

    /// <summary>The kind of file the file pickers show.</summary>
    StyleFiles,

    LanguageField,

    /// <summary>This language's own name, as its speakers write it.</summary>
    LanguageName,

    /// <summary>This language's BCP 47 tag, which the page declares so Narrator reads it in its voice.</summary>
    LanguageTag,

    /// <summary>The choice that follows Windows, with the language that gives now.</summary>
    LanguageFromWindows,

    TemperatureField,

    DuplicateOverlay,

    StatField,
    SmoothField,

    /// <summary>Smoothing over a number of seconds.</summary>
    SmoothSeconds,
    DecimalsField,
    OffsetField,
    MeterGraph,
    AlertOnly,
    AlertOnlyNeedsMeter,

    /// <summary>A network or a drive speed's switch to show it in bits per second, and what it keeps.</summary>
    Bits,
    BitsHint,
    CombineField,

    CombineAdd,

    HideThisOverlay,
    ShowThisOverlay,
    DeleteThisOverlay,

    MoveToDisplay,
    MergeInto,

    Delete,

    RestoreSettings,

    /// <summary>What the page shows instead of itself when it failed to draw, with <see cref="Reload"/>.</summary>
    SomethingWentWrong,

    Reload,

    /// <summary>The Settings row of the window's list, and the heading of its page.</summary>
    SettingsPage,

    /// <summary>The sheet of the window's shortcuts, and its title.</summary>
    KeyboardShortcuts,

    /// <summary>The name of the title row's "…" button.</summary>
    MoreActions,

    /// <summary>The name of the title row's button that opens the folded list.</summary>
    ShowList,

    NotifyMe,

    /// <summary>Why Separate pill is off on a pill's first item.</summary>
    AlreadyOwnPill,

    /// <summary>The name of a color box's hex field.</summary>
    HexColor,

    /// <summary>The name of an item's chart: the item's name.</summary>
    ChartOf,

    AddWarningLine,
    AddCriticalLine,

    /// <summary>What the chart's place says while the sensor service is stopped.</summary>
    ChartSensorsStopped,

    /// <summary>What the chart's place says for a CPU sensor that needs the driver.</summary>
    ChartNeedsDriver,

    /// <summary>What the chart's place says when its sensor has not read yet.</summary>
    ChartNoReadings,

    /// <summary>The name of the chart's "…" button.</summary>
    ChartActions,

    /// <summary>The chart's axis at its start.</summary>
    ChartStart,

    /// <summary>The chart's axis at its end.</summary>
    ChartNow,

    MoveToNewOverlay,

    /// <summary>A row menu's entry that moves the item to another overlay: the overlay's name.</summary>
    MoveToOverlay,

    Rename,
    EditPillLabel,
    Remove,
    Collapse,
    Expand,
    NewOverlay,
    ExitThermi,

    /// <summary>A pill, named by its label.</summary>
    PillName,

    /// <summary>A pill's row in the list: its label and how many items it holds, more than one.</summary>
    PillItems,

    /// <summary>A pill's row in the list holding one item: its label.</summary>
    PillOneItem,

    /// <summary>An overlay's row: its name and how many items it holds, more than one.</summary>
    OverlayItems,

    /// <summary>An overlay's row: its name, holding one item.</summary>
    OverlayOneItem,

    /// <summary>A hidden overlay's mark in the list and on the map, the only places it shows while it is hidden.</summary>
    OverlayHidden,

    /// <summary>What Narrator hears once an item moved: its name, its place and count in its pill, and the pill's label.</summary>
    ItemMoved,

    /// <summary>The Sensor dropdown's filter: how many sensors it lists.</summary>
    FilterSensors,

    /// <summary>Auto, then what it follows now, a sensor or a device.</summary>
    AutoFollows,

    /// <summary>A sensor's name, then its device's.</summary>
    SensorOn,

    /// <summary>What More options holds, under its name.</summary>
    MoreOptionsHint,

    /// <summary>The shortcut sheet's line for Ctrl+F.</summary>
    ShortcutFind,

    /// <summary>The shortcut sheet's line for Ctrl+N.</summary>
    ShortcutAdd,

    /// <summary>The shortcut sheet's line for Ctrl+Z and Ctrl+Y.</summary>
    ShortcutUndo,

    /// <summary>The shortcut sheet's line for Ctrl+,.</summary>
    ShortcutSettings,

    /// <summary>The shortcut sheet's line for F1.</summary>
    ShortcutSheet,

    /// <summary>The shortcut sheet's line for F2.</summary>
    ShortcutRename,

    /// <summary>The shortcut sheet's line for Alt+Up and Alt+Down.</summary>
    ShortcutMove,

    /// <summary>The shortcut sheet's line for Delete.</summary>
    ShortcutRemove,

    /// <summary>The shortcut sheet's line for Shift+F10.</summary>
    ShortcutMenu,

    /// <summary>The app's name beside its mark in the title row.</summary>
    AppName,

    /// <summary>What Smoothing does, under its name.</summary>
    SmoothHint,

    /// <summary>What Only on alert does, under its name.</summary>
    AlertOnlyHint,
}
