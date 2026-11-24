using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Security.Cryptography.Catalog;

namespace Thermi.Trust;

/// <summary>
/// A release's catalog, <c>thermi.cat</c>, as PowerShell's <c>New-FileCatalog</c> makes it and thermi's Authenticode key
/// signs it: every file of the version under its path, each with the hash Windows computes for its kind, so a program's
/// leaves its own signature out. Windows reads it; thermi parses nothing of it itself.
/// </summary>
public static class FileCatalog
{
    public const string Name = "thermi.cat";

    // The attribute New-FileCatalog gives each member: its path, relative to the folder it was made over. Each file is a
    // member twice, by its SHA-1 and by its SHA-256, and only the second is read.
    private const string FileTag = "FilePath";

    private const int Sha256Bytes = 32;

    /// <summary>
    /// Null when the catalog in <paramref name="folder"/> lists every other file there under its own path with its own
    /// hash; else why not. Whether the catalog is signed, and by whom, is <see cref="Authenticode.SignedBy"/>'s.
    /// </summary>
    public static string? Lists(string folder)
    {
        var catalog = Path.Combine(folder, Name);
        if (!File.Exists(catalog) || Members(catalog) is not { } listed)
        {
            return $"it has no {Name} that Windows can read";
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file);
            if (relative != Name && (!listed.TryGetValue(relative, out var hash) || !string.Equals(hash, Hash(file), StringComparison.Ordinal)))
            {
                return $"its catalog does not list {relative} as it is";
            }
        }

        return null;
    }

    private static unsafe Dictionary<string, string>? Members(string catalog)
    {
        var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        fixed (char* path = catalog)
        {
            var handle = PInvoke.CryptCATOpen(path, CRYPTCAT_OPEN_FLAGS.CRYPTCAT_OPEN_EXISTING, 0, CRYPTCAT_VERSION.CRYPTCAT_VERSION_2, 0);
            if ((nint)handle.Value is 0 or -1)
            {
                return null;
            }

            try
            {
                for (var member = PInvoke.CryptCATEnumerateMember(handle, null); member is not null; member = PInvoke.CryptCATEnumerateMember(handle, member))
                {
                    if (member->pIndirectData is not null && member->pIndirectData->Digest is { cbData: Sha256Bytes } digest
                        && FileOf(handle, member) is { } file)
                    {
                        members[file] = Convert.ToHexString(new ReadOnlySpan<byte>(digest.pbData, Sha256Bytes));
                    }
                }
            }
            finally
            {
                PInvoke.CryptCATClose(handle);
            }
        }

        return members;
    }

    private static unsafe string? FileOf(Windows.Win32.Foundation.HANDLE handle, CRYPTCATMEMBER* member)
    {
        for (var attribute = PInvoke.CryptCATEnumerateAttr(handle, member, null); attribute is not null;
            attribute = PInvoke.CryptCATEnumerateAttr(handle, member, attribute))
        {
            if (string.Equals(attribute->pwszReferenceTag.ToString(), FileTag, StringComparison.OrdinalIgnoreCase))
            {
                return new string((char*)attribute->pbValue, 0, (int)(attribute->cbValue / sizeof(char))).TrimEnd('\0');
            }
        }

        return null;
    }

    // The hash Windows itself puts in a catalog for this file's kind.
    private static unsafe string Hash(string file)
    {
        // Windows 8's calls, which thermi's Windows 10 and later all have.
        if (!OperatingSystem.IsWindowsVersionAtLeast(8))
        {
            throw new PlatformNotSupportedException("Windows before 8 cannot hash a file for a catalog.");
        }

        nint admin;
        bool acquired;
        fixed (char* algorithm = "SHA256")
        {
            acquired = PInvoke.CryptCATAdminAcquireContext2(&admin, null, algorithm, null, 0);
        }

        if (!acquired)
        {
            throw new IOException($"Windows could not hash {file}: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        try
        {
            using var open = File.OpenHandle(file);
            uint size = 0;
            PInvoke.CryptCATAdminCalcHashFromFileHandle2(admin, open, ref size, default);
            var hash = new byte[size];
            if (!PInvoke.CryptCATAdminCalcHashFromFileHandle2(admin, open, ref size, hash))
            {
                throw new IOException($"Windows could not hash {file}: {Marshal.GetLastPInvokeErrorMessage()}");
            }

            return Convert.ToHexString(hash);
        }
        finally
        {
            PInvoke.CryptCATAdminReleaseContext(admin, 0);
        }
    }
}
