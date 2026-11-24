using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.WinTrust;

namespace Thermi.Trust;

/// <summary>Whether Windows holds a file's Authenticode signature valid, and whose it is.</summary>
public static class Authenticode
{
    // CERT_E_REVOCATION_FAILURE: the revocation could not be checked.
    private const int RevocationUnknown = unchecked((int)0x800B010E);

    /// <summary>Whether <paramref name="path"/> holds a signature Windows trusts, from anyone.</summary>
    public static bool Trusted(string path) => Verify(path, signers: null);

    /// <summary>
    /// Whether <paramref name="path"/> holds a signature Windows trusts, by a certificate whose SHA-256 thumbprint is one of
    /// <paramref name="signers"/>, and not revoked. A certificate, not a name: a signing service may sign other projects
    /// under the same name.
    /// </summary>
    public static bool SignedBy(string path, IReadOnlyCollection<string> signers) => Verify(path, signers);

    /// <summary>The SHA-256 thumbprint of the certificate that signed <paramref name="path"/>, in upper-case hex.</summary>
    public static string Signer(string path)
    {
#pragma warning disable SYSLIB0057 // The signed file's signer, which X509CertificateLoader cannot read; trust is judged apart.
        using var signed = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        using var certificate = X509CertificateLoader.LoadCertificate(signed.GetRawCertData());
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }

    private static unsafe bool Verify(string path, IReadOnlyCollection<string>? signers)
    {
        fixed (char* file = path)
        {
            var info = new WINTRUST_FILE_INFO { cbStruct = (uint)sizeof(WINTRUST_FILE_INFO), pcwszFilePath = file };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                fdwRevocationChecks = signers is null ? WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE : WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
            };
            data.Anonymous.pFile = &info;
            var action = PInvoke.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            try
            {
                var verdict = PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);

                // A revocation server out of reach is no revocation, as browsers hold it: the pinned certificate is the
                // check, and a PC offline or behind a proxy still updates. A certificate revoked is refused.
                return (verdict == 0 || (signers is not null && verdict == RevocationUnknown))
                    && (signers is null || signers.Contains(Signer(path), StringComparer.OrdinalIgnoreCase));
            }
            finally
            {
                data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
                PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);
            }
        }
    }
}
