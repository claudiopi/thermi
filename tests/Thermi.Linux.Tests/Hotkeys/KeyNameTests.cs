using Thermi.Core;
using Thermi.Linux.Hotkeys;

namespace Thermi.Linux.Tests.Hotkeys;

/// <summary>Every key a hotkey may take on Windows has its name on the Linux desktops, so none of them is refused here alone.</summary>
public sealed class KeyNameTests
{
    [Fact]
    public void EveryKeyAHotkeyTakesHasItsNameOnLinux()
    {
        var taken = Enumerable.Range(0, 256)
            .Where(key => Core.Hotkeys.Parse(Core.Hotkeys.Write(KeyModifiers.Ctrl | KeyModifiers.Alt, key)) is not null)
            .ToList();

        Assert.Contains(0x2E, taken);
        Assert.All(taken, key => Assert.True(KeyNames.Knows(key), $"Linux has no name for the key 0x{key:X2}."));
    }
}
