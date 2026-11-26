using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

// Records compare their collections by reference; a manifest read back holds new ones.
internal sealed class ManifestComparer : IEqualityComparer<Manifest?>
{
    public static readonly ManifestComparer Instance = new();

    public bool Equals(Manifest? x, Manifest? y) =>
        x is not null && y is not null
        && (x with { Packages = y.Packages, Msis = y.Msis, Withdrawn = y.Withdrawn }) == y
        && x.Packages.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(y.Packages.OrderBy(p => p.Key, StringComparer.Ordinal))
        && x.Msis.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(y.Msis.OrderBy(p => p.Key, StringComparer.Ordinal))
        && x.Withdrawn.SequenceEqual(y.Withdrawn);

    public int GetHashCode(Manifest? obj) => obj?.Sequence.GetHashCode() ?? 0;
}
