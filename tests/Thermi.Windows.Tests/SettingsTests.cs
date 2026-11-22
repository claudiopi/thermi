using Thermi.Core;

namespace Thermi.Windows.Tests;

/// <summary>The user's config file: never overwritten while broken, and no move is lost.</summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "thermi-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Exception> _reported = [];

    private string File => Path.Combine(_folder, "config.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private Settings Loaded()
    {
        var settings = new Settings(new ConfigStore(File), _reported.Add);
        settings.Load();
        return settings;
    }

    [Fact]
    public void AMissingFileIsCreatedWithTheDefaults()
    {
        var settings = Loaded();

        Assert.Null(settings.Problem);
        Assert.True(System.IO.File.Exists(File));
    }

    [Fact]
    public void AMoveIsSaved()
    {
        Loaded().Move(new Position(10, 20));

        Assert.Equal(new Position(10, 20), Loaded().Config.Position);
    }

    [Fact]
    public void ABrokenFileIsNeverOverwrittenAndTheMoveIsSavedOnceItIsFixed()
    {
        var settings = Loaded();
        System.IO.File.WriteAllText(File, "{ \"items\": [ broken");
        settings.Load();

        settings.Move(new Position(10, 20));

        Assert.NotNull(settings.Problem);
        Assert.Equal("{ \"items\": [ broken", System.IO.File.ReadAllText(File));

        System.IO.File.WriteAllText(File, "{ \"version\": 1 }");
        settings.Load();

        Assert.Null(settings.Problem);
        Assert.Equal(new Position(10, 20), Loaded().Config.Position);
    }

    [Fact]
    public void AMoveThatCannotBeWrittenIsSavedOnTheNextGoodLoad()
    {
        var settings = Loaded();
        using (new FileStream(File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings.Move(new Position(10, 20));
        }

        Assert.Single(_reported);
        Assert.Null(Loaded().Config.Position);

        settings.Load();

        Assert.Equal(new Position(10, 20), Loaded().Config.Position);
    }

    [Fact]
    public void WhileBrokenTheLastGoodConfigStays()
    {
        var settings = Loaded();
        settings.Move(new Position(10, 20));
        System.IO.File.WriteAllText(File, "not json");

        settings.Load();

        Assert.NotNull(settings.Problem);
        Assert.Equal(new Position(10, 20), settings.Config.Position);
    }
}
