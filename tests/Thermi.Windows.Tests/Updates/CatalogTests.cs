using System.Runtime.InteropServices;
using Thermi.Trust;
using Thermi.Windows.Tests.Processes;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// A release's signed catalog, which a stolen manifest key cannot make: a version runs only when thermi's certificate signed
/// it and it lists every file. The fixture is a version's folder and the catalog PowerShell's <c>New-FileCatalog</c> made
/// over it, unsigned, since no test holds thermi's key.
/// </summary>
public sealed class CatalogTests : IDisposable
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Updates", "Catalog");

    // The runtime thermi ships, which Microsoft signs.
    private static readonly string Runtime = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "coreclr.dll");

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

    // What could be done to a version's files after the catalog was made over them; a file moved is one added where no
    // member names it, as "a file added" is.
    [Theory]
    [InlineData("nothing", true)]
    [InlineData("a file changed", false)]
    [InlineData("a file added", false)]
    [InlineData("a file deleted", false)]
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
            case "a file deleted":
                File.Delete(page);
                break;
            case "the catalog gone":
                File.Delete(Path.Combine(_folder, FileCatalog.Name));
                break;
            default:
                break;
        }

        Assert.Equal(listed, FileCatalog.Lists(_folder) is null);
    }

    // A program's hash leaves its own signature out, as Windows computes it: a catalog made over a signed library lists it.
    [Fact]
    public async Task ACatalogListsAProgramByWindowsOwnHash()
    {
        File.Copy(Runtime, Path.Combine(_folder, "coreclr.dll"));
        File.Delete(Path.Combine(_folder, FileCatalog.Name));

        var make = $"New-FileCatalog -Path '{_folder}' -CatalogFilePath '{Path.Combine(_folder, FileCatalog.Name)}' -CatalogVersion 2.0 | Out-Null";
        var (made, _) = await DesktopProcess.RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", make], TimeSpan.FromMinutes(1));

        Assert.Equal(0, made);
        Assert.Null(FileCatalog.Lists(_folder));
    }

    // A certificate by its thumbprint, never its name, which a signing service gives every project it signs for.
    [Fact]
    public void OnlyThermisCertificateSignsACatalog()
    {
        var signer = Authenticode.Signer(Runtime);

        Assert.True(Authenticode.Trusted(Runtime) && Authenticode.SignedBy(Runtime, [signer]));
        Assert.False(Authenticode.SignedBy(Runtime, [UpdatePc.Thumbprint]));
        Assert.False(Authenticode.Trusted(Path.Combine(_folder, FileCatalog.Name)));
    }
}
