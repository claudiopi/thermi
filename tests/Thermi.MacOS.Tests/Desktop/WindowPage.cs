namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// The settings window's page as VoiceOver reads it, through the accessibility of System Events: WebKit gives the page's
/// buttons to it by the names a person hears, which a test presses as a person does.
/// </summary>
internal static class WindowPage
{
    // Presses the first button of the app of a pid's front window, page included, whose name is the one given.
    private const string Press = """
        function run([pid, name]) {
          const app = Application('System Events').processes.whose({ unixId: Number(pid) })[0];
          const button = app.windows[0].entireContents().find(each => each.role() === 'AXButton' && each.name() === name);
          if (button === undefined) { throw new Error('No button ' + name); }
          button.actions.byName('AXPress').perform();
        }
        """;

    /// <summary>Presses the button named <paramref name="name"/> in the window of <paramref name="pid"/>.</summary>
    public static async Task PressAsync(OwnSession session, int pid, string name, CancellationToken cancel)
    {
        var script = Path.Combine(session.Temporary, "press.js");
        await File.WriteAllTextAsync(script, Press, cancel).ConfigureAwait(false);
        var pressed = await session.RunAsync("osascript", ["-l", "JavaScript", script, $"{pid}", name], cancel: cancel).ConfigureAwait(false);
        if (pressed.Exit != 0)
        {
            throw new InvalidOperationException($"The page has no button to press: {pressed.Error}");
        }
    }
}
