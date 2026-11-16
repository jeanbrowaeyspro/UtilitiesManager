using System.Security.Cryptography;
using UtilitiesManager.GitHub;

namespace UtilitiesManager.Updates;

/// <summary>Version plus récente de UtilitiesManager trouvée sur GitHub, avec son lanceur et son empreinte.</summary>
public sealed record AvailableUpdate(SemVersion Version, GitHubRelease Release, GitHubAsset Launcher, GitHubAsset Checksum)
{
    public string Notes => Release.Body ?? string.Empty;
}

/// <summary>
/// Mises à jour de UtilitiesManager par les releases GitHub de son dépôt public : quand chercher, quelle version
/// proposer, téléchargement vérifié par SHA-256. Jamais silencieuse : l'application propose, Jean valide.
/// </summary>
public sealed class UpdateService(GitHubRepositoryClient client)
{
    /// <summary>Dépôt public de l'outil (propriétaire/dépôt).</summary>
    public const string Repository = "jeanbrowaeyspro/UtilitiesManager";

    public const string LauncherAsset = AppPaths.LauncherFileName;
    public const string ChecksumAsset = LauncherAsset + ".sha256";

    /// <summary>Jamais deux recherches automatiques à moins d'une heure d'écart.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(1);

    public static bool IsCheckDue(DateTimeOffset? last, DateTimeOffset now) => last is null || now - last.Value >= MinimumInterval;

    /// <summary>
    /// Version plus récente que <paramref name="current"/>, null si l'application est à jour. Une version ignorée
    /// n'est proposée qu'à une recherche manuelle.
    /// </summary>
    public async Task<AvailableUpdate?> CheckAsync(SemVersion current, bool includePrereleases, string? skippedVersion, bool manual,
                                                   CancellationToken cancellation = default)
    {
        var releases = await client.GetReleasesAsync(cancellation: cancellation).ConfigureAwait(false);
        var best = SelectUpdate(releases, current, includePrereleases);
        if (best is null)
        {
            Log.Info($"UtilitiesManager {current} à jour");
            return null;
        }
        if (!manual && string.Equals(skippedVersion, best.Version.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            Log.Info($"UtilitiesManager {best.Version} disponible mais ignorée");
            return null;
        }
        Log.Info($"UtilitiesManager {best.Version} disponible (version installée {current})");
        return best;
    }

    /// <summary>Plus haute release supérieure à <paramref name="current"/> qui porte le lanceur et son empreinte.</summary>
    public static AvailableUpdate? SelectUpdate(IEnumerable<GitHubRelease> releases, SemVersion current, bool includePrereleases)
    {
        AvailableUpdate? best = null;
        foreach (var release in releases)
        {
            if (release.Draft || !SemVersion.TryParse(release.TagName, out var version)
                || ((release.Prerelease || version.IsTestVersion) && !includePrereleases)
                || version <= current || (best is not null && version <= best.Version))
                continue;
            var launcher = release.FindAsset(n => n.Equals(LauncherAsset, StringComparison.OrdinalIgnoreCase));
            var checksum = release.FindAsset(n => n.Equals(ChecksumAsset, StringComparison.OrdinalIgnoreCase));
            if (launcher is not null && checksum is not null)
                best = new AvailableUpdate(version, release, launcher, checksum);
        }
        return best;
    }

    /// <summary>Télécharge le lanceur vers <paramref name="destination"/> et vérifie son empreinte et son en-tête.</summary>
    public async Task DownloadAsync(AvailableUpdate update, string destination, IProgress<double>? progress = null,
                                    CancellationToken cancellation = default)
    {
        var expected = ParseChecksum(await client.ReadAssetTextAsync(update.Checksum, cancellation).ConfigureAwait(false))
                       ?? throw new UpdateException(L.T("The SHA-256 checksum of the update is unreadable."));
        await client.DownloadAssetAsync(update.Launcher, destination, progress, cancellation).ConfigureAwait(false);
        var actual = ComputeSha256(destination);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) || !IsExecutable(destination))
        {
            File.Delete(destination);
            throw new UpdateException(L.T("The downloaded file does not match its SHA-256 checksum, it was deleted."));
        }
        Log.Info($"Empreinte SHA-256 vérifiée pour {update.Launcher.Name} {update.Version}");
    }

    /// <summary>Empreinte d'un fichier .sha256 : "empreinte" ou "empreinte  nom" (format sha256sum).</summary>
    public static string? ParseChecksum(string text)
    {
        var first = text.Split((char[])[' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    public static string ComputeSha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Vrai si le fichier commence par l'en-tête <c>MZ</c> d'un exécutable Windows.</summary>
    public static bool IsExecutable(string path)
    {
        using var file = File.OpenRead(path);
        return file.Length > 2 && file.ReadByte() == 'M' && file.ReadByte() == 'Z';
    }
}

public sealed class UpdateException(string message) : Exception(message);
