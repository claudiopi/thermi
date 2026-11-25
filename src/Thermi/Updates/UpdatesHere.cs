using System.Reflection;
using System.ServiceProcess;
using Microsoft.Win32;
using Thermi.Commands;
using Thermi.Core.Updates;

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

    /// <summary>Sends the service one of its update codes, which any signed-in user may; throws as Windows refuses.</summary>
    public static void Tell(int code)
    {
        using var service = new ServiceController(StartSensors.Service);
        service.ExecuteCommand(code);
    }

    private static string? Metadata(string key) =>
        typeof(UpdatesHere).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;

    private static UpdateState State()
    {
        try
        {
            return UpdateState.Read(ThermiPaths.State() is { } path ? File.ReadAllText(path) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The service writes it whole by a move; a read that meets it mid-move says no check was made yet.
            return new UpdateState();
        }
    }
}
