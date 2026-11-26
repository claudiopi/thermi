using System.Collections;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Thermi.Windows.Tests.Processes;

/// <summary>
/// A process a test starts, always on a desktop of the tests' own, so neither it nor any process it starts, which take
/// their parent's desktop, ever shows a window, a message box or a crash dialog on the user's screen. The desktop is the
/// one given, which a test's windows share, or one of its own. Its standard streams are pipes the test reads and writes,
/// and its environment is the test's with the variables given, so no other test's process sees them.
/// </summary>
internal sealed class DesktopProcess : IDisposable
{
    // Handles a process inherits are every inheritable one of the test process: the pipes' ends are made and handed over
    // under one lock, so another start never takes them too and keeps a pipe open past its process.
    private static readonly Lock Starting = new();

    private readonly AnonymousPipeServerStream _input;
    private readonly AnonymousPipeServerStream _output;
    private readonly AnonymousPipeServerStream _error;
    private readonly OwnDesktop? _desktop;
    private readonly HANDLE _process;
    private readonly StreamReader _errors;

    private DesktopProcess(
        string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? environment, string? desktop, bool talks, bool unelevated)
    {
        if (desktop is null)
        {
            _desktop = new OwnDesktop();
            desktop = _desktop.Name;
        }

        lock (Starting)
        {
            _input = new(PipeDirection.Out, HandleInheritability.Inheritable);
            _output = new(PipeDirection.In, HandleInheritability.Inheritable);
            _error = new(PipeDirection.In, HandleInheritability.Inheritable);
            _process = Create(CommandLine(exe, args), EnvironmentBlock(environment), desktop, unelevated, out var id);
            Id = id;
            _input.DisposeLocalCopyOfClientHandle();
            _output.DisposeLocalCopyOfClientHandle();
            _error.DisposeLocalCopyOfClientHandle();
        }

        Input = new StreamWriter(_input, new UTF8Encoding(false)) { AutoFlush = true };
        Output = new StreamReader(_output, Encoding.UTF8);

        // What nobody reads is read anyway, so a process that writes more than a pipe holds never waits on it.
        _errors = new StreamReader(_error, Encoding.UTF8);
        Said = _errors.ReadToEndAsync();
        Told = talks ? Task.FromResult("") : Output.ReadToEndAsync();
    }

    public int Id { get; }

    public StreamWriter Input { get; }

    /// <summary>Its standard output, which only a process started to talk is read from by the test.</summary>
    public StreamReader Output { get; }

    /// <summary>All it writes to its standard error, once it has ended.</summary>
    public Task<string> Said { get; }

    // Its standard output drained, for one that does not talk.
    private Task<string> Told { get; }

    /// <summary>
    /// Starts <paramref name="exe"/> on <paramref name="desktop"/>, a test's own, or on a desktop made for it alone when
    /// null; one that <paramref name="talks"/> has its standard output read by the test, line by line. One started
    /// <paramref name="unelevated"/> runs as a basic user, its administrators' rights only to deny, even when the tests run
    /// elevated, as on a GitHub runner, whose account is the built-in Administrator and so is always.
    /// </summary>
    public static DesktopProcess Start(
        string exe,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment = null,
        string? desktop = null,
        bool talks = false,
        bool unelevated = false) => new(exe, args, environment, desktop, talks, unelevated);

    /// <summary>
    /// Runs <paramref name="exe"/> to its end, on a desktop of its own, and gives its exit code and what it wrote to its
    /// error stream; one that runs past <paramref name="patience"/>, as on a message box nobody can see, is ended, with -1.
    /// </summary>
    public static async Task<(int Code, string Said)> RunAsync(
        string exe,
        IReadOnlyList<string> args,
        TimeSpan patience,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(exe, args, environment);
        process.Input.Close();
        var code = await Task.Run(() => process.WaitForExit(patience)) ? process.ExitCode : -1;
        if (code == -1)
        {
            process.Kill();
        }

        await process.Told;
        return (code, await process.Said);
    }

    /// <summary>Waits up to <paramref name="patience"/> for it to end, on the real clock, which only bounds a hang.</summary>
    public bool WaitForExit(TimeSpan patience) =>
        TestPInvoke.WaitForSingleObject(_process, (uint)patience.TotalMilliseconds) == WAIT_EVENT.WAIT_OBJECT_0;

    public unsafe int ExitCode
    {
        get
        {
            uint code;
            return TestPInvoke.GetExitCodeProcess(_process, &code) ? unchecked((int)code) : -1;
        }
    }

    public void Kill() => TestPInvoke.TerminateProcess(_process, 1);

