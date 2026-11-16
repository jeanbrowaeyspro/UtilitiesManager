using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace UtilitiesManager.Updates;

/// <summary>Version plus recente trouvee sur GitHub, avec son installeur et son empreinte.</summary>
public sealed record AvailableUpdate(SemVersion Version, GitHubRelease Release, GitHubAsset Installer, GitHubAsset Checksum)
{
    /// <summary>
    /// Vrai si une des versions entre l'installee (exclue) et celle-ci (incluse) a une rubrique Extension dans ses
    /// notes : l'installeur recopiera l'extension, TwinCAT doit etre ferme.
    /// </summary>
    public bool ChangesExtension { get; init; }

    public string Notes => Release.Body ?? string.Empty;
}

/// <summary>
/// Mises a jour de l'application par les Releases GitHub : quand verifier, quelle version proposer,
/// telechargement verifie par SHA-256, lancement silencieux de l'installeur.
/// </summary>
public sealed partial class UpdateService
{
    /// <summary>Depot GitHub prive de TwinCATPlus (proprietaire/depot).</summary>
    public const string Repository = "LBLBRENTA/TwinCATPlus";

    public const string InstallerPrefix = "TwinCATPlus-Setup-";
    public const string ChecksumSuffix = ".sha256";

    /// <summary>Arguments de l'installeur Inno Setup en silencieux.</summary>
    public const string SilentArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";

    private const string LogSource = "Mises à jour";

    /// <summary>Jamais deux recherches automatiques a moins d'une heure d'ecart.</summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(1);

    private readonly GitHubReleasesClient _client;
    private readonly AppLog _log;

    public UpdateService(GitHubReleasesClient client, AppLog log)
    {
        _client = client;
        _log = log;
    }

    public GitHubReleasesClient Client => _client;

    /// <summary>Faut-il une recherche automatique maintenant ?</summary>
    public static bool IsCheckDue(UpdateSettings settings, DateTime now, bool atStartup)
    {
        if (!settings.AutoCheck)
        {
            return false;
        }

        var last = settings.LastCheck;
        if (last is not null && now - last.Value < MinimumInterval)
        {
            return false;
        }

        return settings.Frequency switch
        {
            UpdateFrequency.Daily => atStartup || last is null || now - last.Value >= TimeSpan.FromDays(1),
            UpdateFrequency.Weekly => atStartup || last is null || now - last.Value >= TimeSpan.FromDays(7),
            _ => atStartup,
        };
    }

    /// <summary>
    /// Recherche une version plus recente que <paramref name="current"/>. Une version ignoree n'est proposee
    /// qu'a une recherche manuelle. Null si l'application est a jour.
    /// </summary>
    public async Task<AvailableUpdate?> CheckAsync(SemVersion current, UpdateSettings settings, bool manual, CancellationToken cancellation = default)
    {
        var releases = await _client.GetReleasesAsync(cancellation: cancellation).ConfigureAwait(false);
        var best = SelectUpdate(releases, current, settings.IncludePrereleases);
        if (best is null)
        {
            _log.Info(LogSource, $"Version {current} à jour");
            return null;
        }

        if (!manual && string.Equals(settings.SkippedVersion, best.Version.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _log.Info(LogSource, $"Version {best.Version} disponible mais ignorée à la demande de l'utilisateur");
            return null;
        }

        _log.Info(LogSource, $"Version {best.Version} disponible (version installée {current})");
        return best;
    }

    /// <summary>Plus haute release superieure a <paramref name="current"/> qui porte un installeur et son empreinte.</summary>
    public static AvailableUpdate? SelectUpdate(IEnumerable<GitHubRelease> releases, SemVersion current, bool includePrereleases)
    {
        AvailableUpdate? best = null;
        foreach (var release in releases)
        {
            if (release.Draft || (release.Prerelease && !includePrereleases) || !SemVersion.TryParse(release.TagName, out var version))
            {
                continue;
            }

            if (version.IsTestVersion && !includePrereleases)
            {
                continue;
            }

            var installer = release.FindAsset(n => n.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
                && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            var checksum = installer is null ? null : release.FindAsset(n => n.Equals(installer.Name + ChecksumSuffix, StringComparison.OrdinalIgnoreCase));
            if (installer is null || checksum is null || version <= current || (best is not null && version <= best.Version))
            {
                continue;
            }

            best = new AvailableUpdate(version, release, installer, checksum);
        }

        if (best is null)
        {
            return null;
        }

        var changesExtension = releases.Any(r => SemVersion.TryParse(r.TagName, out var v) && v > current && v <= best.Version
            && ExtensionHeading().IsMatch(r.Body ?? string.Empty));
        return best with { ChangesExtension = changesExtension };
    }

    /// <summary>Telecharge l'installeur et verifie son empreinte. Renvoie le chemin de l'installeur.</summary>
    public async Task<string> DownloadAsync(AvailableUpdate update, string folder, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var expected = ParseChecksum(await _client.ReadAssetTextAsync(update.Checksum, cancellation).ConfigureAwait(false));
        if (expected is null)
        {
            throw new UpdateException(UpdateFailure.ChecksumUnreadable, $"Empreinte {update.Checksum.Name} illisible");
        }

        var target = Path.Combine(folder, update.Installer.Name);
        _log.Info(LogSource, $"Téléchargement de {update.Installer.Name} vers {folder}");
        await _client.DownloadAssetAsync(update.Installer, target, progress, cancellation).ConfigureAwait(false);
        var actual = ComputeSha256(target);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new UpdateException(UpdateFailure.ChecksumMismatch,
                $"Empreinte fausse pour {update.Installer.Name} : attendue {expected}, obtenue {actual}, fichier supprimé");
        }

        _log.Info(LogSource, $"Empreinte SHA-256 vérifiée pour {update.Installer.Name}");
        return target;
    }

    /// <summary>
    /// Lance l'installeur en silencieux, sans elevation : il la demande lui-meme (PrivilegesRequired=admin) et garde
    /// ainsi l'utilisateur d'origine pour relancer l'application sans droits administrateur (runasoriginaluser).
    /// Faux si l'installeur n'a pas pu etre lance.
    /// </summary>
    public bool StartInstaller(string installer)
    {
        try
        {
            Process.Start(new ProcessStartInfo(installer, SilentArguments) { UseShellExecute = true })?.Dispose();
            _log.Info(LogSource, $"Installeur lancé : {installer} {SilentArguments}");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Warning(LogSource, $"Installeur non lancé ({ex.Message})");
            return false;
        }
    }

    /// <summary>Empreinte d'un fichier .sha256 : "empreinte" ou "empreinte  nom" (format sha256sum).</summary>
    public static string? ParseChecksum(string text)
    {
        var first = text.Split((char[])[' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"^###\s+" + Changelog.SectionExtension + @"\s*$", RegexOptions.Multiline)]
    private static partial Regex ExtensionHeading();

    public static string ComputeSha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

public enum UpdateFailure
{
    ChecksumUnreadable,
    ChecksumMismatch,
}

public sealed class UpdateException : Exception
{
    public UpdateException(UpdateFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public UpdateFailure Failure { get; }
}
