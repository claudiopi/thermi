using Thermi.Core.Sensors.Rules;

namespace Thermi.Tests.Sensors;

/// <summary>What each chip family's SMC keys measure, as the macOS service names its sensors by them.</summary>
public sealed class AppleSiliconTests
{
    [Theory] // Rows: a static table's entries, a family each, the prefix a later M falls back to, and keys no family names.
    [InlineData("Apple M1", "Tp09", AppleRole.EfficiencyCore, "E-Core Tp09")]
    [InlineData("Apple M2 Max", "Tp1h", AppleRole.EfficiencyCore, "E-Core Tp1h")]
    [InlineData("Apple M3 Pro", "Tf14", AppleRole.GpuCore, "GPU Core Tf14")]
    [InlineData("Apple M4", "Tp01", AppleRole.PerformanceCore, "P-Core Tp01")]
    [InlineData("Apple M4", "Tm0p", AppleRole.Memory, "Memory Tm0p")]
    [InlineData("Apple M4", "Tp00", null, null)]
    [InlineData("Apple M5", "Te07", AppleRole.EfficiencyCore, "E-Core Te07")]
    [InlineData("Apple M5", "Ts00", null, null)]
    [InlineData("Apple M4", "PSTR", AppleRole.Power, "System Total")]
    [InlineData("Apple A18 Pro", "Tp05", null, null)]
    public void EachFamilyNamesItsOwnKeys(string chip, string key, AppleRole? role, string? name)
    {
        var named = AppleSilicon.KeyOf(chip, key);

        Assert.Equal(role, named?.Role);
        Assert.Equal(name, named?.Name);
    }
}
