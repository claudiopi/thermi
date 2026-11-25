using System.Diagnostics;
using Microsoft.Win32;
using Thermi.Core.Updates;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// What the updater asks of the PC beyond its folder: the policy an administrator left, read at each check so a change
/// needs no restart; whether the network is metered; and starting the watchdog, a process that outlives the service.
/// </summary>
internal sealed record UpdateHost(Func<(object? Updates, object? Channel)> PolicyValues, Func<bool> Metered, Action<ProcessStartInfo> Start)
{
    public static UpdateHost Windows { get; } = new(ReadPolicy, Network.Metered, start => Process.Start(start)?.Dispose());

    public UpdatePolicy Policy(UpdateBuild build)
    {
        var (updates, channel) = PolicyValues();
        return UpdatePolicy.Of(build.Built, updates, channel);
    }

    private static (object? Updates, object? Channel) ReadPolicy()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UpdatePolicy.Key);
        return (key?.GetValue(UpdatePolicy.UpdatesValue), key?.GetValue(UpdatePolicy.ChannelValue));
    }
}
