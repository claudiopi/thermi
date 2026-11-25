namespace Thermi.Core.Translations;

/// <summary>The catalog's titles, counts and badges, and the CPU fan finder's.</summary>
[Translated]
public enum CatalogText
{
    FanFinderWaiting,

    FanFinderLoading,

    /// <summary>Once the load has ended, while the finder still watches the fans, as some answer only after it.</summary>
    FanFinderWatching,

    /// <summary>The chip that shows every type.</summary>
    AllKinds,

    /// <summary>Beside &quot;Find my CPU fan&quot;, before it runs: what it does.</summary>
    FindCpuFanHint,

    /// <summary>While the finder runs: the fan that rose most, by how many RPM and by how much of its speed before the load, in percent.</summary>
    FanRising,

    /// <summary>The catalog's title, with the overlay the item goes to.</summary>
    AddTo,

    /// <summary>The name of the catalog's choice of overlay, beside its title.</summary>
    CatalogOverlay,

    /// <summary>The name of the catalog's type chips.</summary>
    KindField,

    /// <summary>How many sensors the catalog lists.</summary>
    SensorCount,

    OneSensor,

    /// <summary>What Narrator says as the search changes, after the count.</summary>
    CountSaid,

    /// <summary>The catalog's search that lists nothing, with what was typed.</summary>
    NoMatchFor,

    /// <summary>After a search that lists nothing: the one word to try instead.</summary>
    TryOneWord,

    /// <summary>A catalog row's reason: a CPU temperature, power or clock of exactly zero.</summary>
    RowNeedsDriver,

    /// <summary>A catalog row's reason: a fan at 0 RPM.</summary>
    RowStopped,

    /// <summary>A catalog row's note: a GPU's fan at 0 RPM, which the GPU stops while it is cool.</summary>
    RowResting,

    /// <summary>A catalog row's reason: any other sensor without a value.</summary>
    RowReadsNothing,

    /// <summary>A catalog row's note: the game's FPS or frame time, which read only while an app in front presents frames.</summary>
    RowNoFrames,

    /// <summary>A catalog row's reason: the game's FPS or frame time while the sensor service cannot read frames.</summary>
    RowFramesUnread,

    /// <summary>Under a catalog row: the items that already show it.</summary>
    ShownAs,

    /// <summary>One of those items, with more than one overlay: its name and its overlay's.</summary>
    ShownOn,

    /// <summary>The group a network adapter lists under, before its name.</summary>
    GroupNetwork,

    /// <summary>The group a drive lists under, before its name.</summary>
    GroupDisk,

    /// <summary>The group a battery lists under, before its name.</summary>
    GroupBattery,

    /// <summary>What Narrator says once an item is added, with its name and its overlay's.</summary>
    Added,

    /// <summary>With no sensor read, in place of the catalog's rows.</summary>
    CatalogEmpty,

    CatalogEmptyHint,

    /// <summary>The CPU fan's Sensor row: the button that runs the finder.</summary>
    FindIt,

    /// <summary>Asked before the finder loads the CPU, which a person may not want while they work.</summary>
    FinderAsk,

    /// <summary>The button that answers <see cref="FinderAsk"/> and starts the finder.</summary>
    LoadCpu,

    /// <summary>The finder's result: the button that makes the CPU fan item show the fan found.</summary>
    UseIt,

    /// <summary>Puts the finder's result away.</summary>
    Dismiss,

    /// <summary>What Narrator says as the finder starts.</summary>
    FinderStarted,

    /// <summary>What Narrator says when the finder is cancelled.</summary>
    FinderStopped,

    /// <summary>The finder's result when the CPU fan item already shows the fan found.</summary>
    AlreadyUsed,

    /// <summary>After the fan found: how many RPM it rose.</summary>
    FanRose,

    /// <summary>The button in the catalog's search that empties it.</summary>
    ClearSearch,

    /// <summary>Under a probable name: that it is only likely, with the chip's own name for the input.</summary>
    LikelyNote,

    /// <summary>The probable name of a fan's duty, with the fan's probable name, so it never reads as the fan's speed.</summary>
    LikelyDuty,

    /// <summary>Under an input a chip only numbers: the chip and the input's number.</summary>
    InputNote,

    /// <summary>A library's name that another type of the same device shares, with its own type in lower case.</summary>
    WithKind,

    /// <summary>A probable name's tooltip: the chip, its name for the input, and the probable name.</summary>
    LikelyTip,

    /// <summary>The tooltip of an input a chip only numbers, with the chip.</summary>
    InputTip,

    /// <summary>After the catalog's count: how many sensors it hides.</summary>
    HiddenCount,

    /// <summary>The chip that shows the sensors the catalog hides.</summary>
    ShowAll,

    /// <summary>A catalog row's pencil, which renames its sensor, with the sensor's name.</summary>
    RenameSensor,

    /// <summary>The box that renames a sensor in its row, with the sensor's name.</summary>
    SensorNameBox,
}
