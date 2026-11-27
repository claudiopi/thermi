using Thermi.Core.Agents;
using Thermi.Core.Tray;
using Thermi.Core.Updates;

namespace Thermi.Tests.Agents;

/// <summary>What status says of the version, the channel and the updates on Linux, from the repository's file the packages wrote.</summary>
public sealed class StatusAboutTests
{
    [Theory]
    [InlineData("Types: deb\nURIs: https://u/v1/linux/apt\nSuites: beta\nComponents: main\n", null, "beta")]
    [InlineData("Types: deb\nURIs: https://u/v1/linux/apt\nSuites: stable\nComponents: main\n", null, "stable")]
    [InlineData(null, "[thermi]\nname=thermi\nbaseurl=https://u/v1/linux/rpm/beta/\ngpgcheck=1\n", "beta")]
    [InlineData("Types: deb\nURIs: https://u/v1/linux/apt\n", null, "stable")]
    public void StatusSaysTheChannelTheRepositoryNames(string? aptSources, string? dnfRepo, string channel)
    {
        var facts = new AgentFacts(ServiceState.Running, Installed: true, Taken: false, Latest: null, Settings: null)
        {
            Os = AgentOs.Linux,
            About = new AboutFacts("1.2.3", SystemRepository.Channel(aptSources, dnfRepo), "system"),
        };

        var answer = AgentStatus.Of(facts);

        Assert.Equal(("1.2.3", channel, "system"), (answer.Version, answer.Channel, answer.Updates));
    }
}
