using System.Security.Cryptography;

namespace Thermi.Releases;

/// <summary>The release and backup key pairs, each a private and a public PEM.</summary>
internal static class KeyFiles
{
    public static readonly IReadOnlyList<string> Names = ["release.key.pem", "release.pub.pem", "backup.key.pem", "backup.pub.pem"];

    /// <summary>
    /// Writes both pairs into <paramref name="folder"/>, or none and why: never over a key there, as the backup pair is
    /// the one every PC trusts once a release key is revoked, and a pair half new matches nothing.
    /// </summary>
    public static string? MakeIn(DirectoryInfo folder)
    {
        folder.Create();
        if (Names.FirstOrDefault(name => File.Exists(Path.Combine(folder.FullName, name))) is { } there)
        {
            return $"{there} is already in {folder.FullName}: make keys in a new, empty folder.";
        }

        var written = new List<string>();
        try
        {
            foreach (var name in new[] { "release", "backup" })
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                New(Path.Combine(folder.FullName, name + ".key.pem"), key.ExportPkcs8PrivateKeyPem(), written);
                New(Path.Combine(folder.FullName, name + ".pub.pem"), key.ExportSubjectPublicKeyInfoPem(), written);
            }
        }
        catch (IOException ex)
        {
            written.ForEach(File.Delete);
            return $"no keys were written: {ex.Message}";
        }

        return null;
    }

    // Never over a file, even one made since the check above.
    private static void New(string path, string text, List<string> written)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        written.Add(path);
        using var writer = new StreamWriter(file);
        writer.Write(text);
    }
}
