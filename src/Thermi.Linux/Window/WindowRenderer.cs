using System.Runtime.InteropServices;

namespace Thermi.Linux.Window;

/// <summary>
/// GTK's renderer for the window. Where WebKit paints without the GPU, as Ubuntu's does on NVIDIA's driver, GTK's GPU
/// renderers would upload each in-memory frame to a new texture each second, a cost near all the rest the idle window
/// does; GTK's Cairo renderer shows the frame as it is.
/// </summary>
internal static class WindowRenderer
{
    // GSK takes a renderer named on the display before GSK_RENDERER, as GTK's inspector names its own: writing the variable
    // instead would race the threads that read the environment, on a glibc before 2.41.
    private const string DisplayRenderer = "gsk-renderer";

    // Read by GSK as the window is realized, and kept as long as the process.
    private static readonly IntPtr Cairo = Marshal.StringToCoTaskMemUTF8("cairo");

    /// <summary>
    /// The Cairo renderer for <paramref name="window"/> where WebKit draws without the GPU, unless a person chose one. The
    /// name goes from the display once the window is realized, so no later surface takes it.
    /// </summary>
    public static void Choose(WebKit.Settings settings, Gtk.Window window)
    {
        if (settings.GetHardwareAccelerationPolicy() == WebKit.HardwareAccelerationPolicy.Never && GLib.Functions.Getenv("GSK_RENDERER") is null)
        {
            var display = window.GetDisplay();
            display.SetData(DisplayRenderer, Cairo);
            window.OnRealize += (_, _) => display.SetData(DisplayRenderer, IntPtr.Zero);
        }
    }
}
