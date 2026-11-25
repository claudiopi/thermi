using Microsoft.Win32;
using Thermi.Ui;

namespace Thermi.Windows.Tests.Window;

/// <summary>"Start with Windows" and the installer share one start-up entry, in a Run key of the test's own.</summary>
public sealed class StartWithWindowsTests
{
    // R52: the switch writes and deletes the value, quoted, as the installer writes it, and reads what Windows holds.
    [Fact]
    public void TheSwitchWritesAndDeletesTheStartUpEntry()
    {
        using var run = new TestRunKey();

        var before = run.Startup.On;
        run.Startup.Set(on: true);
        var written = run.Value;
        run.Startup.Set(on: true);
        var on = run.Startup.On;
        run.Startup.Set(on: false);

        Assert.Equal((false, true, false), (before, on, run.Startup.On));
        Assert.Equal($"\"{TestRunKey.Exe}\"", written);
        Assert.Null(run.Value);
    }

    // A value left by a thermi installed elsewhere starts nothing: the switch shows off, and turning it on writes this one.
    [Fact]
    public void AnEntryOfAnotherPathShowsOffAndTurningItOnWritesThisOne()
    {
        using var run = new TestRunKey();
        run.Write(@"""C:\Program Files\thermi 0.1\thermi.exe""");

        var before = run.Startup.On;
        run.Startup.Set(on: true);

        Assert.Equal((false, true, $"\"{TestRunKey.Exe}\""), (before, run.Startup.On, run.Value));
    }

    // Task Manager's Startup tab turns an app off beside the Run key, in StartupApproved, whose first byte is odd while
    // off: the switch shows it off then, and turning it on clears that, leaving the Run value as it was.
    [Fact]
    public void AnEntryTurnedOffInTaskManagerShowsOffAndTurningItOnClearsIt()
    {
        using var run = new TestRunKey();
        run.Startup.Set(on: true);

        run.Approve(0x03);
        var off = run.Startup.On;
        run.Startup.Set(on: true);
        var (on, approved, value) = (run.Startup.On, run.Approved, run.Value);
        run.Approve(0x02);

        Assert.Equal((false, true, null, $"\"{TestRunKey.Exe}\""), (off, on, approved, value));
        Assert.True(run.Startup.On);
    }

    // R52: the installer keeps the same value, in the same key, under the same name, and writes it again only on a first
    // install or when it was already there, so a reinstall never turns the switch back on.
    [Fact]
    public void TheInstallerKeepsTheSameStartUpEntry()
    {
        var script = File.ReadAllText(Path.Combine(TestRecordings.RepoRoot(), "scripts", "install.ps1"));

        Assert.Contains($"$runKey = 'HKCU:\\{Thermi.Core.Install.RunAtSignIn.Key}'", script, StringComparison.Ordinal);
        Assert.Contains($"Set-ItemProperty $runKey -Name {StartWithWindows.ValueName} -Value \"`\"$overlay`\"\"", script, StringComparison.Ordinal);
        Assert.Contains("if ($firstInstall -or $startsWithWindows) {", script, StringComparison.Ordinal);
        Assert.Contains($"$approvedKey = 'HKCU:\\{StartWithWindows.ApprovedKey}'", script, StringComparison.Ordinal);
    }
}

/// <summary>A Run key of the test's own under the user's hive, deleted with everything in it once the test is done.</summary>
internal sealed class TestRunKey : IDisposable
{
    public const string Exe = @"C:\Program Files\thermi\thermi.exe";

    private readonly string _parent = @"Software\thermi-tests\" + Guid.NewGuid().ToString("N");

    public TestRunKey()
    {
        Startup = new StartWithWindows(_parent + @"\Run", _parent + @"\StartupApproved", Exe);
    }

    public StartWithWindows Startup { get; }

    public string? Value
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_parent + @"\Run");
            return key?.GetValue(StartWithWindows.ValueName) as string;
        }
    }

    /// <summary>The StartupApproved entry's first byte, or null when there is none.</summary>
    public byte? Approved
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_parent + @"\StartupApproved");
            return key?.GetValue(StartWithWindows.ValueName) is byte[] { Length: > 0 } bytes ? bytes[0] : null;
        }
    }

    public void Write(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_parent + @"\Run");
        key.SetValue(StartWithWindows.ValueName, value);
    }

    /// <summary>Writes the entry as Task Manager does: its state, then the time it changed.</summary>
    public void Approve(byte state)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_parent + @"\StartupApproved");
        key.SetValue(StartWithWindows.ValueName, new byte[] { state, 0, 0, 0, 0x5A, 0x1E, 0x3C, 0x79, 0x9F, 0x5B, 0xDB, 0x01 }, RegistryValueKind.Binary);
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_parent, throwOnMissingSubKey: false);
}
