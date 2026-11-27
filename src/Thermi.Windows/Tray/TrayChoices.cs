using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Windows.Commands;
using Thermi.Windows.Logging;

namespace Thermi.Windows.Tray;

/// <summary>What a row chosen in thermi's menu or an overlay's reaches on Windows, past what <see cref="MenuChoices"/> does on every OS.</summary>
internal sealed class TrayChoices(TrayParts parts, ILogger log) : IMenuHost
{
    public Settings Settings => parts.Settings;

    public GestureUndo? Gestures => parts.Actions.Gestures;

    public ItemHistory History => parts.Actions.History;

    public int Overlays => parts.Overlays.Count;

    public void Choose(MenuRow row, int? menuOverlay) => MenuChoices.Choose(this, row, menuOverlay);

    public IReadOnlyList<StyleConfig> Looks() => MenuChoices.Looks(Settings);

    public EditResult Settle(ThermiConfig before, ThermiConfig after) => parts.Actions.Settle(before, after);

    public void OpenSettings(int overlay, string? item) => parts.Actions.OpenSettings(overlay, item);

    public void OpenSize(int overlay) => parts.Actions.OpenSize(overlay);

    public void UndoGesture(int overlay) => parts.Overlays[overlay].UndoGesture();

    public void Separate(int overlay, string item) => parts.Overlays[overlay].Separate(item);

    public void HideOverlays(bool hidden) => parts.Visibility.HideOverlays = hidden;

    public void Exit() => parts.Actions.Exit();

    public void RequestPaint() => parts.Actions.RequestPaint();

    public void Reach(MenuEntry entry, int overlay)
    {
        if (entry == MenuEntry.StartSensors)
        {
            _ = Task.Run(StartSensorsAsync, CancellationToken.None);
        }
        else if (entry == MenuEntry.TaskManager)
        {
            parts.Environment.TaskManager();
        }
        else if (overlay < parts.Overlays.Count && parts.Overlays[overlay].Picture() is { } picture)
        {
            using (picture)
            {
                parts.Environment.CopyImage(parts.Overlays[overlay].Hwnd, picture);
            }
        }
    }

    // On a thread of its own: starting a process behind UAC blocks its caller until the prompt closes, and the host's thread
    // must keep painting. The pipe coming back is what shows the service ran.
    private async Task StartSensorsAsync()
    {
        try
        {
            if (await parts.Environment.StartSensors().ConfigureAwait(false) is { } code and not StartSensors.Started)
            {
                log.StartSensorsEnded(code);
            }
        }
#pragma warning disable CA1031 // A start that fails leaves the sensors stopped, which the menu still offers to fix.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.StartSensorsFailed(ex);
        }
    }
}
