using System.Text.Json.Nodes;

namespace Thermi.MacOS.Tests;

/// <summary>
/// A recording of what a Mac's IOKit answered, as <c>thermi-sensors --raw</c> writes it and <c>--replay</c> reads it: a
/// folder for each second, from <c>0</c>, each with what the SMC answered by its keys, <c>smc.json</c>, and what the HID
/// sensors, IOReport and IOKit's registry answered, each in a file of its own. A test changes a copy as a Mac would
/// differ.
/// </summary>
internal static class Raw
{
    /// <summary>A copy of the recording <paramref name="name"/> in the session's own folder, for a test to change.</summary>
    public static string Copy(OwnSession session, string name)
    {
        var from = Built.Recording(name);
        var to = Path.Combine(session.Temporary, name);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return to;
    }

    /// <summary>
    /// The SMC's keys that <paramref name="gone"/> picks, gone from the second <paramref name="from"/> on, as a key read once
    /// and then no more.
    /// </summary>
    public static Task VanishAsync(string recording, Func<string, bool> gone, int from, CancellationToken cancel) =>
        EachSecondAsync(recording, "smc.json", (second, smc) =>
        {
            foreach (var key in second < from ? [] : smc.Select(pair => pair.Key).Where(gone).ToList())
            {
                smc.Remove(key);
            }
        }, cancel);

    /// <summary>
    /// The Mac's names written where IOKit's registry gives them: its host's and its person's names, its serial number
    /// and its adapters' MAC addresses.
    /// </summary>
    public static Task NameTheMacAsync(string recording, string[] names, CancellationToken cancel) =>
        EachSecondAsync(recording, "registry.json", (_, registry) =>
        {
            registry["IOPlatformExpertDevice"] = new JsonObject { ["IOPlatformSerialNumber"] = names[2], ["model"] = names[0] };
            registry["IOEthernetInterface"] = new JsonObject { ["IOMACAddress"] = names[3], ["BSD Name"] = "en0" };
            registry["IOUserClientCreator"] = $"pid 1, {names[1]}";
        }, cancel);

    private static async Task EachSecondAsync(string recording, string file, Action<int, JsonObject> change, CancellationToken cancel)
    {
        foreach (var folder in Directory.EnumerateDirectories(recording))
        {
            var path = Path.Combine(folder, file);
            var read = JsonNode.Parse(await File.ReadAllTextAsync(path, cancel).ConfigureAwait(false))!.AsObject();
            change(int.Parse(Path.GetFileName(folder), System.Globalization.CultureInfo.InvariantCulture), read);
            await File.WriteAllTextAsync(path, read.ToJsonString(), cancel).ConfigureAwait(false);
        }
    }
}
