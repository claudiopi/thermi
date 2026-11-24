using Thermi.Core.Translations;

namespace Thermi.Core.Placement;

/// <summary>
/// What the window says of an overlay that does not fit beside the app buttons, and its way out. An overlay keeps the
/// width it is drawn at on its taskbar's row (<see cref="TaskbarRow"/>), its labels and items whatever the room, since
/// labels follow the user's setting alone.
/// </summary>
[Translated]
public enum FitText
{
    DoesNotFit,
    HideLabels,
}
