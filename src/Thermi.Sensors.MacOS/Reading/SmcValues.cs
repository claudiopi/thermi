using Thermi.Core.Sensors.Rules;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// The SMC's keys thermi shows on this chip, found once, and each read once a second, however many readers ask, so a
/// recording keeps the one value they all read. A key that fails to read, or is gone, reads missing, and says the keys
/// should be found again: a key that fails then too is left out, as one the SMC lists and never answers.
/// </summary>
internal sealed class SmcValues(IMac mac, string chip)
{
    private readonly Dictionary<string, double?> _read = new(StringComparer.Ordinal);

    /// <summary>The keys found, each with what it measures, in the SMC's order.</summary>
    public IReadOnlyList<(string Key, AppleKey Named)> Keys { get; private set; } = [];

    /// <summary>
    /// The fans the SMC counts this second, read each second as every key is, so a recording holds it in each; a Mac that
    /// gives no count, as a virtual machine, has none, and that is no key failing.
    /// </summary>
    public int Fans => Value(AppleSilicon.FanCount) is { } fans ? (int)Math.Clamp(fans, 0, 10) : 0;

    /// <summary>Whether a key failed to read this second, so the keys are found again at the next.</summary>
    public bool Failed { get; private set; }

    /// <summary>Finds the keys again, reading each, and keeps those that answer.</summary>
    public void Find()
    {
        Keys = [.. mac.SmcKeys()
            .Select(key => (Key: key, Named: AppleSilicon.KeyOf(chip, key)))
            .Where(found => found.Named is not null && this[found.Key] is not null)
            .Select(found => (found.Key, found.Named!))];
        Failed = false;
    }

    /// <summary>Starts a second: every key is read afresh.</summary>
    public void Next()
    {
        _read.Clear();
        Failed = false;
    }

    /// <summary>A key's value this second, or null when it is gone or failed to read.</summary>
    public double? this[string key]
    {
        get
        {
            var value = Value(key);
            Failed |= value is null;
            return value;
        }
    }

    private double? Value(string key)
    {
        if (!_read.TryGetValue(key, out var value))
        {
            value = mac.Smc(key);
            _read[key] = value;
        }

        return value;
    }
}
