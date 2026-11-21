namespace Thermi.Core;

/// <summary>How alarming a reading is. The overlay colors values by level.</summary>
public enum Level
{
    Normal,
    Warning,
    Critical,
}

/// <summary>The values at which a reading turns into a warning and then critical.</summary>
public sealed record Threshold
{
    public Threshold(double warning, double critical)
    {
        if (!double.IsFinite(warning) || !double.IsFinite(critical) || warning > critical)
        {
            throw new ArgumentException($"Expected finite values with warning <= critical, got {warning} and {critical}.", nameof(warning));
        }

        Warning = warning;
        Critical = critical;
    }

    public double Warning { get; }

    public double Critical { get; }

    /// <summary>A missing reading is never alarming: the overlay shows it as missing instead.</summary>
    public Level LevelOf(Reading reading) => reading.Usable switch
    {
        null => Level.Normal,
        var value when value >= Critical => Level.Critical,
        var value when value >= Warning => Level.Warning,
        _ => Level.Normal,
    };
}
