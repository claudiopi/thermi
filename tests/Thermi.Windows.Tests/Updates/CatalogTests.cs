using System.Runtime.InteropServices;
using Thermi.Trust;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// A release's signed catalog, which a stolen manifest key cannot make: a version runs only when thermi's publisher signed
/// it and it lists every file. The fixture is a version's folder and the catalog PowerShell's <c>New-FileCatalog</c> made
/// over it, unsigned, since no test holds thermi's key.
/// </summary>
public sealed class CatalogTests : IDisposable
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Updates", "Catalog");

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "catalogs", Guid.NewGuid().ToString("N"));

    public CatalogTests()
    {
        foreach (var file in Directory.EnumerateFiles(Fixture, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_folder, Path.GetRelativePath(Fixture, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // What could be done to a version's files after the catalog was made over them.
    [Theory]
    [InlineData("nothing", true)]
    [InlineData("a file changed", false)]
    [InlineData("a file added", false)]
    [InlineData("a file moved", false)]
    [InlineData("the catalog gone", false)]
    public void AVersionRunsOnlyWhatItsCatalogLists(string done, bool listed)
    {
        var page = Path.Combine(_folder, "settings", "index.html");
        switch (done)
        {
            case "a file changed":
                File.AppendAllText(page, "<script src=\"https://example.com/x.js\"></script>");
                break;
            case "a file added":
                File.WriteAllText(Path.Combine(_folder, "settings", "extra.js"), "fetch('https://example.com')");
                break;
            case "a file moved":
                File.Move(page, Path.Combine(_folder, "index.html"));
                break;
            case "the catalog gone":
                File.Delete(Path.Combine(_folder, FileCatalog.Name));
                break;
            default:
                break;
        }

        Assert.Equal(listed, FileCatalog.Lists(_folder) is null);
    }

    [Fact]
    public void OnlyItsPublisherSignsACatalog()
    {
        // The runtime thermi ships, which Microsoft signs as ".NET".
        var runtime = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "coreclr.dll");

        Assert.True(Authenticode.Trusted(runtime) && Authenticode.SignedBy(runtime, ".NET"));
        Assert.False(Authenticode.SignedBy(runtime, "thermi"));
        Assert.False(Authenticode.Trusted(Path.Combine(_folder, FileCatalog.Name)));
    }
}
