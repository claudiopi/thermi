using System.Text.Json.Nodes;
using Thermi.Client;
using Thermi.Core.Agents;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Linux.Tests.Desktops;
using Thermi.Tests.Contract;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// AI agents and other programs on Linux (<c>docs/specs/006-linux/spec.md</c>, R2, R15 and R16): <c>thermi-cli</c> from
/// each shell, the SDK, and a settings file that crosses from Windows and back.
/// </summary>
public sealed class AgentTests
{
    // Each command of spec 003 that answers with no edit, and what it ends with when the service serves and no overlay runs.
    private static readonly (string[] Args, string? Schema, int Exit)[] Commands =
    [
        (["status", "--schema", "1"], "status", 0),
        (["read", "--schema", "1"], "read", 0),
        (["sensors", "--schema", "1"], "sensors", 0),
        (["monitors"], null, 0),
        (["config"], null, 0),
        (["looks"], null, 0),
        (["read", "no.such.metric"], null, 2),
    ];

    // How each shell runs a command and says the code it ended with, after what it printed.
    private static readonly (string Shell, string Code)[] Shells = [("bash", "$?"), ("zsh", "$?"), ("fish", "$status")];

    [Fact(Skip = "Waiting for its code")]
    public async Task TheCliAnswersOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        using var units = await UserUnits.StartAsync(session, cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);
        var cli = Built.Program("thermi-cli");

        foreach (var (shell, code) in Shells)
        {
            foreach (var (args, schema, exit) in Commands)
            {
                var said = await session.RunAsync(shell, ["-c", $"{cli} {string.Join(' ', args)}; echo \"ended {code}\""], cancel: cancel);
                var ended = said.Out.TrimEnd().Split('\n')[^1];
                Assert.Equal($"ended {exit}", ended);
                var json = said.Out[..^(ended.Length + 1)];
                Assert.Empty(schema is null ? [] : SchemaCheck.Findings(AgentSchemas.Current[schema], JsonNode.Parse(json)));
            }
        }

        var status = await new ThermiClient(cli).StatusAsync(cancel);
        Assert.Equal(("running", false), (status.Service, status.Overlay));
        var monitors = JsonNode.Parse((await session.RunAsync(cli, ["monitors"], cancel: cancel)).Out)!.AsArray();
        Assert.NotNull(Assert.Single(monitors)!["taskbar"]);
        await StartsAtSignInAsync(session, units, cli, cancel);
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task AWindowsFileLosesNothingOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        // A file Windows wrote, with hotkeys and tray icons, and an FPS item, which Linux does not read.
        var file = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Built.Root, "tests", "Thermi.Tests", "Configs", "v6.json"), cancel))!;
        file["overlays"]![0]!["items"]!.AsArray().Add(new JsonObject { ["id"] = Metrics.Fps.Id, ["metric"] = Metrics.Fps.Id });
        var windows = file.ToJsonString();
        Directory.CreateDirectory(Path.GetDirectoryName(Settings.PathOf(session))!);
        await File.WriteAllTextAsync(Settings.PathOf(session), windows, cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);

        // The overlay and the window read it and write it back, as a person's edit and an agent's do.
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var edited = await session.RunAsync(Built.Program("thermi-cli"), ["edit", """{"kind":"size","overlay":0,"size":1.25}"""], cancel: cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var written = JsonNode.Parse(await File.ReadAllTextAsync(Settings.PathOf(session), cancel))!;
        var status = JsonNode.Parse((await session.RunAsync(Built.Program("thermi-cli"), ["status", "--schema", "1"], cancel: cancel)).Out)!;

        Assert.Equal(0, edited.Exit);
        var expected = JsonNode.Parse(windows)!;
        expected["overlays"]![0]!["size"] = 1.25;
        Assert.True(JsonNode.DeepEquals(expected, written), $"The file lost or changed what it held:\n{written}");
        Assert.Null(ThermiConfigJson.Read(written.ToJsonString()).Problem);
        Assert.Contains(status["problems"]!.AsArray(), problem => (string?)problem!["code"] == "frames-not-read");
    }

    // start-with-windows keeps its name on Linux and enables the overlay's user unit, or disables it, never starting it.
    private static async Task StartsAtSignInAsync(OwnSession session, UserUnits units, string cli, CancellationToken cancel)
    {
        foreach (var (on, enabled) in new[] { ("on", "enabled"), ("off", "disabled") })
        {
            Assert.Equal(0, (await session.RunAsync(cli, ["start-with-windows", on], cancel: cancel)).Exit);
            Assert.Equal(enabled, await units.SystemctlAsync(["is-enabled", "thermi.service"], cancel));
            Assert.Equal("inactive", await units.SystemctlAsync(["is-active", "thermi.service"], cancel));
        }
    }
}
