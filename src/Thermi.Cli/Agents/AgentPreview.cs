using System.Globalization;
using Thermi.Cli.Commands;
using Thermi.Core.Agents;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Cli.Agents;

/// <summary>
/// <c>preview</c>: an overlay painted by the host as it draws the readings of now, decided here: which overlay of which
/// settings, for which snapshot, at what scale.
/// </summary>
internal sealed partial class AgentCommands
{
    /// <summary>The picture as the command answers it, and its PNG, which the MCP tool gives as an image; no PNG when it did not paint.</summary>
    public async Task<(AgentReply Reply, byte[]? Png)> PictureAsync(AgentRequest request, CancellationToken stop)
    {
        // Settings that cannot be read paint no overlay of the person's; they and the overlay are checked before the service is asked.
        var settings = await SettingsAsync();
        var overlay = request.Overlay ?? 0;
        if (settings.Problem is { } problem)
        {
            return (Broken(problem), null);
        }

        if (overlay < 0 || overlay >= settings.Config.Overlays.Count)
        {
            var last = settings.Config.Overlays.Count - 1;
            var said = string.Create(CultureInfo.InvariantCulture, $"The settings have overlays 0 to {last}; config lists them.");
            return (new AgentReply(AgentExit.BadArguments, Error: said), null);
        }

        var feed = environment.Sensors();
        var (samples, state, taken) = await AgentSamples.TakeAsync(feed, 1, time, stop);
        return samples is [var snapshot] ? Painted(settings.Config, overlay, snapshot, request.Scale) : (Unread(state, taken, null), null);
    }

    private async Task<AgentReply> PreviewAsync(AgentRequest request, CancellationToken stop) => (await PictureAsync(request, stop)).Reply;

    // A picture the OS could not paint, as one too big for the memory it has, is said, never a crash.
    private (AgentReply, byte[]?) Painted(ThermiConfig settings, int overlay, Snapshot snapshot, float asked)
    {
        try
        {
            var scale = Fitted(settings, overlay, snapshot, asked);
            var painted = environment.Paint(settings, overlay, snapshot, scale);
            var answer = new PreviewAnswer(overlay, scale, painted.Width, painted.Height, Convert.ToBase64String(painted.Png));
            return (new AgentReply(AgentExit.Done, AgentJson.Write(answer)), painted.Png);
        }
        catch (InvalidOperationException ex)
        {
            return (new AgentReply(AgentExit.Refused, Error: $"The overlay could not be painted: {ex.Message}"), null);
        }
    }

    // Smaller where the scale asked is past the longest side, as measured before a pixel is drawn; rounding up may leave a
    // pixel over, which another round takes off.
    private float Fitted(ThermiConfig settings, int overlay, Snapshot snapshot, float asked)
    {
        var scale = asked;
        var longest = Longest(settings, overlay, snapshot, scale);
        while (longest > PreviewAnswer.Longest)
        {
            scale *= (float)PreviewAnswer.Longest / longest;
            longest = Longest(settings, overlay, snapshot, scale);
        }

        return scale;
    }

    private int Longest(ThermiConfig settings, int overlay, Snapshot snapshot, float scale)
    {
        var (width, height) = environment.Measure(settings, overlay, snapshot, scale);
        return Math.Max(width, height);
    }
}
