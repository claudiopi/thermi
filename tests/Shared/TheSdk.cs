using Thermi.Client;

namespace Thermi.Tests.Shared;

/// <summary>
/// A program of the person's that reads thermi through the SDK, <c>new ThermiClient()</c>, which finds <c>thermi-cli</c> on
/// its PATH and starts it with its environment: the tests' own program, which a test's session starts with
/// <see cref="Status"/>, says on one line the service's state and whether an overlay runs.
/// </summary>
internal static class TheSdk
{
    /// <summary>What the tests' own program takes to answer as the SDK reads.</summary>
    public const string Status = "--sdk-status";

    /// <summary>The tests' own program, started with <see cref="Status"/>: the SDK's status, on one line.</summary>
    public static async Task<int> AnswerAsync()
    {
        var status = await new ThermiClient().StatusAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"{status.Service} {status.Overlay}").ConfigureAwait(false);
        return 0;
    }

    /// <summary>What the tests' own program said the SDK read, from its run.</summary>
    public static (string Service, bool Overlay) Said(Ran read)
    {
        var said = read.Out.Trim().Split(' ');
        return read.Exit == 0 && said.Length == 2 ? (said[0], bool.Parse(said[1])) : throw new InvalidOperationException($"The SDK did not read: {read.Error}");
    }
}