    public void Dispose()
    {
        if (!WaitForExit(TimeSpan.Zero))
        {
            Kill();
            WaitForExit(TimeSpan.FromSeconds(10));
        }

        TestPInvoke.CloseHandle(_process);
        Input.Dispose();
        Output.Dispose();
        _errors.Dispose();
        _input.Dispose();
        _output.Dispose();
        _error.Dispose();
        _desktop?.Dispose();
    }

    private unsafe HANDLE Create(string commandLine, string environment, string desktop, bool unelevated, out int id)
    {
        using var token = unelevated ? BasicUser() : null;
#pragma warning disable S3869 // CreateProcessAsUser takes a raw token; the safe handle holds it until the process is made.
        var user = token is null ? HANDLE.Null : (HANDLE)token.DangerousGetHandle();
#pragma warning restore S3869
        var line = (commandLine + "\0").ToCharArray();
        fixed (char* place = desktop)
        fixed (char* command = line)
        fixed (char* variables = environment)
        {
            var startup = new STARTUPINFOW
            {
                cb = (uint)sizeof(STARTUPINFOW),
                lpDesktop = place,
                dwFlags = STARTUPINFOW_FLAGS.STARTF_USESTDHANDLES,
#pragma warning disable S3869 // STARTUPINFOW takes raw handles; the pipes hold theirs until the process has its copies.
                hStdInput = (HANDLE)_input.ClientSafePipeHandle.DangerousGetHandle(),
                hStdOutput = (HANDLE)_output.ClientSafePipeHandle.DangerousGetHandle(),
                hStdError = (HANDLE)_error.ClientSafePipeHandle.DangerousGetHandle(),
#pragma warning restore S3869
            };
            PROCESS_INFORMATION process;
            var flags = PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT | PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW;
            var created = token is null
                ? TestPInvoke.CreateProcess(null, command, null, null, true, flags, variables, null, &startup, &process)
                : TestPInvoke.CreateProcessAsUser(user, null, command, null, null, true, flags, variables, null, &startup, &process);
            if (!created)
            {
                throw new Win32Exception();
            }

            TestPInvoke.CloseHandle(process.hThread);
            id = (int)process.dwProcessId;
            return process.hProcess;
        }
    }

    // The token Windows' basic user level makes of the test's own, as runas /trustlevel:0x20000 does: one made from the
    // caller's own needs no right to start a process with.
    private static unsafe SafeFileHandle BasicUser()
    {
        SAFER_LEVEL_HANDLE level;
        if (!TestPInvoke.SaferCreateLevel(TestPInvoke.SAFER_SCOPEID_USER, TestPInvoke.SAFER_LEVELID_NORMALUSER, TestPInvoke.SAFER_LEVEL_OPEN, &level, null))
        {
            throw new Win32Exception();
        }

        try
        {
            HANDLE token;
            if (!TestPInvoke.SaferComputeTokenFromLevel(level, HANDLE.Null, &token, default, null))
            {
                throw new Win32Exception();
            }

            return new SafeFileHandle(token, ownsHandle: true);
        }
        finally
        {
            TestPInvoke.SaferCloseLevel(level);
        }
    }

    // Windows' rules for one argument: quoted when it holds a space, a tab or a quote, or is empty, its quotes and the
    // backslashes before them escaped.
    private static string CommandLine(string exe, IReadOnlyList<string> args)
    {
        var line = new StringBuilder();
        foreach (var argument in args.Prepend(exe))
        {
            line.Append(line.Length > 0 ? " " : "");
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
            {
                line.Append(argument);
                continue;
            }

            line.Append('"');
            var slashes = 0;
            foreach (var character in argument)
            {
                slashes = character == '\\' ? slashes + 1 : 0;
                line.Append(character == '"' ? new string('\\', slashes + 1) + "\"" : character);
            }

            line.Append('\\', slashes).Append('"');
        }

        return line.ToString();
    }

    // The test's own variables with those given, sorted as Windows keeps them, each "name=value", the block ending with an
    // empty one.
    private static string EnvironmentBlock(IReadOnlyDictionary<string, string>? given)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string?)entry.Value ?? "";
        }

        foreach (var (name, value) in given ?? new Dictionary<string, string>())
        {
            variables[name] = value;
        }

        var block = new StringBuilder();
        foreach (var (name, value) in variables)
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString();
    }

    /// <summary>A desktop made for one process alone, closed once the test is done with it.</summary>
    private sealed class OwnDesktop : IDisposable
    {
        private readonly SafeHandle _handle;

        public OwnDesktop()
        {
            _handle = TestWindows.NewDesktop(Name);
        }

        public string Name { get; } = "thermi-test-" + Guid.NewGuid().ToString("N");

        public void Dispose() => _handle.Dispose();
    }
}
