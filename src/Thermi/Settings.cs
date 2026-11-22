using Thermi.Core;

namespace Thermi;

/// <summary>
/// The overlay's settings as the user's file holds them. While the file cannot be used, the last good
/// config stays on screen and the file is never written: it holds the user's edits. A move made meanwhile,
/// or one that could not be written, is saved once the file works again.
/// </summary>
internal sealed class Settings(ConfigStore store, Action<Exception> report)
{
    private Position? _unsaved;

    public OverlayConfig Config { get; private set; } = new();

    /// <summary>Why the file cannot be used, or null when it can.</summary>
    public string? Problem { get; private set; }

    public void Load()
    {
        var (config, problem) = store.Load();
        Problem = problem;
        if (problem is not null)
        {
            return;
        }

        Config = config;
        if (_unsaved is { } position)
        {
            Config = Config with { Position = position };
            Save();
        }
    }

    public void Move(Position position)
    {
        Config = Config with { Position = position };
        _unsaved = position;
        if (Problem is null)
        {
            Save();
        }
    }

    private void Save()
    {
        try
        {
            store.Save(Config);
            _unsaved = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report(ex);
        }
    }
}
