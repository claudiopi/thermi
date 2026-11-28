using System.Diagnostics;

namespace Thermi.MacOS.Tests;

/// <summary>
/// A session of a test's own on the Mac: a home with a <c>~/Library</c> of its own, where thermi keeps its settings, logs
/// and agents' plists, and the socket thermi and <c>thermi-cli</c> read, which the test serves; none of them the person's.
/// Every process a macOS test starts starts here, with the session's environment alone and nothing of the test process's
/// but its PATH, after the build's programs, and where .NET runs from; a repo rule holds it. A home of its own keeps
/// nothing off the screen or out of the person's launchd, so a plain session starts only what <see cref="PlainSession"/>
/// lets it, and all else starts only in a session made for tests (<see cref="OnScreen"/>): anywhere else the test skips,
/// and on the CI, whose runner is one, it fails. Ending the session ends all it started, takes out of launchd every agent
/// loaded from its home, and deletes its folders.
/// </summary>
internal sealed class OwnSession : IAsyncDisposable
{
    /// <summary>
    /// What makes a session for tests: the CI's macOS runner and the test Mac's user of their own set it to the name of
    /// the user the tests run as, which no person's own account sets.
    /// </summary>
    public const string MadeForTests = "THERMI_TEST_SESSION";

    private readonly DirectoryInfo _folder;
    private readonly bool _onScreen;
    private readonly List<Process> _started = [];

    public OwnSession()
        : this(onScreen: false)
    {
    }

    private OwnSession(bool onScreen)
    {
        _onScreen = onScreen;
        _folder = Directory.CreateTempSubdirectory("thermi-session-");
        Home = _folder.CreateSubdirectory("home").FullName;
        Temporary = _folder.CreateSubdirectory("tmp").FullName;
    }

    /// <summary>Whether these tests run in a session made for tests, as <see cref="MadeForTests"/> names its user.</summary>
    public static bool ForTests => Environment.GetEnvironmentVariable(MadeForTests) is { Length: > 0 } user && user == Environment.UserName;

    public string Home { get; }

    public string Temporary { get; }

    /// <summary>
    /// The socket the session's programs read, in place of the service's under <c>~/Library</c>: macOS takes a socket's
    /// path of at most 104 bytes, past which a home in the temporary folder would take it.
    /// </summary>
    public string Socket => Path.Combine(_folder.FullName, "sensors.sock");

    /// <summary>The environment every process of the session starts with.</summary>
    public IReadOnlyDictionary<string, string> Variables
    {
        get
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = $"{AppContext.BaseDirectory.TrimEnd('/')}:{Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin"}",
                ["HOME"] = Home,
                ["TMPDIR"] = Temporary,
                ["LANG"] = "en_US.UTF-8",
                ["THERMI_SENSORS_SOCKET"] = Socket,
            };
            // The build's programs need .NET, which a Mac may hold where only this variable says.
            if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnet)
            {
                variables["DOTNET_ROOT"] = dotnet;
            }

            return variables;
        }
    }

    /// <summary>
    /// A session that may start anything, thermi on the screen and its agents in launchd among it: only in a session made
    /// for tests. Anywhere else the test skips, but on the CI, whose runner is one, it fails: a job whose every such test
    /// skipped would pass.
    /// </summary>
    public static OwnSession OnScreen()
    {
        var why = $"It shows thermi or loads its agents, which only a session made for tests may: {MadeForTests} names its user.";
        Assert.True(ForTests || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true", why);
        Assert.SkipUnless(ForTests, why);
        return new OwnSession(onScreen: true);
    }

    /// <summary>
    /// Starts <paramref name="program"/> in the session, with <paramref name="environment"/> over the session's own, where a
    /// variable given empty is left out; its output is read as it comes, and its input is the test's.
    /// </summary>
    public SessionProcess Start(string program, IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null)
    {
        string[] arguments = [.. args];
        if (!_onScreen && !PlainSession.MayStart(program, arguments, AppContext.BaseDirectory.TrimEnd('/'), Path.Combine(Built.Root, "scripts")))
        {
            throw new InvalidOperationException($"{program} could show on the screen or load agents: start it in a session of OwnSession.OnScreen().");
        }

        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Home,
        };
        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment.Clear();
        foreach (var (name, value) in Variables.Concat(environment ?? new Dictionary<string, string>()))
        {
            start.Environment[name] = value;
        }

        foreach (var (name, _) in environment?.Where(variable => variable.Value.Length == 0) ?? [])
        {
            start.Environment.Remove(name);
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        _started.Add(process);
        return new SessionProcess(process);
    }

    /// <summary>Runs <paramref name="program"/> in the session to its end: its exit code, and what it wrote to each stream.</summary>
    public Task<Ran> RunAsync(
        string program, IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancel = default) =>
        Start(program, args, environment).ToEndAsync(cancel);

    /// <summary>
    /// The person's two launchd domains thermi's agents load into: their graphical session's, <c>gui/&lt;uid&gt;</c>, the
    /// overlay's, and the one their SSH sessions share, <c>user/&lt;uid&gt;</c>, the service's.
    /// </summary>
    public async Task<(string Gui, string User)> DomainsAsync(CancellationToken cancel)
    {
        var uid = (await RunAsync("id", ["-u"], cancel: cancel).ConfigureAwait(false)).Out.Trim();
        return ($"gui/{uid}", $"user/{uid}");
    }

    /// <summary>What a program of the person's reads through the SDK, as the tests' own program in the session says it (<see cref="TheSdk"/>).</summary>
    public async Task<(string Service, bool Overlay)> SdkStatusAsync(CancellationToken cancel) =>
        TheSdk.Said(await RunAsync(Built.Program("Thermi.MacOS.Tests"), [TheSdk.Status], cancel: cancel).ConfigureAwait(false));

    public async ValueTask DisposeAsync()
    {
        await EndStartedAsync().ConfigureAwait(false);

        // launchd starts again an agent loaded from the session's home, after the session as before it.
        var agents = Path.Combine(Home, "Library", "LaunchAgents");
        if (_onScreen && Directory.Exists(agents))
        {
            var (gui, user) = await DomainsAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var plist in Directory.EnumerateFiles(agents, "*.plist"))
            {
                // An agent is in one domain or the other; launchctl refuses the one it is not in, and that is all.
                await RunAsync("launchctl", ["bootout", gui, plist]).ConfigureAwait(false);
                await RunAsync("launchctl", ["bootout", user, plist]).ConfigureAwait(false);
            }

            await EndStartedAsync().ConfigureAwait(false);
        }

        _folder.Delete(recursive: true);
    }

    // Each process the session started ends, the last started first, and lets go of what it held.
    private async Task EndStartedAsync()
    {
        foreach (var process in Enumerable.Reverse(_started))
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            process.Dispose();
        }

        _started.Clear();
    }
}
