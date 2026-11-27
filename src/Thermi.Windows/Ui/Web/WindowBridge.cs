using Thermi.App.Link;
using Thermi.App.Window;
using Thermi.Core.Look;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Ui.Web;

/// <summary>The bridge every OS shares, on Windows: what only Windows can do it asks of <see cref="WindowsHost"/>.</summary>
internal sealed class WindowBridge : PageBridge
{
    private readonly WindowsHost _host;

    public WindowBridge(WindowStart start, WindowEditor editor, ChartHistory chart, Action<Action> onTurn)
        : this(new WindowsHost(start), start, editor, chart, onTurn)
    {
    }

    private WindowBridge(WindowsHost host, WindowStart start, WindowEditor editor, ChartHistory chart, Action<Action> onTurn)
        : base(host, start.Page, editor, chart, onTurn)
    {
        _host = host;
    }

    public WindowEnd? Link
    {
        get => _host.Link;
        set => _host.Link = value;
    }

    public HWND Window
    {
        get => _host.Window;
        set => _host.Window = value;
    }
}
