using Thermi.App.SettingsFile;
using Thermi.Core.Settings;

namespace Thermi.Linux.Tests;

/// <summary>A session's settings file, where thermi, its window and <c>thermi-cli</c> read it: under the session's <c>~/.config</c>.</summary>
internal static class Settings
{
    public static string PathOf(OwnSession session) => ConfigStore.PathIn(session.Variables["XDG_CONFIG_HOME"]);

    public static Task WriteAsync(OwnSession session, ThermiConfig config, CancellationToken cancel)
    {
        var file = PathOf(session);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        return File.WriteAllTextAsync(file, ThermiConfigJson.Write(config), cancel);
    }

    public static async Task<ThermiConfig> ReadAsync(OwnSession session, CancellationToken cancel)
    {
        var (config, problem) = ThermiConfigJson.Read(await File.ReadAllTextAsync(PathOf(session), cancel).ConfigureAwait(false));
        return problem is null ? config : throw new InvalidOperationException($"The settings did not read: {problem}");
    }
}
