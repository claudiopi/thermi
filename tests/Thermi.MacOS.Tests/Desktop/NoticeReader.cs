namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// The notifications on the Mac's screen, as VoiceOver reads them, through the accessibility of System Events: each of
/// Notification Center's banners, with the app it says it is from, its title and its buttons, which a test clicks.
/// </summary>
internal static class NoticeReader
{
    // Lists each banner as "app|title|button,button", or clicks the button named of the banner of a title; Notification
    // Center draws its banners in its own windows, each a group of texts and buttons.
    private const string Read = """
        function run([title, button]) {
          const center = Application('System Events').processes['NotificationCenter'];
          const banners = center.windows().flatMap(window => window.groups().flatMap(group => [group, ...group.groups()]));
          const said = banners.map(banner => ({ banner, texts: banner.staticTexts.value(), buttons: banner.buttons.name() }))
            .filter(notice => notice.texts.length >= 2);
          if (button !== undefined) {
            said.find(notice => notice.texts[1] === title).banner.buttons.byName(button).click();
            return '';
          }
          return said.map(notice => [notice.texts[0], notice.texts[1], notice.buttons.join(',')].join('|')).join('\n');
        }
        """;

    /// <summary>The notifications on screen now, from any app.</summary>
    public static async Task<IReadOnlyList<(string App, string Title, string[] Buttons)>> ShownAsync(OwnSession session, CancellationToken cancel)
    {
        var told = await RunAsync(session, [], cancel).ConfigureAwait(false);
        return [.. told.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('|')).Select(n => (n[0], n[1], n[2].Split(',')))];
    }

    /// <summary>Clicks <paramref name="button"/> of the notification titled <paramref name="title"/>.</summary>
    public static Task ClickAsync(OwnSession session, string title, string button, CancellationToken cancel) => RunAsync(session, [title, button], cancel);

    private static async Task<string> RunAsync(OwnSession session, string[] args, CancellationToken cancel)
    {
        var script = Path.Combine(session.Temporary, "notices.js");
        await File.WriteAllTextAsync(script, Read, cancel).ConfigureAwait(false);
        var ran = await session.RunAsync("osascript", ["-l", "JavaScript", script, .. args], cancel: cancel).ConfigureAwait(false);
        return ran.Exit == 0 ? ran.Out : throw new InvalidOperationException($"System Events read no notification: {ran.Error}");
    }
}
