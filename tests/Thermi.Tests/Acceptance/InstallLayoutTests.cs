using Thermi.Core.Install;
using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

/// <summary>Spec 004: what <c>thermi-sensors --adopt</c>, <c>--undo</c> and <c>--remove</c> read of thermi's install folder.</summary>
public sealed class InstallLayoutTests
{
    private const string Top = @"users\ana\Programs\thermi";

    // From the MSI's folder, the script's copy or a version's, in the install folder; anything else is refused whole.
    [Theory]
    [InlineData(Top + @"\setup", true)]
    [InlineData(Top + @"\SETUP\", true)]
    [InlineData(Top + @"\install-ab12", true)]
    [InlineData(Top + @"\0.8.0", true)]
    [InlineData(Top + @"\elsewhere", false)]
    [InlineData(@"users\ana\Downloads\setup", false)]
    public void AnInstallRunsOnlyFromAFolderOfThermis(string own, bool installs) =>
        Assert.Equal(installs, InstallLayout.InstallsFrom(own, Top));

    // A removal takes update\, the script's copies, the versions and the launchers, and leaves the MSI's folder to the MSI,
    // and anything else at the top to its owner.
    [Theory]
    [InlineData("update", true, true)]
    [InlineData("install-ab12", true, true)]
    [InlineData("0.9.5", true, true)]
    [InlineData("setup", true, false)]
    [InlineData("thermi.exe", false, true)]
    [InlineData("thermi-sensors.exe", false, true)]
    [InlineData("update", false, false)]
    public void ARemovalTakesOnlyThermisOwnEntries(string name, bool folder, bool removed) =>
        Assert.Equal(removed, InstallLayout.Removes(name, folder));

    // The service's image path, TOP for the install folder, and the version an install lays down: the version the service
    // runs, and the later one the install leaves running; none for a service run from elsewhere or a build before versions.
    [Theory]
    [InlineData(@"""TOP\0.9.5\thermi-sensors.exe""", "0.9.0", "0.9.5", "0.9.5")]
    [InlineData(@"""TOP\0.9.0\thermi-sensors.exe"" --elevated", "0.9.0", "0.9.0", null)]
    [InlineData(@"TOP\0.9.0\thermi-sensors.exe", "0.9.5", "0.9.0", null)]
    [InlineData(@"""TOP\thermi-sensors.exe""", "0.9.0", null, null)]
    [InlineData(@"""TOP\..\other\0.9.5\thermi-sensors.exe""", "0.9.0", null, null)]
    [InlineData("", "0.9.0", null, null)]
    [InlineData(null, "0.9.0", null, null)]
    public void AnInstallLeavesOnlyALaterVersionRunning(string? image, string version, string? running, string? later)
    {
        var path = image?.Replace("TOP", Top, StringComparison.Ordinal);

        Assert.Equal(running, InstallLayout.Running(Top, path)?.ToString());
        Assert.Equal(later, InstallLayout.Later(Top, path, ReleaseVersion.Parse(version)!)?.ToString());
    }
}
