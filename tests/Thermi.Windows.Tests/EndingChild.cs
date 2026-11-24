using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Thermi.Logging;

namespace Thermi.Windows.Tests;

/// <summary>
/// The test assembly started again as a process that ends on a failure, for <see cref="ThermiLogTests"/>: with
/// <see cref="FailureVariable"/> set, it fails before xunit starts, since xunit's own handler of what ends a process
/// waits forever and runs before any a test adds.
/// </summary>
internal static class EndingChild
{
    /// <summary>Where the child logs, through the log the overlay and the window use.</summary>
    public const string LogVariable = "THERMI_TEST_CHILD_LOG";

    /// <summary>How it fails: "timer", on a timer's thread while the work runs, or "entry", in the work itself.</summary>
    public const string FailureVariable = "THERMI_TEST_CHILD_FAILURE";

#pragma warning disable CA2255 // The one way to run before xunit's entry point, in the test assembly's own process only.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void FailIfAsked()
    {
        if (Environment.GetEnvironmentVariable(FailureVariable) is not { } failure || Environment.GetEnvironmentVariable(LogVariable) is not { } path)
        {
            return;
        }

        Fail(failure, path);
    }

    // Apart, so that compiling the initializer loads no thermi type: coverlet leaves out every assembly the test
    // process has loaded before it instruments them, and would measure none of thermi.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Fail(string failure, string path)
    {
        using var logs = ThermiLog.Create(path, TimeProvider.System, trace: false);
        var log = logs.CreateLogger("Thermi.Tests");
        if (failure == "timer")
        {
            _ = ThermiLog.RunLoggedAsync(log, () =>
            {
                using var timer = TimeProvider.System.CreateTimer(_ => throw new InvalidOperationException("the timer's failure"), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                using var never = new ManualResetEventSlim();
                never.Wait();
                return Task.FromResult(0);
            });
            return;
        }

        // As it would leave Main: unhandled on a thread, which Windows hands over as it ends the process.
        var ended = ThermiLog.RunLoggedAsync(log, () => Task.FromException<int>(new InvalidOperationException("the entry's failure")));
        var leaving = new Thread(() => ExceptionDispatchInfo.Throw(ended.Exception!.InnerException!));
        leaving.Start();
        leaving.Join();
    }
}
