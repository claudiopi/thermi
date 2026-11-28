using System.Globalization;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Tray;
using Thermi.Core.Translations;
using Thermi.Core.Updates;

namespace Thermi.Tests.Agents;

/// <summary>What status and About say of the version, the channel and the updates on Linux, from the repository's file the packages wrote.</summary>
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
            Os = HostOs.Linux,
            About = SystemRepository.About("1.2.3", installed: true, aptSources, dnfRepo),
        };

        var answer = AgentStatus.Of(facts);

        Assert.Equal(("1.2.3", channel, "system"), (answer.Version, answer.Channel, answer.Updates));
    }

    // About on Linux says what status says: a build the packages installed updates with the system's, on the channel the
    // repository names; one run from the repo updates from nothing and names no channel, whatever repository the PC has.
    // Each language says it.
    [Theory] // Rows: installed on beta, installed on stable, and a build from the repo on a PC with thermi's beta repository.
    [InlineData(true, "Suites: beta\n", UpdateLineKind.FromSystem, true)]
    [InlineData(true, null, UpdateLineKind.FromSystem, false)]
    [InlineData(false, "Suites: beta\n", UpdateLineKind.NotUpdating, false)]
    public void AboutOnLinuxSaysWhatStatusSays(bool installed, string? aptSources, UpdateLineKind kind, bool beta)
    {
        var about = SystemRepository.About("1.2.3", installed, aptSources, dnfRepo: null);
        var line = SystemRepository.Line(about);
        var status = AgentStatus.Of(new AgentFacts(ServiceState.Running, Installed: true, Taken: false, Latest: null, Settings: null)
        {
            Os = HostOs.Linux,
            About = about,
        });

        Assert.Equal((kind, beta), (line.Kind, line.Beta));
        Assert.Equal((line.Kind == UpdateLineKind.FromSystem, line.Beta), (status.Updates == "system", status.Channel == "beta"));
        Assert.All(Texts.Languages, language =>
            Assert.NotEmpty(UpdateStatus.Say(line, Texts.Of(language), CultureInfo.GetCultureInfo(language), TimeZoneInfo.Utc)));
    }
}
