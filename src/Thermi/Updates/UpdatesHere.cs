using System.Reflection;
using Microsoft.Win32;
using Thermi.Core.Updates;

namespace Thermi.Updates;

/// <summary>
/// What About says of updates on this PC (docs/adr/0008-updates.md): whether this build updates itself at all, the PC's
/// policy, and the service's state, which it reads but never writes.
/// </summary>
internal static class UpdatesHere
{
    private static readonly bool Built = typeof(UpdatesHere).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "ThermiUpdatesFrom" && attribute.Value is { Length: > 0 });

    public static UpdateLine Line(DateTimeOffset now)
    {
        using var key = Registry.LocalMachine.OpenSubKey(UpdatePolicy.Key);
        var policy = UpdatePolicy.Of(
            Built && ThermiPaths.State is not null, key?.GetValue(UpdatePolicy.UpdatesValue), key?.GetValue(UpdatePolicy.ChannelValue));
        return UpdateStatus.Of(policy, State(), now);
    }

    private static UpdateState State()
    {
        try
        {
            return UpdateState.Read(ThermiPaths.State is { } path && File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The service writes it whole by a move; a read that meets it mid-move says no check was made yet.
            return new UpdateState();
        }
    }
}
