using System.Globalization;
using System.Reflection;
using System.ServiceProcess;
using Microsoft.Win32;
using Thermi.Commands;
using Thermi.Core.Install;
using Thermi.Core.Updates;
using Thermi.Install;

namespace Thermi.Updates;

/// <summary>
/// What About says of updates on this PC (docs/adr/0008-updates.md): whether this build updates itself at all, the PC's
/// policy, and the service's state, which it reads but never writes.
/// </summary>
internal static class UpdatesHere
{
    private static readonly bool Built = UpdatePolicy.Updates(Metadata("ThermiUpdatesFrom"), Metadata("ThermiUpdatesSigners"));

    public static UpdateLine Line(DateTimeOffset now, DateTimeOffset? asked = null)
    {
        using var key = Registry.LocalMachine.OpenSubKey(UpdatePolicy.Key);
        var policy = UpdatePolicy.Of(
            Built && ThermiPaths.Updates is not null, key?.GetValue(UpdatePolicy.UpdatesValue), key?.GetValue(UpdatePolicy.ChannelValue));
        return UpdateStatus.Of(policy, State(), now, asked);
    }

    /// <summary>
    /// Sends the service one of its update codes, which any signed-in user may, or in a per-user install sets the user's own
    /// host's event of it (docs/adr/0016-admin-on-demand.md); throws as Windows refuses, or when no host made the event.
    /// </summary>
    public static void Tell(int code)
    {
        if (!SensorPipes.ServiceInstalled() && SensorPipes.UserPipe is { } pipe)
        {
            if (!EventWaitHandle.TryOpenExisting(UserHost.EventOf(pipe, code.ToString(CultureInfo.InvariantCulture)), out var told))
            {
                throw new InvalidOperationException("No sensor host of this user's runs to be told.");
            }

            using (told)
            {
                told.Set();
            }

            return;
        }

        using var service = new ServiceController(StartSensors.Service);
        service.ExecuteCommand(code);
    }

    private static string? Metadata(string key) =>
        typeof(UpdatesHere).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;

    public static Thermi.Core.Install.MachineInstall? MachineInstall() => State().MachineInstall;

    private static UpdateState State()
    {
        for (var tries = 1; tries <= 3; tries++)
        {
            try
            {
                return UpdateState.Read(ThermiPaths.State() is { } path ? File.ReadAllText(path) : null);
            }
            catch (FileNotFoundException) when (tries < 3)
            {
                // The service writes a newer copy, then deletes this one: the next look finds the newer.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A read that keeps missing it says no check was made yet.
                return new UpdateState();
            }
        }

        return new UpdateState();
    }
}
