namespace Thermi.Core.Translations;

/// <summary>The settings window's overlay page: its head, Appearance and Placement, and what Narrator hears as overlays change.</summary>
[Translated]
public enum OverlayPageText
{
    FontField,
    GapField,
    EffectField,
    ColorblindSafe,
    OledCare,

    /// <summary>The overlay page's three headings.</summary>
    AppearanceSection,
    PlacementSection,
    TextSection,

    /// <summary>Under an overlay page's title: how many items, then where it sits.</summary>
    OverlayHeadItems,
    OverlayHeadOneItem,

    /// <summary>What Narrator hears of the preview at the top of Appearance, from the overlay's name.</summary>
    Preview,

    /// <summary>A Look tile's name, from the look's.</summary>
    LookTile,

    /// <summary>The look an overlay has when it wears none of the saved ones.</summary>
    DefaultLook,

    SaveAsMyLook,

    /// <summary>The name of the Look card's "…" button.</summary>
    LookFileActions,

    /// <summary>The name of a saved look's "…" button.</summary>
    LookActions,

    UseOnAllOverlays,

    /// <summary>What "Save as my look" keeps, in its dialog.</summary>
    LookKeeps,

    /// <summary>The name a new saved look starts with.</summary>
    MyLook,

    RenameLook,
    ThemeField,

    /// <summary>Under Background opacity, below 30 %.</summary>
    ShadowBelow,

    TextAndShape,
    TextAndShapeHint,
    LabelWeight,
    ValueWeight,
    CornersField,

    /// <summary>What Show units does, under its name.</summary>
    UnitsHint,

    /// <summary>The switch for an overlay's labels, and what it does under its name.</summary>
    ShowLabels,
    ShowLabelsHint,

    ColorsField,
    ColorblindHint,

    /// <summary>Under Text colors: Auto, or colors of one's own.</summary>
    TextColorsAuto,
    TextColorsOwn,

    /// <summary>The hint in a color's hex box, and what it says of a value it cannot read.</summary>
    HexHint,
    HexFormat,

    /// <summary>Under the meter's and the text's swatches, a word each, so a swatch says what it colors.</summary>
    SwatchNormal,
    SwatchWarning,
    SwatchCritical,
    SwatchLabels,
    SwatchValues,

    ColorGreen,
    ColorSky,
    ColorBlue,
    ColorYellow,
    ColorAmber,
    ColorOrange,
    ColorRed,
    ColorRose,
    ColorVermilion,
    ColorWhite,
    ColorGray,
    ColorBlack,

    /// <summary>Under Position: a vertical overlay snapped to the taskbar, a free one, and one on the taskbar.</summary>
    AboveTaskbar,
    FreeHint,
    TaskbarHint,

    /// <summary>A display on the map and in "Move to display", counted from the main one.</summary>
    DisplayName,
    MainDisplay,

    /// <summary>A display's pixels, and its scale when it is not 100 %.</summary>
    DisplaySize,
    DisplayScaled,

    /// <summary>A spot of the map: the spot, then its display's number.</summary>
    SpotOnMonitor,

    /// <summary>Why a top spot sits lower than the corner.</summary>
    TopSpotHint,

    WhenToShowField,
    LockGroup,

    /// <summary>The lock group's state while nothing in it is on, and while the lock is.</summary>
    AllOff,
    Locked,

    ClickThroughHint,
    OledHint,

    /// <summary>Two states side by side, as "Locked · OLED care".</summary>
    SummaryJoin,

    /// <summary>What Narrator hears once an overlay is made, deleted, merged, hidden or shown.</summary>
    OverlayAdded,
    OverlayDeleted,
    OverlayMerged,
    OverlayHiddenSaid,
    OverlayShownSaid,

    /// <summary>The Position row's note when the overlay's taskbar spot floats, by why.</summary>
    TaskbarOnSide,
    TaskbarHides,

    /// <summary>The switch that fades the overlay near the pointer, and what it does.</summary>
    Fade,
    FadeHint,

    /// <summary>Why Direction is grayed: one pill has nothing to stack.</summary>
    DirectionOnePill,

    /// <summary>What Narrator hears of a number typed in a slider's box past its range, from the slider's name, its ends and what it holds.</summary>
    TypedOutOfRange,

    /// <summary>What Narrator hears of text typed in a slider's box that is no number, from the slider's name and what it holds.</summary>
    TypedNotANumber,
}
