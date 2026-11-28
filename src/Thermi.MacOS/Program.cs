namespace Thermi.MacOS;

/// <summary><c>thermi</c> on macOS: the overlay, the menu bar's items, <c>thermi settings</c> and <c>thermi preview</c>.</summary>
internal static class Program
{
    private static int Main()
    {
        NSApplication.Init();
        Console.Error.WriteLine("thermi: this build shows no overlay.");
        return 1;
    }
}
