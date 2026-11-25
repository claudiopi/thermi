using Thermi.Core.Settings;

namespace Thermi.Windows.Tests.Updates;

/// <summary>What a rollback across a change of the settings' format leaves the user, spec 004.</summary>
public sealed class SettingsRollbackTests : IDisposable
{
    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // A10: a newer version wrote the file, keeping this version's copy beside it, and the PC went back a version.
    [Fact]
    public async Task SettingsSurviveARollback()
    {
        var mine = $$"""{ "version": {{ThermiConfig.CurrentVersion}}, "overlays": [ { "name": "Floating" } ] }""";
        var newer = $$"""{ "version": {{ThermiConfig.CurrentVersion + 1}}, "overlays": [ { "name": "Docked" } ] }""";
        var folder = Path.GetDirectoryName(_folder.File)!;
        var cancel = TestContext.Current.CancellationToken;
        _folder.Write(newer);
        await File.WriteAllTextAsync(Path.Combine(folder, $"config.v{ThermiConfig.CurrentVersion}-2026-11-20-101010.json"), mine, cancel);

        var settings = await _folder.LoadedAsync();

        Assert.Null(settings.Problem);
        Assert.Equal(ThermiConfigJson.Write(ThermiConfigJson.Read(mine).Config), _folder.Text());
        var kept = Assert.Single(_folder.Files, name => name.StartsWith($"config.v{ThermiConfig.CurrentVersion + 1}-", StringComparison.Ordinal));
        Assert.Equal(newer, await File.ReadAllTextAsync(Path.Combine(folder, kept), cancel));
    }
}
