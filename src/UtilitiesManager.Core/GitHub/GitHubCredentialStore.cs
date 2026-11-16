using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace UtilitiesManager.GitHub;

/// <summary>
/// Connexion GitHub de l'utilisateur gardee dans un fichier chiffre par DPAPI pour le compte Windows courant
/// (illisible par un autre compte ou sur un autre poste). Ecriture atomique.
/// </summary>
public sealed class GitHubCredentialStore(string file)
{
    public string File { get; } = file;

    /// <summary>Connexion enregistree, null si absente ou illisible (fichier d'un autre compte, abime).</summary>
    public GitHubCredential? Load()
    {
        try
        {
            return System.IO.File.Exists(File)
                ? JsonSerializer.Deserialize<GitHubCredential>(Dpapi.Unprotect(System.IO.File.ReadAllBytes(File)))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or Win32Exception)
        {
            return null;
        }
    }

    public void Save(GitHubCredential credential) =>
        AtomicFile.WriteAllBytes(File, Dpapi.Protect(JsonSerializer.SerializeToUtf8Bytes(credential)));

    public void Delete() => System.IO.File.Delete(File);

    /// <summary>CryptProtectData / CryptUnprotectData, portee utilisateur, sans interface.</summary>
    private static class Dpapi
    {
        private const int UiForbidden = 0x1;

        public static byte[] Protect(byte[] data) => Transform(data, protect: true);

        public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(Math.Max(data.Length, 1)) };
            var output = default(DataBlob);
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                var ok = protect
                    ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
                if (!ok)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
