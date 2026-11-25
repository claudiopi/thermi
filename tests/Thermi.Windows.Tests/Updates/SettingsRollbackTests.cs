using Thermi.Core.Settings;
using Thermi.Windows.Overlay;

namespace Thermi.Windows.Tests.Updates;

/// <summary>What a rollback across a change of the settings' format leaves the user, spec 004.</summary>
public sealed class SettingsRollbackTests : IDisposable
{
    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // A10: a newer version wrote the file, keeping this version's copy beside it, and the PC went back a version. A process
    // an update left on the older version, as an agent's thermi-cli mcp, finds the same newer file without a rollback to
    // it: it says the file is newer and writes nothing.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingsSurviveARollback(bool rolledBack)
    {
        var mine = $$"""{ "version": {{ThermiConfig.CurrentVersion}}, "overlays": [ { "name": "Floating" } ] }""";
        var newer = $$"""{ "version": {{ThermiConfig.CurrentVersion + 1}}, "overlays": [ { "name": "Docked" } ] }""";
        var folder = Path.GetDirectoryName(_folder.File)!;
        var cancel = TestContext.Current.CancellationToken;
        _folder.Write(newer);
        await File.WriteAllTextAsync(Path.Combine(folder, $"config.v{ThermiConfig.CurrentVersion}-2026-11-20-101010.json"), mine, cancel);
        using var store = new ConfigStore(_folder.File, _folder.Clock, installed: () => rolledBack);

        var read = await store.ReadAsync();

        string[] kept = rolledBack ? [newer] : [];
        Assert.Equal(rolledBack ? null : ConfigProblemKind.Newer, read.Problem?.Kind);
        Assert.Equal(rolledBack ? ThermiConfigJson.Write(ThermiConfigJson.Read(mine).Config) : newer, _folder.Text());
        Assert.Equal(
            kept,
            _folder.Files.Where(name => name.StartsWith($"config.v{ThermiConfig.CurrentVersion + 1}-", StringComparison.Ordinal))
                .Select(name => File.ReadAllText(Path.Combine(folder, name))));
    }
}
