using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.Core.Updates;

/// <summary>
/// A release's version, <c>major.minor.patch</c> with an optional <c>-beta.n</c> or <c>-rc.n</c>, ordered as SemVer orders
/// them: a beta before its release. It names its folder under the install folder, so it is read strictly: digits without
/// leading zeros, and nothing that could leave a folder.
/// </summary>
[JsonConverter(typeof(ReleaseVersionJson))]
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string? Stage = null, int StageNumber = 0) : IComparable<ReleaseVersion>
{
    private static readonly string[] Stages = ["beta", "rc"];

    public static ReleaseVersion? Parse(string? text)
    {
        if (text is null || text.Length > 40)
        {
            return null;
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = (dash < 0 ? text : text[..dash]).Split('.');
        if (core.Length != 3 || Number(core[0]) is not { } major || Number(core[1]) is not { } minor || Number(core[2]) is not { } patch)
        {
            return null;
        }

        if (dash < 0)
        {
            return new ReleaseVersion(major, minor, patch);
        }

        var stage = text[(dash + 1)..].Split('.');
        return stage.Length == 2 && Stages.Contains(stage[0], StringComparer.Ordinal) && Number(stage[1]) is { } n and > 0
            ? new ReleaseVersion(major, minor, patch, stage[0], n)
            : null;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        // A release follows every beta and candidate of it; between stages, beta comes before rc.
        return (Rank(this), StageNumber).CompareTo((Rank(other), other.StageNumber));
    }

    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Compare(left, right) < 0;
    }

    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Compare(left, right) > 0;
    }

    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Compare(left, right) <= 0;
    }

    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Compare(left, right) >= 0;
    }

    public override string ToString() => Stage is null
        ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
        : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{Stage}.{StageNumber}");

    private static int Compare(ReleaseVersion? left, ReleaseVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    private static int Rank(ReleaseVersion version) => version.Stage is null ? Stages.Length : Array.IndexOf(Stages, version.Stage);

    private static int? Number(string text) =>
        text.Length is > 0 and <= 9 && (text.Length == 1 || text[0] != '0') && text.All(char.IsAsciiDigit)
            ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;
}

public sealed class ReleaseVersionJson : JsonConverter<ReleaseVersion>
{
    public override ReleaseVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReleaseVersion.Parse(reader.GetString()) ?? throw new JsonException("Not a version thermi releases.");

    public override void Write(Utf8JsonWriter writer, ReleaseVersion value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
