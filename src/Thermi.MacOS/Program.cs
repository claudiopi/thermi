using Thermi.MacOS.Agents;

namespace Thermi.MacOS;

/// <summary>
/// <c>thermi</c> on macOS: the overlay, the menu bar's items, <c>thermi settings</c> and <c>thermi preview</c>; and
/// <c>thermi register</c> and <c>thermi unregister</c>, which load and take out its agents before AppKit starts, so the
/// cask runs them over SSH too, where there is no window server.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (await Registration.RunAsync(args, Console.Error, TimeProvider.System, CancellationToken.None) is { } registered)
        {
            return registered;
        }

        NSApplication.Init();
        await Console.Error.WriteLineAsync("thermi: this build shows no overlay.");
        return 1;
    }
}
