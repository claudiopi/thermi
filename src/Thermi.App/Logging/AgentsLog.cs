using System.Text;

namespace Thermi.App.Logging;

/// <summary>
/// The agents' log, <see cref="ThermiLog.AgentsPath"/> and the files it rolled into, read back for the settings window's
/// AI agents section: each file from its end, shared, as the agents' processes may be writing it.
/// </summary>
internal static class AgentsLog
{
    // Far more than the few changes the section lists, and far less than a whole file.
    private const int Tail = 64 * 1024;

    /// <summary>The file written last, the one a person opens to read what agents did, or where the first will be.</summary>
    public static string Newest(string path) => Files(path).LastOrDefault() ?? path;

    public static IReadOnlyList<string> Lines(string path) => [.. Files(path).SelectMany(TailOf)];

    // Serilog rolls thermi-agents.log into thermi-agents_001.log and on, beside it.
    private static IEnumerable<string> Files(string path) =>
        Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles($"{Path.GetFileNameWithoutExtension(path)}*{Path.GetExtension(path)}")
                .OrderBy(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
            : [];

    private static IEnumerable<string> TailOf(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - Tail);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = reader.ReadToEnd().Split('\n');
            // A read from the middle of a file starts in the middle of a line.
            return start > 0 ? lines[1..] : lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
