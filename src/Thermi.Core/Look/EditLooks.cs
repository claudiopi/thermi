using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>How an overlay's frame or whole fades while the window edits another one.</summary>
public enum EditFade
{
    None,

    /// <summary>Only the pills fade, so the values and their levels read as before.</summary>
    Frame,

    /// <summary>The whole overlay fades, as one whose pills are the opposite of the taskbar's would stand out otherwise.</summary>
    Whole,
}

/// <summary>
/// The look an overlay shows at on every OS while the settings window is open or a drag holds it: the overlays the window
/// does not edit fade, and the one a drag holds shows whole over whatever it passes.
/// </summary>
public static class EditLooks
{
    private const double FrameFade = 0.3;
    private const double WholeFade = 0.5;

    /// <summary>
    /// How the window fades <paramref name="overlay"/> while it edits <paramref name="editing"/>, none while it is closed
    /// or edits none: the one it edits not at all, every other its frame, or whole when its pills are the opposite of the
    /// taskbar's.
    /// </summary>
    public static EditFade FadeOf(int overlay, int? editing, OverlayConfig config, bool lightTaskbar) =>
        editing is null || overlay == editing ? EditFade.None
        : config.Style.Pills == (lightTaskbar ? PillTheme.Dark : PillTheme.Light) ? EditFade.Whole
        : EditFade.Frame;

    /// <summary>
    /// The look with its fade, or, <paramref name="held"/> in a drag over other overlays and the window's fades, with a
    /// background of its own whatever the look's opacity. A faded frame outlines the text in the pill's color, so the
    /// values keep their contrast over any backdrop the pill no longer hides.
    /// </summary>
    public static OverlayLook Applied(OverlayLook look, EditFade fade, bool held = false) => held ? look with { Background = 1, Opacity = 1 } : fade switch
    {
        EditFade.Frame => look with { Background = look.Background * FrameFade, Effect = TextEffect.Outline },
        EditFade.Whole => look with { Opacity = look.Opacity * WholeFade },
        EditFade.None => look,
        _ => look,
    };
}
