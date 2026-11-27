namespace Thermi.Core.Translations;

/// <summary>Where an overlay sits, as the placement tab and thermi's menu say it: its spot, its monitor, and what a change of monitors keeps.</summary>
[Translated]
public enum PlaceText
{
    /// <summary>A spot, then the number of its monitor.</summary>
    OnMonitor,

    FreePosition,

    /// <summary>GNOME's words for the taskbar's spot, "on the taskbar".</summary>
    OnTopBar,

    /// <summary>A desktop with panels' words for the taskbar's spot.</summary>
    OnPanel,

    /// <summary>A desktop with no bar's words for the taskbar's spot.</summary>
    AtBottomEdge,
}
