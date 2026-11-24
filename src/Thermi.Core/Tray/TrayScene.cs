using System.Security.Cryptography;
using System.Text;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>A taskbar's colors: its background and text, its gray, and the level colors that read on it.</summary>
public sealed record Taskbar(Rgb Background, Rgb Text, Rgb Gray, Rgb Normal, Rgb Warning, Rgb Critical)
{
    /// <summary>Windows' dark taskbar, which an accent-colored one counts as; its level colors are the overlay's.</summary>
    public static Taskbar Dark { get; } = new(
        Rgb.Parse("#1C1C1C"),
        Rgb.Parse("#FFFFFF"),
        Rgb.Parse("#8A8A8A"),
        OverlayLook.DefaultNormal,
        OverlayLook.DefaultWarning,
        OverlayLook.DefaultCritical);

    /// <summary>Windows' light taskbar, with darker level colors that read on it.</summary>
    public static Taskbar Light { get; } = new(
        Rgb.Parse("#EEEEEE"),
        Rgb.Parse("#1A1A1A"),
        Rgb.Parse("#6E6E6E"),
        Rgb.Parse("#1E7F3E"),
        Rgb.Parse("#8A5A00"),
        Rgb.Parse("#B42318"));

    public static Taskbar Of(bool light) => light ? Light : Dark;

    /// <summary>A level's chosen color, or this taskbar's own for that level when the chosen one reads under 3:1 on it.</summary>
    public Rgb Readable(Rgb chosen, Level level) =>
        chosen.ContrastWith(Background) >= OverlayLook.MinContrast ? chosen
        : level == Level.Critical ? Critical
        : level == Level.Warning ? Warning
        : Normal;
}

/// <summary>
/// What one tray icon shows. An item's icon names its <see cref="ItemId"/> and shows <see cref="Text"/> in
/// <see cref="Color"/>; thermi's own has neither, and shows thermi's mark in its <see cref="Mark"/> state.
/// </summary>
public sealed record TrayIcon(Guid Id, string? ItemId, string? Text, Rgb Color, string Tooltip)
{
    /// <summary>thermi's mark's state on its own icon; null on an item's.</summary>
    public MarkState? Mark { get; init; }
}

/// <summary>
/// What the tray icons are made from besides the settings: the latest snapshot and whether it can be shown, the regional
/// format, the language, the taskbar's mode, and the path of the running thermi, which each icon's id is tied to.
/// </summary>
public sealed record TrayInput(Snapshot? Snapshot, ServiceState Service, IFormatProvider Numbers, Texts Texts, bool LightTaskbar, string ExePath)
{
    /// <summary>The overlays' items' history, so an icon shows what its item shows on the overlay; none shows the values now.</summary>
    public ItemHistory? History { get; init; }

    /// <summary>The "Hide overlays" flag, which lives in the overlay's memory, so thermi's tooltip can say the overlays are hidden.</summary>
    public bool HideOverlays { get; init; }

    /// <summary>Whether Windows' high contrast is on, under which thermi's mark is the one-color glyph in the system's text color.</summary>
    public bool HighContrast { get; init; }

    /// <summary>Whether the window is open, while which an overlay shows its alert-only items too, so each can be selected.</summary>
    public bool WindowOpen { get; init; }

    /// <summary>The CPU's base clock in MHz, as Windows gives it, below which a hot CPU throttles; null when unknown.</summary>
    public double? BaseClock { get; init; }

    /// <summary>What is busiest on the CPU and on the GPU now, which a critical value's tooltip names; null when not read.</summary>
    public Busiest? Busiest { get; init; }
}

/// <summary>A process by its name, and its share, 0 to 100, of a CPU's logical processors or of a GPU's busiest engine.</summary>
public sealed record BusyProcess(string Name, double Share);

/// <summary>The busiest process on the CPU and on the GPU, each null when none is busy or it was not read.</summary>
public sealed record Busiest(BusyProcess? Cpu, BusyProcess? Gpu);

/// <summary>
/// Which tray icons show and what each shows: thermi's own icon first, always, then one per item that asks for one, in
/// the overlays' order. The overlay draws them and redraws one only when <see cref="TrayRefresh"/> says it changed.
/// </summary>
public static class TrayScene
{
    private const string ThermiName = "thermi";

