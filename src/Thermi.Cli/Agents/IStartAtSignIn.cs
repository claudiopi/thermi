namespace Thermi.Cli.Agents;

/// <summary>Whether the overlay starts at sign-in, as the OS holds it, and the switch that changes it.</summary>
internal interface IStartAtSignIn
{
    bool On { get; }

    void Set(bool on);
}
