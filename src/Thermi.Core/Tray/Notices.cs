using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Tray;

/// <summary>A Windows notice from thermi's icon, each a text of the translations.</summary>
public enum NoticeKind
{
    FirstRun,

    /// <summary>The first start on a PC whose CPU reads zero: thermi is running, and its click opens the window's driver bar.</summary>
    FirstRunDriver,
    OverlaysHidden,
    OverlayHidden,
    ClickThrough,
    ClickThroughAll,
}

/// <summary>A notice's buttons; the first-run notice has none, since a click on it opens the window.</summary>
[Flags]
public enum NoticeButtons
{
    None = 0,
    ShowThem = 1,
    ShowIt = 2,
    Undo = 4,
    GotIt = 8,
}

public sealed record Notice(NoticeKind Kind, NoticeButtons Buttons)
{
    /// <summary>The overlays it is about, in their order: those hidden, or those whose click-through its Undo turns off.</summary>
    public IReadOnlyList<int> Overlays { get; init; } = [];
}

/// <summary>
/// What the notices are decided from: the settings, the hide flag, whether the window made the last change, and whether
/// this is thermi's first start, which lasts until the window first opens.
/// </summary>
public sealed record NoticeInput(ThermiConfig Config, bool HideOverlays, bool FromWindow, bool FirstStart)
{
    /// <summary>Whether thermi's driver may be missing, as the CPU reads zero; null until the sensors are first read.</summary>
    public bool? DriverMissing { get; init; } = false;
}

/// <summary>
/// When thermi's icon shows a notice: on the first start; the first time overlays are hidden from outside the window,
/// since the window's list already says so; and each time click-through turns on, whatever turned it on, since an
/// overlay that ignores the mouse traps the user the same way. A notice goes when what it is about ends, and a new one
/// takes its place, as Windows shows one at a time.
/// </summary>
public sealed class Notices
{
    private NoticeInput? _last;
    private bool _firstRunTold;
    private bool _hiddenTold;

    /// <summary>The notice still showing, or null once what it was about ended.</summary>
    public Notice? Showing { get; private set; }

    /// <summary>Takes in the state now, and gives the notice to show, or null when none is new.</summary>
    public Notice? Update(NoticeInput input)
    {
        var before = _last;
        _last = input;
        if (Showing is { } showing && Ended(showing, input))
        {
            Showing = null;
        }

        var notice = FirstRun(input) ?? ClickThrough(before, input) ?? Hidden(before, input);
        Showing = notice ?? Showing;
        return notice;
    }

    /// <summary>A click-through notice's Undo: click-through off on the overlays it was about, those still there.</summary>
    public static ThermiConfig Undo(ThermiConfig config, Notice notice) =>
        notice.Overlays.Aggregate(config, (current, overlay) => OverlayEdits.SetClickThrough(current, overlay, on: false).Config);

    private static bool Ended(Notice notice, NoticeInput input) => notice.Kind switch
    {
        NoticeKind.FirstRun or NoticeKind.FirstRunDriver => !input.FirstStart,
        NoticeKind.ClickThrough or NoticeKind.ClickThroughAll =>
            !notice.Overlays.Any(overlay => overlay < input.Config.Overlays.Count && input.Config.Overlays[overlay].ClickThrough),
        NoticeKind.OverlaysHidden or NoticeKind.OverlayHidden => !input.HideOverlays && !input.Config.Overlays.Any(overlay => overlay.Hidden),
        _ => true,
    };

    // It waits for the first reading, so a PC without the driver is offered it at once, where the window's bar installs it.
    private Notice? FirstRun(NoticeInput input)
    {
        if (!input.FirstStart || _firstRunTold || input.DriverMissing is not { } driver)
        {
            return null;
        }

        _firstRunTold = true;
        return new Notice(driver ? NoticeKind.FirstRunDriver : NoticeKind.FirstRun, NoticeButtons.None);
    }

    private static Notice? ClickThrough(NoticeInput? before, NoticeInput input)
    {
        var overlays = input.Config.Overlays;
        var turned = Turned(overlays, before?.Config.Overlays, overlay => overlay.ClickThrough);
        if (turned.Count == 0)
        {
            return null;
        }

        var all = overlays.Count > 1 && overlays.All(overlay => overlay.ClickThrough);
        return new Notice(all ? NoticeKind.ClickThroughAll : NoticeKind.ClickThrough, NoticeButtons.Undo | NoticeButtons.GotIt)
        {
            Overlays = all ? [.. Enumerable.Range(0, overlays.Count)] : turned,
        };
    }

