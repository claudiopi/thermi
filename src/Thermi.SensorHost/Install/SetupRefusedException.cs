namespace Thermi.SensorHost.Install;

/// <summary>Windows refused a step of the install.</summary>
public sealed class SetupRefusedException : Exception
{
    public SetupRefusedException()
    {
    }

    public SetupRefusedException(string message)
        : base(message)
    {
    }

    public SetupRefusedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
