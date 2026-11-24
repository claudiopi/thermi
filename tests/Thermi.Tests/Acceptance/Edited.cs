using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>Settings as the window edits them, and as a person edits the file meanwhile.</summary>
internal static class Edited
{
    /// <summary>The settings an edit gave, which must not have been refused.</summary>
    public static ThermiConfig Done(EditResult result)
    {
        Assert.Null(result.Refused);
        return result.Config;
    }

    /// <summary>An edit the window makes and keeps for undo.</summary>
    public static ThermiConfig Done(UndoHistory history, ThermiConfig config, Func<ThermiConfig, EditResult> edit)
    {
        var edited = Done(edit(config));
        history.Record(config, edited);
        return edited;
    }

    /// <summary>A change made in the file, read back as the window reads it.</summary>
    public static ThermiConfig ByHand(ThermiConfig config, Func<ThermiConfig, ThermiConfig> change)
    {
        var (read, problem) = ThermiConfigJson.Read(ThermiConfigJson.Write(change(config)));
        Assert.Null(problem);
        return read;
    }

    public static ThermiConfig WithItem(ThermiConfig config, string id, Func<ItemConfig, ItemConfig> change) => config with
    {
        Overlays = [.. config.Overlays.Select(overlay => overlay with { Items = [.. overlay.Items.Select(item => item.Id == id ? change(item) : item)] })],
    };

    public static ThermiConfig WithOverlay(ThermiConfig config, int index, Func<OverlayConfig, OverlayConfig> change) => config with
    {
        Overlays = [.. config.Overlays.Select((overlay, at) => at == index ? change(overlay) : overlay)],
    };

    public static ItemConfig Item(ThermiConfig config, string id) => config.Overlays.SelectMany(overlay => overlay.Items).Single(item => item.Id == id);

    public static List<string> Ids(OverlayConfig overlay) => [.. overlay.Items.Select(item => item.Id)];

    /// <summary>The settings as the file holds them, to compare by value.</summary>
    public static string File(ThermiConfig config) => ThermiConfigJson.Write(config);
}
