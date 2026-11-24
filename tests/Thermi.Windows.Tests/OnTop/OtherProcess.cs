using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.OnTop;

/// <summary>
/// Another process's windows on a test's desktop, for <see cref="TopmostTests"/>: the overlay ignores what its own process
/// reorders, so the taskbar it answers must be another's. It is the test assembly started again, which with
/// <see cref="DesktopVariable"/> set joins that desktop before xunit starts, and raises a window above every other for
/// each line it reads: "taskbar" one of the taskbar's class, a class of thermi's as itself, any other word one of a class
/// of its own.
/// </summary>
internal sealed class OtherProcess : IDisposable
{
    private const string DesktopVariable = "THERMI_TEST_CHILD_DESKTOP";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Process _process;

    /// <param name="desktop">The test's desktop, which it joins.</param>
    /// <param name="program">A copy of the test assembly's program to run under this name, so it is another program to Windows, not this one again.</param>
    public OtherProcess(string desktop, string? program = null)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Thermi.Windows.Tests.exe");
        if (program is not null)
        {
            var copy = Path.Combine(AppContext.BaseDirectory, program);
            File.Copy(exe, copy, overwrite: true);
            exe = copy;
        }

        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[DesktopVariable] = desktop;
        _process = Process.Start(start) ?? throw new InvalidOperationException("The other process did not start.");
    }

    public int Id => _process.Id;

    /// <summary>Raises the window of <paramref name="kind"/> above every other, made the first time, and gives it once raised.</summary>
    public HWND Raise(string kind)
    {
        _process.StandardInput.WriteLine(kind);
        _process.StandardInput.Flush();

        // A plain read, which handles none of the thread's messages, so the overlay cannot answer the raise before the test
        // looks; a line that never comes ends the other process, and the read with it.
        var late = false;
        using (TimeProvider.System.CreateTimer(_ => { late = true; _process.Kill(); }, null, Patience, Timeout.InfiniteTimeSpan))
        {
            if (_process.StandardOutput.ReadLine() is { } line)
            {
                return (HWND)nint.Parse(line, CultureInfo.InvariantCulture);
            }
        }

        throw late
            ? new TimeoutException($"The other process raised no {kind} window in {Patience.TotalSeconds:n0} s.")
            : new InvalidOperationException("The other process ended.");
    }

    public void Dispose()
    {
        _process.StandardInput.Close();
        if (!_process.WaitForExit(Patience))
        {
            _process.Kill();
        }

        _process.Dispose();
    }

#pragma warning disable CA2255 // The one way to run before xunit's entry point, in the test assembly's own process only.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RaiseIfAsked()
    {
        if (Environment.GetEnvironmentVariable(DesktopVariable) is not { } desktop)
        {
            return;
        }

        // On the initializer's own thread, which has no window yet, so it may move to the test's desktop: another thread
        // would wait for the initializer to end before it could run any code of this assembly.
        Serve(desktop);
#pragma warning disable RS0030 // Ends the helper before xunit's entry point would run the tests in it.
        Environment.Exit(0);
#pragma warning restore RS0030
    }

    // Apart, so that compiling the initializer loads no thermi type: coverlet leaves out every assembly the test
    // process has loaded before it instruments them, and would measure none of thermi.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Serve(string desktopName)
    {
        using var desktop = TestPInvoke.OpenDesktop(desktopName, default, false, TestWindows.AllAccess);
        if (desktop.IsInvalid || !TestPInvoke.SetThreadDesktop(desktop))
        {
            throw new Win32Exception();
        }

        var windows = new Dictionary<string, HWND>(StringComparer.Ordinal);
        while (Console.ReadLine() is { } kind)
        {
            if (!windows.TryGetValue(kind, out var window))
            {
                var className = kind == "taskbar" ? "Shell_TrayWnd" : kind.StartsWith("thermi-", StringComparison.Ordinal) ? kind : "thermi-test-" + kind;
                HiddenWindow.Register(className, name => HiddenWindow.Plain(TestWindows.DefaultProcedure, name));

                // Topmost from the start, as a taskbar is: one made topmost only once shown stays under the overlay at its first raise.
                windows[kind] = window = HiddenWindow.Create(className, WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST);
                PInvoke.SetWindowPos(window, HWND.HWND_TOPMOST, 0, 0, 200, 40, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
            }

            PInvoke.SetWindowPos(window, HWND.HWND_TOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            Console.WriteLine(((nint)window).ToString(CultureInfo.InvariantCulture));
        }
    }
}
