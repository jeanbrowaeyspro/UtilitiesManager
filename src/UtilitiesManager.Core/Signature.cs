using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace UtilitiesManager;

/// <summary>Résultat du contrôle de la signature Authenticode d'un fichier.</summary>
/// <param name="Trusted">Signature valide, chaîne de certificats reconnue par Windows.</param>
/// <param name="Signer">Nom commun (CN) du certificat du signataire.</param>
/// <param name="Error">Raison du refus de Windows, si la signature n'est pas valide.</param>
public sealed record SignatureCheck(bool Trusted, string? Signer, string? Thumbprint, string? Error)
{
    /// <summary>
    /// Vrai si l'installeur peut être lancé : signature valide et, si le profil en attend
    /// un, signataire <paramref name="expectedSigner"/>.
    /// </summary>
    public bool Accepts(string? expectedSigner) =>
        Trusted && (string.IsNullOrEmpty(expectedSigner) || string.Equals(Signer, expectedSigner, StringComparison.Ordinal));

    public override string ToString() =>
        $"{(Trusted ? "valide" : $"non valide ({Error})")}, signataire {Signer ?? "aucun"}, empreinte {Thumbprint ?? "aucune"}";
}

/// <summary>Contrôle de signature, remplacé par un faux dans les tests de l'installation.</summary>
public interface ISignatureVerifier
{
    SignatureCheck Verify(string path);
}

/// <summary>
/// Signature Authenticode vérifiée par Windows (WinVerifyTrust, révocation contrôlée sur
/// toute la chaîne), puis nom du signataire lu dans le certificat.
/// </summary>
public sealed class AuthenticodeVerifier : ISignatureVerifier
{
    public SignatureCheck Verify(string path)
    {
        var status = WinVerifyTrust(path);
        string? signer = null, thumbprint = null;
        try
        {
#pragma warning disable SYSLIB0057   // aucun équivalent dans X509CertificateLoader pour un fichier signé
            using var signed = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using var certificate = new X509Certificate2(signed);
            signer = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            thumbprint = certificate.Thumbprint;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Fichier sans signature.
        }
        var trusted = status == 0;
        return new SignatureCheck(trusted, signer, thumbprint, trusted ? null : $"0x{status:X8}");
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private static int WinVerifyTrust(string path)
    {
        var file = new WintrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            FilePath = path,
        };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, fDeleteOld: false);
            var data = new WintrustData
            {
                Size = (uint)Marshal.SizeOf<WintrustData>(),
                UiChoice = 2,              // WTD_UI_NONE
                RevocationChecks = 1,      // WTD_REVOKE_WHOLECHAIN
                UnionChoice = 1,           // WTD_CHOICE_FILE
                File = filePointer,
                StateAction = 1,           // WTD_STATEACTION_VERIFY
                ProviderFlags = 0x80,      // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            };
            var action = GenericVerifyV2;
            var noWindow = new IntPtr(-1);
            var status = NativeVerify(noWindow, ref action, ref data);
            data.StateAction = 2;          // WTD_STATEACTION_CLOSE
            NativeVerify(noWindow, ref action, ref data);
            return status;
        }
        finally
        {
            Marshal.DestroyStructure<WintrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [DllImport("wintrust.dll", EntryPoint = "WinVerifyTrust", CharSet = CharSet.Unicode)]
    private static extern int NativeVerify(IntPtr window, ref Guid action, ref WintrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
