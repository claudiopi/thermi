using Thermi.SensorHost;

namespace Thermi.Windows.Tests;

/// <summary>The service around the pipe server: a failure must stop it with an error, so Windows restarts it.</summary>
public sealed class SensorServiceTests
{
    [Fact]
    public async Task AFailingServerStopsTheServiceWithAnErrorCode()
    {
        var stopped = new TaskCompletionSource();
        using var service = new SensorService(_ => throw new InvalidOperationException("The GPU driver reset."), stopped.SetResult);

        await service.RunAsync();

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Equal(1, service.ExitCode);
        Assert.Equal(SensorService.Name, service.ServiceName);
    }

    [Fact]
    public async Task AServerThatStopsWhenAskedLeavesNoError()
    {
        var stopped = false;
        using var service = new SensorService(_ => Task.CompletedTask, () => stopped = true);

        await service.RunAsync();

        Assert.Equal(0, service.ExitCode);
        Assert.False(stopped);
    }
}
