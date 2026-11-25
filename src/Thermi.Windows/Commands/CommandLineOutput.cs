using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Console;

namespace Thermi.Windows.Commands;

/// <summary>
/// Where <c>thermi</c>'s errors and help go. <c>thermi.exe</c> is a windowed program, which Windows gives no console of its
/// own: started from a terminal, it writes in the terminal's; redirected, to a file or a pipe; started from Explorer,
/// nowhere, since nobody is there to read it.
/// </summary>
internal static class CommandLineOutput
{
    /// <summary>Attaches the terminal's console when <paramref name="stream"/> leads nowhere, so what is written to it shows there.</summary>
    public static void Borrow(STD_HANDLE stream)
    {
        var handle = PInvoke.GetStdHandle(stream);
        if (handle.IsNull || handle == HANDLE.INVALID_HANDLE_VALUE)
        {
            // Without a terminal behind the process, as from Explorer, this fails, and the text goes nowhere.
            PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS);
        }
    }
}
