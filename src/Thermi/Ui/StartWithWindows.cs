using Microsoft.Win32;
using Thermi.Core.Install;

namespace Thermi.Ui;

/// <summary>
/// "Start with Windows": the overlay's value in the user's Run key, the very one <c>scripts/install.ps1</c> writes, so
/// Windows holds the one truth both read. The settings file never holds it, so undo never touches it, and a reinstall
/// keeps it as the user left it. Task Manager's Startup tab turns it off beside it, in StartupApproved, without deleting
/// it: the entry there starts with an odd byte while off (3, or 1 on older Windows) and an even one while on.
/// </summary>
/// <param name="key">The Run key under the user's hive; tests give one of their own.</param>
/// <param name="approvedKey">Task Manager's StartupApproved key.</param>
/// <param name="exePath">What the value starts at sign-in.</param>
internal sealed class StartWithWindows(string key, string approvedKey, string exePath)
{

    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string ValueName = RunAtSignIn.Name;

    public static StartWithWindows Of(string exePath) => new(RunAtSignIn.Key, ApprovedKey, exePath);

    // A value of another path, as a thermi installed elsewhere left, starts nothing that is this one: it shows off.
    public bool On => Written && !TurnedOff;

    private string Value => RunAtSignIn.Of(exePath);

    private bool Written
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(key);
            return run?.GetValue(ValueName) is string value && string.Equals(value, Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool TurnedOff
    {
        get
        {
            using var approved = Registry.CurrentUser.OpenSubKey(approvedKey);
            return approved?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
        }
    }

    /// <summary>
    /// Writes the value, quoted as the installer writes it, over one of another path, or deletes it. Turning it
    /// on clears Task Manager's off too, since deleting that entry is how Windows reads a value it never saw turned off.
    /// </summary>
    public void Set(bool on)
    {
        if (on && TurnedOff)
        {
            using var approved = Registry.CurrentUser.OpenSubKey(approvedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        if (on && Written)
        {
            return;
        }

        using var run = Registry.CurrentUser.CreateSubKey(key);
        if (on)
        {
            run.SetValue(ValueName, Value);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
