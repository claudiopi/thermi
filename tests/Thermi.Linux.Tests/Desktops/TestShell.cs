using System.Text.Json.Nodes;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// The GNOME Shell extensions a test's Shell loads at its start: the tests' own, and thermi's or another in its place.
/// Extensions copied in a running Shell load only at the next sign-in, so they are in place before it starts.
/// </summary>
internal static class TestShell
{
    /// <summary>The bus name the tests' extension takes once the Shell is up and out of its overview.</summary>
    public const string ReadyName = "org.thermi.TestShell";

    private const string Uuid = "thermi-tests@thermi";

    // Unsafe mode lets the tests screenshot the Shell and run its input for tests through Eval. The headless Shell starts
    // in its overview, which would cover every window.
    private const string Code = """
        import Gio from 'gi://Gio';
        import * as Main from 'resource:///org/gnome/shell/ui/main.js';
        import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

        export default class extends Extension {
            enable() {
                global.context.unsafe_mode = true;
                const own = () => { this._name = Gio.bus_own_name(Gio.BusType.SESSION, 'org.thermi.TestShell', Gio.BusNameOwnerFlags.NONE, null, null, null); };
                const out = () => {
                    if (!Main.overview.visible) { own(); return; }
                    const hidden = Main.overview.connect('hidden', () => { Main.overview.disconnect(hidden); own(); });
                    Main.overview.hide();
                };
                if (Main.layoutManager._startingUp) {
                    const started = Main.layoutManager.connect('startup-complete', () => { Main.layoutManager.disconnect(started); out(); });
                } else {
                    out();
                }
            }

            disable() {
                if (this._name) Gio.bus_unown_name(this._name);
            }
        }
        """;

    /// <summary>thermi's extension as the package installs it.</summary>
    public static string Thermi { get; } = Path.Combine(Built.Root, "src", "Thermi.Gnome");

    /// <summary>
    /// Puts the tests' extension and <paramref name="extension"/>, a folder with its <c>metadata.json</c>, none when null, where
    /// the session's Shell loads them, and enables both.
    /// </summary>
    public static async Task InstallAsync(OwnSession session, string? extension, CancellationToken cancel)
    {
        var extensions = Path.Combine(session.Variables["XDG_DATA_HOME"], "gnome-shell", "extensions");
        var tests = Directory.CreateDirectory(Path.Combine(extensions, Uuid)).FullName;
        await File.WriteAllTextAsync(Path.Combine(tests, "extension.js"), Code, cancel).ConfigureAwait(false);
        // The tests' extension loads on the Shell that runs, whichever thermi's lists.
        var running = await session.RunAsync("gnome-shell", ["--version"], cancel: cancel).ConfigureAwait(false);
        var major = running.Out.Trim().Split(' ')[^1].Split('.')[0];
        var metadata = new JsonObject
        {
            ["uuid"] = Uuid,
            ["name"] = "thermi's tests",
            ["description"] = "thermi's tests",
            ["shell-version"] = new JsonArray(major),
        };
        await File.WriteAllTextAsync(Path.Combine(tests, "metadata.json"), metadata.ToJsonString(), cancel).ConfigureAwait(false);
        List<string> enabled = [Uuid];
        if (extension is not null)
        {
            var uuid = (string)JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(extension, "metadata.json"), cancel).ConfigureAwait(false))!["uuid"]!;
            var target = Directory.CreateDirectory(Path.Combine(extensions, uuid)).FullName;
            foreach (var file in Directory.EnumerateFiles(extension).Where(file => Path.GetExtension(file) is ".js" or ".json" or ".css"))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }

            enabled.Add(uuid);
        }

        var set = await session.RunAsync(
            "gsettings", ["set", "org.gnome.shell", "enabled-extensions", $"[{string.Join(", ", enabled.Select(uuid => $"'{uuid}'"))}]"], cancel: cancel)
            .ConfigureAwait(false);
        if (set.Exit != 0)
        {
            throw new InvalidOperationException($"The Shell's extensions were not enabled: {set.Error}");
        }
    }
}
