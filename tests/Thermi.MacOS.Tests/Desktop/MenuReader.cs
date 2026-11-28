namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// A menu thermi has open, as VoiceOver reads it, through the accessibility of System Events, which the user of a session
/// made for tests lets osascript use: AppKit's own menus are the only ones it finds there, with their titles.
/// </summary>
internal static class MenuReader
{
    // Lists the titles of the menu the app of a pid has open, its overlay's or its menu bar item's, "-" for a separator;
    // clicks the entry at an index, or the app's first item in the menu bar, thermi's own.
    private const string Read = """
        function run([pid, choose]) {
          const app = Application('System Events').processes.whose({ unixId: Number(pid) })[0];
          const status = app.menuBars.length > 1 ? app.menuBars[1].menuBarItems[0] : null;
          if (choose === 'status') { status.click(); return ''; }
          const menu = app.menus.length > 0 ? app.menus[0] : status && status.menus.length > 0 ? status.menus[0] : null;
          if (menu === null) { return ''; }
          const items = menu.menuItems();
          if (choose !== undefined) { items[Number(choose)].click(); return ''; }
          return items.map(item => item.title() || '-').join('\n');
        }
        """;

    /// <summary>The titles of the entries of the menu <paramref name="pid"/> has open, separators left out; none when it has none.</summary>
    public static async Task<IReadOnlyList<string>> EntriesAsync(OwnSession session, int pid, CancellationToken cancel)
    {
        var told = await RunAsync(session, [$"{pid}"], cancel).ConfigureAwait(false);
        return [.. told.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(title => title != "-")];
    }

    /// <summary>Chooses the entry at <paramref name="index"/> of the menu <paramref name="pid"/> has open, separators counted out.</summary>
    public static async Task ChooseAsync(OwnSession session, int pid, int index, CancellationToken cancel)
    {
        var told = await RunAsync(session, [$"{pid}"], cancel).ConfigureAwait(false);
        var at = told.Split('\n').Select((title, each) => (title, each)).Where(entry => entry.title != "-").ElementAt(index).each;
        await RunAsync(session, [$"{pid}", $"{at}"], cancel).ConfigureAwait(false);
    }

    /// <summary>Opens the menu of thermi's own item in the menu bar, which <see cref="EntriesAsync"/> then reads.</summary>
    public static Task OpenStatusItemAsync(OwnSession session, int pid, CancellationToken cancel) => RunAsync(session, [$"{pid}", "status"], cancel);

    private static async Task<string> RunAsync(OwnSession session, string[] args, CancellationToken cancel)
    {
        var script = Path.Combine(session.Temporary, "menu.js");
        await File.WriteAllTextAsync(script, Read, cancel).ConfigureAwait(false);
        var ran = await session.RunAsync("osascript", ["-l", "JavaScript", script, .. args], cancel: cancel).ConfigureAwait(false);
        return ran.Exit == 0 ? ran.Out : throw new InvalidOperationException($"System Events read no menu: {ran.Error}");
    }
}
