using System.Security.Cryptography.X509Certificates;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.WinTrust;

namespace Thermi.Trust;

/// <summary>Whether Windows holds a file's Authenticode signature valid, and whose it is.</summary>
public static class Authenticode
{
    /// <summary>Whether <paramref name="path"/> holds a signature Windows trusts, from anyone.</summary>
    public static bool Trusted(string path) => Verify(path, publisher: null);

    /// <summary>Whether <paramref name="path"/> holds a signature Windows trusts, from <paramref name="publisher"/>.</summary>
    public static bool SignedBy(string path, string publisher) => Verify(path, publisher);

    private static unsafe bool Verify(string path, string? publisher)
    {
        fixed (char* file = path)
        {
            var info = new WINTRUST_FILE_INFO { cbStruct = (uint)sizeof(WINTRUST_FILE_INFO), pcwszFilePath = file };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                fdwRevocationChecks = WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
            };
            data.Anonymous.pFile = &info;
            var action = PInvoke.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            try
            {
                return PInvoke.WinVerifyTrust(HWND.Null, ref action, &data) == 0 && (publisher is null || Signer(path) == publisher);
            }
            finally
            {
                data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
                PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);
            }
        }
    }

    // Read once Windows holds the signature valid; the file is in a folder only administrators change.
    private static string Signer(string path)
    {
#pragma warning disable SYSLIB0057 // The signed file's signer, which X509CertificateLoader cannot read; trust was judged just before.
        using var signed = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        using var certificate = X509CertificateLoader.LoadCertificate(signed.GetRawCertData());
        return certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
    }
}
