extern alias sensors;

using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.Sensors.Windows.Service;

namespace Thermi.Windows.Tests.Sensors;

/// <summary>
/// The <c>thermi-elevated</c> service, which runs the window's two fixes as SYSTEM: what the elevated thermi it starts
/// inherits, and the exit code it stops with, which is the window's answer.
/// </summary>
public sealed class ElevatedServiceTests
{
    // The service's environment is the machine's, which an administrator's programs set too, as a monitoring agent's
    // profiler does: of it, the elevated thermi gets only Windows' own folders, read from Windows, and a PATH of System32,
    // so nothing loads code into .NET before its Main, and nothing of a user's profile is there.
    [Theory]
    [InlineData("CORECLR_ENABLE_PROFILING")]
    [InlineData("CORECLR_PROFILER_PATH")]
    [InlineData("COR_PROFILER")]
    [InlineData("COR_PROFILER_PATH")]
    [InlineData("DOTNET_ADDITIONAL_DEPS")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("DOTNET_ROOT")]
    [InlineData("LOCALAPPDATA")]
    public void TheElevatedThermiGetsNoVariableItDoesNotName(string name)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = Path.Combine(Path.GetTempPath(), "planted.dll"),
            ["PATH"] = Path.GetTempPath(),
        };

        ElevatedService.Clean(environment);

        Assert.Equal((false, Environment.SystemDirectory), (environment.ContainsKey(name), environment["PATH"]));
    }

    // Whatever the fix answers, or a thermi that could not start, the service stops with it, so the window reads it.
    [Theory]
    [InlineData(8, 8)]
    [InlineData(null, 1)]
    public async Task TheServiceStopsWithItsFixsExitCode(int? answer, int exitCode)
    {
        var stopped = new TaskCompletionSource();
        string[]? ran = null;
        var reported = new List<Exception>();
        using var service = new ElevatedService(
            (arguments, _) =>
            {
                ran = [.. arguments];
                return answer is { } code ? Task.FromResult(code) : throw new Win32Exception(2);
            },
            new FakeTimeProvider(),
            reported.Add,
            stopped.SetResult);

        await service.RunAsync(["install-driver"]);

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Equal((exitCode, ElevatedService.Name), (service.ExitCode, service.ServiceName));
        Assert.Equal(["elevated", "install-driver"], ran!);
        Assert.Equal(answer is null ? 1 : 0, reported.Count);
    }
}
