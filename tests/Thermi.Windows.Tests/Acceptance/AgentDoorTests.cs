using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Thermi.Core.Agents;
using Thermi.Windows.Tests.Processes;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// <c>thermi-cli</c>, the console door to the same program (<c>docs/adr/0013-agents-cli-and-mcp.md</c>): every shell waits
/// for it, captures its JSON and gets its exit code, which a windowed <c>thermi.exe</c> gives none of them.
/// </summary>
public sealed class AgentDoorTests : IDisposable
{
    private static readonly string Cli = Path.Combine(AppContext.BaseDirectory, "thermi-cli.exe");

    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    public enum Shell
    {
        PowerShell,
        Cmd,
        GitBash,
    }

    // Each shell runs monitors, which reads no settings, into a file, then the door with no command, whose code it exits with.
    [Theory]
    [InlineData(Shell.PowerShell)]
    [InlineData(Shell.Cmd)]
    [InlineData(Shell.GitBash)]
    public async Task TheCliWaitsAndAnswersInEveryShell(Shell shell)
    {
        var folder = Path.GetDirectoryName(_folder.File)!;
        var captured = Path.Combine(folder, "monitors.json");
        var (exe, args) = shell switch
        {
            Shell.PowerShell => ("pwsh", new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                $"& '{Cli}' monitors > '{captured}'; if ($LASTEXITCODE) {{ exit 9 }}; & '{Cli}'; exit $LASTEXITCODE",
            }),
            Shell.Cmd => ("cmd", ["/d", "/c", Script(folder, "door.cmd",
                $"@\"{Cli}\" monitors > \"{captured}\"\r\n@if errorlevel 1 exit /b 9\r\n@\"{Cli}\"\r\n@exit /b %errorlevel%\r\n")]),
            Shell.GitBash => (GitBash(), [Script(folder, "door.sh",
                $"'{Posix(Cli)}' monitors > '{Posix(captured)}' || exit 9\n'{Posix(Cli)}'\nexit $?\n").Replace('\\', '/')]),
            _ => throw new ArgumentOutOfRangeException(nameof(shell)),
        };

        var (code, said) = await DesktopProcess.RunAsync(exe, args, TimeSpan.FromSeconds(60), _folder.Profile);

        Assert.Equal(2, code);
        Assert.Contains("monitors", said, StringComparison.Ordinal);
        var answer = JsonNode.Parse(await File.ReadAllTextAsync(captured, TestContext.Current.CancellationToken))!;
        Assert.Equal(AgentSchema.Current, (int)answer["schema"]!);
        Assert.NotEmpty(answer["monitors"]!.AsArray());

        // The door runs the agents' commands alone, and its help goes where no JSON is read.
        Assert.Equal(2, (await DesktopProcess.RunAsync(Cli, ["elevated", "start-sensors"], TimeSpan.FromSeconds(60), _folder.Profile)).Code);
        var (helped, help) = await DesktopProcess.RunAsync(Cli, ["--help"], TimeSpan.FromSeconds(60), _folder.Profile);
        Assert.Equal(0, helped);
        Assert.Contains("monitors", help, StringComparison.Ordinal);
    }

    // The built door serves the tools over its stdio, on a desktop of its own; monitors reads no settings.
    [Fact]
    public async Task TheCliServesItsToolsOverMcp()
    {
        using var process = DesktopProcess.Start(Cli, ["mcp"], _folder.Profile, talks: true);
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(process.Input.BaseStream, process.Output.BaseStream),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(13, (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Count);
        var monitors = await client.CallToolAsync("monitors", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(monitors.IsError ?? false);
    }

    private static string Script(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Posix(string path) => path.Replace('\\', '/');

    // Git for Windows' bash, where its installer puts it, as on the CI's runners.
    private static string GitBash() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
}