    public static IReadOnlyList<TrayIcon> Of(ThermiConfig config, TrayInput input)
    {
        var snapshot = ThermiMark.Live(input);
        var readings = config.Overlays.Select(overlay => ThermiMark.Readable(overlay, input)).ToList();
        var scene = new Scene(
            config,
            input with { Service = snapshot is null ? StateWithout(input.Service) : ServiceState.Running },
            Taskbar.Of(input.LightTaskbar),
            readings);
        var icons = new List<TrayIcon> { ThermiIcon(scene) };
        for (var overlay = 0; overlay < config.Overlays.Count; overlay++)
        {
            var items = config.Overlays[overlay].Items;
            var labels = OverlayLayout.RunLabels(items, input.Texts);
            for (var at = 0; at < items.Count; at++)
            {
                if (items[at].Tray)
                {
                    icons.Add(ItemIcon(scene, overlay, items[at], labels[at]));
                }
            }
        }

        return icons;
    }

    /// <summary>
    /// An icon's id, the same on every start of the same thermi, so Windows remembers where it was pinned: made from the
    /// item's id, or from thermi's for its own icon, and from the executable's path, since Windows ties an id to the path
    /// that first used it.
    /// </summary>
    public static Guid IdOf(string? itemId, string exePath)
    {
        var name = string.Concat(exePath.ToUpperInvariant(), "\n", itemId is null ? ThermiName : "item\n" + itemId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    /// <summary>The overlay an icon opens the window on: its item's, or the first for thermi's own icon and an item that is gone.</summary>
    public static int OverlayOf(ThermiConfig config, string? itemId) => itemId is null ? 0 : ConfigEdits.Find(config, itemId)?.Overlay ?? 0;

    private static ServiceState StateWithout(ServiceState service) => service == ServiceState.Stopped ? service : ServiceState.Stale;

    // The mark's state colors its body; the icon's color is the ink of its line, and of the whole glyph while gray.
    private static TrayIcon ThermiIcon(Scene scene)
    {
        var color = scene.Input.Service == ServiceState.Running ? scene.Taskbar.Text : scene.Taskbar.Gray;
        return new TrayIcon(IdOf(null, scene.Input.ExePath), null, null, color, ThermiMark.Tooltip(scene.Config, scene.Input))
        {
            Mark = ThermiMark.Of(scene.Config, scene.Input, scene.Input.HighContrast),
        };
    }

    private static TrayIcon ItemIcon(Scene scene, int overlay, ItemConfig item, string? label)
    {
        var id = IdOf(item.Id, scene.Input.ExePath);
        // What Windows itself says shows whether the sensor service runs or not.
        var service = SystemSensors.Reads(item) ? ServiceState.Running : scene.Input.Service;
        var (texts, numbers) = (scene.Input.Texts, scene.Input.Numbers);
        var now = OverlayLayout.ReadingOf(item, scene.Readings[overlay]);
        var reading = scene.Input.History?.Shown(item, now) ?? now;
        var tooltip = TrayTexts.ItemTooltip(label, reading, service, texts, numbers, scene.Config.Temperature, item.Decimals);
        if (service != ServiceState.Running)
        {
            return new TrayIcon(id, item.Id, ReadingFormatter.Missing, scene.Taskbar.Text, tooltip);
        }

        // Digits take the taskbar's text color at normal, as with no meter, and their level's color only at warning and critical.
        var text = reading is { } read ? TrayTexts.IconText(read, numbers, scene.Config.Temperature, item.Decimals) : ReadingFormatter.Missing;
        var color = MeterColor(scene, overlay, item) is { Level: not Level.Normal } metered ? metered.Color : scene.Taskbar.Text;
        return new TrayIcon(id, item.Id, text, color, tooltip);
    }

    private static (Level Level, Rgb Color)? MeterColor(Scene scene, int overlay, ItemConfig item)
    {
        if (item.Meter is null || OverlayLayout.MeterOf(item, scene.Readings[overlay]) is not { Usable: not null } meter)
        {
            return null;
        }

        var level = OverlayLayout.LevelOf(item, meter);
        // The icon sits on the taskbar, not on a pill: the look's colors on dark pills are the ones chosen or the dark
        // taskbar's, and a light taskbar takes its own for any that would not read on it.
        var look = OverlayLook.Of(scene.Config.Overlays[overlay].Style with { Pills = PillTheme.Dark }, lightTaskbar: false);
        return (level, scene.Taskbar.Readable(look.MeterColor(level), level));
    }

    private sealed record Scene(ThermiConfig Config, TrayInput Input, Taskbar Taskbar, List<IReadOnlyDictionary<string, Reading>> Readings);
}

/// <summary>Overlays named as the window's list names them, for thermi's own tray icon and its submenus.</summary>
public static class OverlayNames
{
    /// <summary>The name the user typed, else where it sits, as <see cref="Names.OfOverlays"/> says.</summary>
    public static string Of(ThermiConfig config, int overlay, Texts texts) => Names.OfOverlays(config, texts)[overlay];
}
