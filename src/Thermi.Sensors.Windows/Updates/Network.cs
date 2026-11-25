using System.Runtime.InteropServices;
using Windows.Win32.Networking.NetworkListManager;

namespace Thermi.Sensors.Windows.Updates;

/// <summary>Whether Windows counts the network as metered, as a phone's hotspot or a capped plan, where only an urgent version downloads.</summary>
internal static class Network
{
    private const uint Costly = (uint)NLM_CONNECTION_COST.NLM_CONNECTION_COST_FIXED | (uint)NLM_CONNECTION_COST.NLM_CONNECTION_COST_VARIABLE
        | (uint)NLM_CONNECTION_COST.NLM_CONNECTION_COST_OVERDATALIMIT | (uint)NLM_CONNECTION_COST.NLM_CONNECTION_COST_ROAMING;

    public static unsafe bool Metered()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8))
        {
            return false;
        }

        try
        {
            var costs = (INetworkCostManager)new NetworkListManager();
            costs.GetCost(out var cost, null);
            return (cost & Costly) != 0;
        }
        catch (COMException)
        {
            // Without the answer, the network is treated as one that is not: a check is a few hundred bytes either way.
            return false;
        }
    }
}
