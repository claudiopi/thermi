using System.Text.Json.Nodes;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>How the person's settings find the extension in place of thermi's as the test's Shell starts.</summary>
public enum ExtensionStart
{
    /// <summary>On, as GNOME's Extensions app or thermi turned it on.</summary>
    On,

    /// <summary>Off, as GNOME leaves a copy a package put in place, which no one turned on.</summary>
    NeverOn,

    /// <summary>Off, as the person turned it off in GNOME's Extensions app.</summary>
    TurnedOff,
}

/// <summary>
/// The GNOME Shell extensions a test's Shell loads at its start: the tests' own, and thermi's or another in its place.
/// Extensions copied in a running Shell load only at the next sign-in, so they are in place before it starts.
/// </summary>
internal static class TestShell
{
    /// <summary>The bus name the tests' extension takes once the Shell is up and out of its overview.</summary>
    public const string ReadyName = "org.thermi.TestShell";

    /// <summary>The path of the tests' extension's object, whose <c>Frame</c> answers once the stage painted a frame.</summary>
    public const string ObjectPath = "/org/thermi/TestShell";

    private const string Uuid = "thermi-tests@thermi";

    // Unsafe mode lets the tests screenshot the Shell and run its input for tests through Eval. The headless Shell starts
    // in its overview, which would cover every window. Frame answers once the stage painted a frame, which lays it out
    // first: the Shell's screenshot paints the stage as last laid out, and would draw a picture just sent in the old one's
    // size and place.
    private const string Code = $$"""
        import Gio from 'gi://Gio';
        import * as Main from 'resource:///org/gnome/shell/ui/main.js';
        import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

        const XML = '<node><interface name="org.thermi.TestShell"><method name="Frame"/></interface></node>';

        export default class extends Extension {
            FrameAsync(args, invocation) {
                const painted = global.stage.connect('after-paint', () => {
                    global.stage.disconnect(painted);
                    invocation.return_value(null);
                });
                global.stage.queue_redraw();
            }

            enable() {
                this._service = Gio.DBusExportedObject.wrapJSObject(XML, this);
                this._service.export(Gio.DBus.session, '{{ObjectPath}}');
                global.context.unsafe_mode = true;
                // GNOME 50 may show it again once its start is complete; the tests never use it.
                this._shown = Main.overview.connect('shown', () => Main.overview.hide());
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
                this._service.unexport();
                Main.overview.disconnect(this._shown);
            }
        }
        """;

    /// <summary>thermi's extension as the package installs it.</summary>
    public static string Thermi { get; } = Path.Combine(Built.Root, "src", "Thermi.Gnome");

    /// <summary>
    /// Puts the tests' extension and <paramref name="extension"/>, a folder with its <c>metadata.json</c>, none when null, where
    /// the session's Shell loads them, and enables the tests' and, as <paramref name="start"/> says, the other.
    /// </summary>
    public static async Task InstallAsync(OwnSession session, string? extension, ExtensionStart start, CancellationToken cancel)
    {
        List<string> disabled = [];
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

            if (start != ExtensionStart.NeverOn)
            {
                (start == ExtensionStart.On ? enabled : disabled).Add(uuid);
            }
        }

        foreach (var (key, uuids) in new[] { ("enabled-extensions", enabled), ("disabled-extensions", disabled) })
        {
            var set = await session.RunAsync(
                "gsettings", ["set", "org.gnome.shell", key, $"[{string.Join(", ", uuids.Select(uuid => $"'{uuid}'"))}]"], cancel: cancel)
                .ConfigureAwait(false);
            _ = set.Exit == 0 ? 0 : throw new InvalidOperationException($"The Shell's {key} were not set: {set.Error}");
        }

        // Mutter takes a fractional scale, as 150 %, only with its framebuffers scaled, which GNOME's Settings turns on when a
        // person picks one. Without animations, a window is on screen as drawn once it is mapped. The Shell reaches no
        // network: installing from extensions.gnome.org fails at once, through a proxy that is not there. No banner shows over
        // the top bar, as the unsafe mode's would where an overlay snaps: the notices are watched as the Shell gets them.
        foreach (var (schema, key, value) in new[]
        {
            ("org.gnome.mutter", "experimental-features", "['scale-monitor-framebuffer']"),
            ("org.gnome.desktop.interface", "enable-animations", "false"),
            ("org.gnome.desktop.notifications", "show-banners", "false"),
            ("org.gnome.system.proxy", "mode", "'manual'"),
            ("org.gnome.system.proxy.http", "host", "'127.0.0.1'"),
            ("org.gnome.system.proxy.http", "port", "9"),
            ("org.gnome.system.proxy.https", "host", "'127.0.0.1'"),
            ("org.gnome.system.proxy.https", "port", "9"),
        })
        {
            var setting = await session.RunAsync("gsettings", ["set", schema, key, value], cancel: cancel).ConfigureAwait(false);
            _ = setting.Exit == 0 ? 0 : throw new InvalidOperationException($"The Shell's {schema} {key} was not set: {setting.Error}");
        }
    }
}
