extern alias sensors;

using sensors::Thermi.Sensors.Windows.Frames;

namespace Thermi.Windows.Tests.Sensors.Frames;

/// <summary>A process held as the service holds one, which ends when a test says.</summary>
internal sealed class EndingProcess(ProcessFacts facts) : IHeldProcess
{
    public ProcessFacts Facts { get; } = facts;

    public bool Exited { get; set; }

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
