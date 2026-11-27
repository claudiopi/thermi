using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Thermi.Client;
using Thermi.Core.Agents;
using Thermi.Tests.Contract;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The .NET SDK, <c>Thermi.Client</c> (<c>src/Thermi.Client/README.md</c>): tiny, MIT and of .NET alone, it reads every
/// answer of the contract whole, and without thermi it says how to install it and starts nothing.
/// </summary>
public sealed class SdkTests
{
    [Fact]
    public void TheSdkReadsEveryAnswer()
    {
        var project = XDocument.Load(Path.Combine(TestFiles.Root, "src", "Thermi.Client", "Thermi.Client.csproj"));
        Assert.DoesNotContain(project.Descendants(), element => element.Name.LocalName is "PackageReference" or "ProjectReference" or "Content");
        Assert.Equal("MIT", project.Descendants("PackageLicenseExpression").Single().Value);
        Assert.All(
            typeof(ThermiClient).Assembly.GetReferencedAssemblies(),
            reference => Assert.True(
                reference.Name is "netstandard" || reference.Name!.StartsWith("System.", StringComparison.Ordinal), $"{reference.Name} is not .NET's."));

        foreach (var (schema, json) in ContractAnswers.All())
        {
            var read = schema switch
            {
                "status" => JsonSerializer.Serialize(ThermiClient.Parse(json, ThermiJson.Default.ThermiStatus), ThermiJson.Default.ThermiStatus),
                "read" => JsonSerializer.Serialize(ThermiClient.Parse(json, ThermiJson.Default.ThermiReading), ThermiJson.Default.ThermiReading),
                _ => JsonSerializer.Serialize(ThermiClient.Parse(json, ThermiJson.Default.ThermiSensors), ThermiJson.Default.ThermiSensors),
            };
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(read)), $"The SDK read a {schema} answer otherwise:\n{json}\n{read}");
        }
    }

    // Spec 003, R22: thermi-cli on PATH first, else where an install for everyone or for one person alone puts it.
    [Fact]
    public void TheSdkFindsThermiCliWhereEitherInstallPutsIt()
    {
        var root = Directory.CreateTempSubdirectory("thermi-sdk-").FullName;
        try
        {
            var onPath = Path.Combine(root, "tools");
            var programFiles = Path.Combine(root, "Program Files");
            var local = Path.Combine(root, "Local");
            string[] installs = [onPath, Path.Combine(programFiles, "thermi"), Path.Combine(local, "Programs", "thermi")];
            // The name each OS gives the program, as its installs write it.
            var name = OperatingSystem.IsWindows() ? "thermi-cli.exe" : "thermi-cli";
            var found = new List<string?>();
            for (var first = installs.Length; first >= 0; first--)
            {
                foreach (var (folder, at) in installs.Select((folder, at) => (folder, at)))
                {
                    Directory.CreateDirectory(folder);
                    var cli = Path.Combine(folder, name);
                    if (at >= first)
                    {
                        File.WriteAllText(cli, "");
                    }
                    else
                    {
                        File.Delete(cli);
                    }
                }

                found.Add(ThermiClient.Locate(onPath + Path.PathSeparator + Path.Combine(root, "none"), programFiles, local));
            }

            Assert.Equal([null, .. installs.Reverse().Select(folder => Path.Combine(folder, name))], found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TheSdkSaysHowToInstall()
    {
        var cli = new FakeCli(new CliEnd(0, "", ""));
        var none = new ThermiClient(() => null, cli);
        var notInstalled = await Assert.ThrowsAsync<ThermiException>(() => none.StatusAsync(TestContext.Current.CancellationToken));
        Assert.Equal(-1, notInstalled.ExitCode);
        Assert.Contains(ThermiClient.InstallUrl, notInstalled.Message, StringComparison.Ordinal);
        Assert.Empty(cli.Ran);

        var missing = AgentStatus.Said(AgentProblem.ServiceMissing);
        var service = new FakeCli(new CliEnd(3, "", $"{missing.Cause} {missing.Fix}\n"));
        var found = new ThermiClient(() => "thermi-cli.exe", service);
        Func<Task>[] calls =
        [
            () => found.ReadAsync(cancel: TestContext.Current.CancellationToken),
            () => found.SensorsAsync(cancel: TestContext.Current.CancellationToken),
            async () =>
            {
                await foreach (var _ in found.WatchAsync(cancel: TestContext.Current.CancellationToken))
                {
                    Assert.Fail("A watch of no service gave a line.");
                }
            },
        ];
        foreach (var call in calls)
        {
            var failed = await Assert.ThrowsAsync<ThermiException>(call);
            Assert.Equal(3, failed.ExitCode);
            Assert.Contains(ThermiClient.InstallUrl, failed.Message, StringComparison.Ordinal);
        }

        Assert.All(service.Ran, ran => Assert.Equal("thermi-cli.exe", ran));
    }

    /// <summary>A <c>thermi-cli</c> that ends as told, and keeps which program it was asked to start.</summary>
    private sealed class FakeCli(CliEnd end) : ICli
    {
        public List<string> Ran { get; } = [];

        public Task<CliEnd> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken cancel)
        {
            Ran.Add(exe);
            return Task.FromResult(end);
        }

        public async IAsyncEnumerable<string> LinesAsync(
            string exe, IReadOnlyList<string> args, Action<CliEnd> ended, [EnumeratorCancellation] CancellationToken cancel)
        {
            Ran.Add(exe);
            await Task.Yield();
            ended(end);
            yield break;
        }
    }
}
