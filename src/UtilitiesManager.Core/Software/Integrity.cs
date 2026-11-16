using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>Fichier refusé avant installation : rien n'est lancé.</summary>
public sealed class IntegrityException(string message) : Exception(message);

/// <summary>
/// Contrôle d'un fichier téléchargé selon <c>verify</c> : signature Authenticode (et éditeur attendu), empreinte
/// SHA-256 (figée ou publiée par l'éditeur), ou origine de confiance (dépôt du profil, release GitHub).
/// </summary>
public sealed class Integrity(ISignatureVerifier signatures)
{
    public async Task CheckAsync(ProgramEntry program, string file, string? depVersion, CancellationToken cancel = default)
    {
        switch (program.VerifyOrDefault)
        {
            case Verifications.Origin:
                return;
            case Verifications.Sha256:
                var expected = program.Sha256 ?? await PublishedHashAsync(program, file, depVersion, cancel).ConfigureAwait(false);
                var actual = Sha256Of(file);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new IntegrityException(L.T("{0}: SHA-256 fingerprint does not match ({1}).", program.Name, Path.GetFileName(file)));
                return;
            default:
                CheckSignature(program, file);
                return;
        }
    }

    /// <summary>Signature valide et éditeur attendu ; un zip est contrôlé exe par exe après décompression.</summary>
    public void CheckSignature(ProgramEntry program, string file)
    {
        var check = signatures.Verify(file);
        Log.Info($"Signature de {Path.GetFileName(file)} : {check}");
        if (!check.Accepts(program.Signer))
            throw new IntegrityException(check.Trusted
                ? L.T("{0}: signed by {1}, {2} expected.", program.Name, check.Signer ?? "?", program.Signer ?? "")
                : L.T("{0}: invalid or missing signature ({1}).", program.Name, Path.GetFileName(file)));
    }

    public static string Sha256Of(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static async Task<string> PublishedHashAsync(ProgramEntry program, string file, string? depVersion, CancellationToken cancel)
    {
        var url = ShellCommands.Expand(program.ChecksumUrl!, version: program.Version, depVersion: depVersion);
        var text = await Web.GetStringAsync(url, cancel).ConfigureAwait(false);
        return FindHash(text, Path.GetFileName(file))
               ?? throw new IntegrityException(L.T("{0}: no fingerprint published for {1}.", program.Name, Path.GetFileName(file)));
    }

    /// <summary>Ligne <c>empreinte *nom</c> ou <c>empreinte  nom</c> d'un fichier au format sha256sum.</summary>
    public static string? FindHash(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (match.Success && string.Equals(match.Groups[2].Value.Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                return match.Groups[1].Value;
        }
        return null;
    }
}
