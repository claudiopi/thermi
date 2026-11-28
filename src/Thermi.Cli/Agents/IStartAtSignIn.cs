namespace Thermi.Cli.Agents;

/// <summary>
/// Whether the overlay starts at sign-in, as the OS holds it, and the switch that changes it; asked of another program on
/// Linux, which the window's loop and thermi-cli wait for without blocking.
/// </summary>
internal interface IStartAtSignIn
{
    Task<bool> OnAsync(CancellationToken cancel);

    Task SetAsync(bool on, CancellationToken cancel);
}