    private Notice? Hidden(NoticeInput? before, NoticeInput input)
    {
        if (_hiddenTold || input.FromWindow)
        {
            return null;
        }

        var overlays = input.Config.Overlays;
        var notice = input.HideOverlays && before?.HideOverlays != true
            ? new Notice(NoticeKind.OverlaysHidden, (overlays.Count == 1 ? NoticeButtons.ShowIt : NoticeButtons.ShowThem) | NoticeButtons.GotIt)
            {
                Overlays = [.. Enumerable.Range(0, overlays.Count)],
            }
            : Turned(overlays, before?.Config.Overlays, overlay => overlay.Hidden) is { Count: > 0 } hidden
                ? new Notice(NoticeKind.OverlayHidden, NoticeButtons.ShowIt | NoticeButtons.GotIt) { Overlays = hidden }
            : null;
        _hiddenTold = notice is not null;
        return notice;
    }

    // The overlays for which the flag is on now and was not before; an overlay that was not there counts as off.
    private static List<int> Turned(IReadOnlyList<OverlayConfig> now, IReadOnlyList<OverlayConfig>? before, Func<OverlayConfig, bool> flag) =>
        [.. Enumerable.Range(0, now.Count).Where(at => flag(now[at]) && !(before is not null && at < before.Count && flag(before[at])))];
}

/// <summary>An item critical long enough to tell: its value, or its peak and how long it lasted when it waited out a full-screen app.</summary>
public sealed record CriticalNotice(string ItemId, double Value, TimeSpan Length, bool WhilePlaying);

/// <summary>A critical notice with the item it is about and its reading now, which give its name and units.</summary>
public sealed record ItemNotice(ItemConfig Item, Reading Reading, CriticalNotice Notice);

/// <summary>
/// "Notify me when critical": one notice per episode, once the item has been critical for <see cref="After"/>. While a
/// full-screen app is in front, that notice waits; once the app leaves, one notice gives the episode's peak and how long
/// it lasted, up to when it cooled or to now.
/// </summary>
public sealed class NotifyMe(TimeProvider time)
{
    public static readonly TimeSpan After = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, Episode> _episodes = new(StringComparer.Ordinal);

    /// <summary>Takes in an item's level and value now, once a second, and gives the notice to show, or null.</summary>
    public CriticalNotice? Add(string itemId, Level level, double value, bool fullScreen)
    {
        var now = time.GetUtcNow();
        if (level == Level.Critical)
        {
            return Critical(itemId, value, fullScreen, now);
        }

        if (!_episodes.TryGetValue(itemId, out var episode))
        {
            return null;
        }

        if (episode.Held && fullScreen)
        {
            episode.Cooled ??= now;
            return null;
        }

        _episodes.Remove(itemId);
        return episode.Held ? new CriticalNotice(itemId, episode.Peak, (episode.Cooled ?? now) - episode.Start, WhilePlaying: true) : null;
    }

    /// <summary>Takes in a fresh snapshot for every item that asks to be told, and gives each notice to show with its item and reading.</summary>
    public IReadOnlyList<ItemNotice> Add(ThermiConfig config, Snapshot snapshot, bool fullScreen)
    {
        var told = new List<ItemNotice>();
        foreach (var overlay in config.Overlays)
        {
            var readings = SensorMap.Read(snapshot, overlay);
            foreach (var item in overlay.Items.Where(item => item.Notify))
            {
                var reading = OverlayLayout.ReadingOf(item, readings);
                var level = OverlayLayout.LevelOf(item, OverlayLayout.MeterOf(item, readings));
                if (reading is { Usable: { } value } read && Add(item.Id, level, value, fullScreen) is { } notice)
                {
                    told.Add(new ItemNotice(item, read, notice));
                }
            }
        }

        return told;
    }

    private CriticalNotice? Critical(string itemId, double value, bool fullScreen, DateTimeOffset now)
    {
        if (!_episodes.TryGetValue(itemId, out var episode))
        {
            _episodes[itemId] = episode = new Episode(now);
        }

        episode.Peak = Math.Max(episode.Peak, value);
        episode.Cooled = null;
        if (episode.Held && !fullScreen)
        {
            (episode.Held, episode.Told) = (false, true);
            return new CriticalNotice(itemId, episode.Peak, now - episode.Start, WhilePlaying: true);
        }

        if (episode.Told || now - episode.Start < After)
        {
            return null;
        }

        episode.Held = fullScreen;
        episode.Told = !fullScreen;
        return fullScreen ? null : new CriticalNotice(itemId, value, now - episode.Start, WhilePlaying: false);
    }

    /// <summary>One run of critical seconds: when it began, its peak, and whether its notice was told or waits for a full-screen app to leave.</summary>
    private sealed class Episode(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; } = start;

        public double Peak { get; set; } = double.MinValue;

        public bool Told { get; set; }

        public bool Held { get; set; }

        /// <summary>When it left critical while its notice waited, so its length stops there.</summary>
        public DateTimeOffset? Cooled { get; set; }
    }
}
