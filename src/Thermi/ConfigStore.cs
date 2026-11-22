using Thermi.Core;

namespace Thermi;

internal sealed class ConfigStore(string path)
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "thermi", "config.json");

    public string FilePath { get; } = path;

    /// <summary>
    /// A missing file is created with the defaults, so there is something to edit. A file that cannot be
    /// opened or understood gives the defaults and the reason why.
    /// </summary>
    public (OverlayConfig Config, string? Problem) Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var defaults = new OverlayConfig();
                Save(defaults);
                return (defaults, null);
            }

            return OverlayConfigJson.Read(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (new OverlayConfig(), $"The config could not be opened ({ex.Message}).");
        }
    }

    /// <summary>Writes beside the file and then swaps it in, so a crash never leaves half a config.</summary>
    public void Save(OverlayConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var next = FilePath + ".next";
        File.WriteAllText(next, OverlayConfigJson.Write(config));
        File.Move(next, FilePath, overwrite: true);
    }
}
