namespace Thermi.SensorHost.Install;

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
