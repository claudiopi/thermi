using System.ComponentModel;
using System.ServiceProcess;
using Thermi.Core.Updates;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Services;

namespace Thermi.Sensors.Windows.Updates;

/// <summary>
/// Which version each of thermi's services runs: its image path names the version's folder, and a switch changes it
/// with the service manager's own call, one write that either happened or did not (docs/adr/0008-updates.md).
/// </summary>
internal sealed class ServiceConfig(InstallFolder folder)
{
    public const string Elevated = "thermi-elevated";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>Points both services at <paramref name="version"/>; the one running goes on until it is started again.</summary>
    public void Point(ReleaseVersion version)
    {
        var exe = folder.ServiceOf(version);
        Change(SensorService.Name, $"\"{exe}\"");
        Change(Elevated, $"\"{exe}\" --elevated");
    }

    public static void Stop(string name = SensorService.Name)
    {
        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.StartPending)
        {
            // One still starting refuses a stop; a watchdog the start itself began asks while it does.
            try
            {
                service.WaitForStatus(ServiceControllerStatus.Running, Wait);
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                // It stops as it is, or says why.
            }

            service.Refresh();
        }

        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, Wait);
        }
    }

    public static void Start(string name = SensorService.Name)
    {
        using var service = new ServiceController(name);
        service.Start();
    }

    // The raw calls, since the tag must be null: the friendly overload of ChangeServiceConfig always passes one.
    private static unsafe void Change(string name, string imagePath)
    {
        var manager = SensorPInvoke.OpenSCManager((PCWSTR)null, (PCWSTR)null, SensorPInvoke.SC_MANAGER_CONNECT);
        var service = SC_HANDLE.Null;
        try
        {
            fixed (char* serviceName = name, path = imagePath)
            {
                service = manager.IsNull ? SC_HANDLE.Null : SensorPInvoke.OpenService(manager, serviceName, SensorPInvoke.SERVICE_CHANGE_CONFIG);
                if (service.IsNull || !SensorPInvoke.ChangeServiceConfig(
                    service, (ENUM_SERVICE_TYPE)SensorPInvoke.SERVICE_NO_CHANGE, (SERVICE_START_TYPE)SensorPInvoke.SERVICE_NO_CHANGE,
                    (SERVICE_ERROR)SensorPInvoke.SERVICE_NO_CHANGE, path, null, null, null, null, null, null))
                {
                    throw new Win32Exception();
                }
            }
        }
        finally
        {
            Close(service);
            Close(manager);
        }
    }

    private static void Close(SC_HANDLE handle)
    {
        if (!handle.IsNull)
        {
            SensorPInvoke.CloseServiceHandle(handle);
        }
    }
}
