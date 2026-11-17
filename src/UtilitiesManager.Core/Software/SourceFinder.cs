using System.Text.RegularExpressions;
using UtilitiesManager.GitHub;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>
/// Source de mise à jour proposée pour un logiciel relevé : entrée du profil préremplie. <see cref="Sure"/> : identifiant
/// winget ou Store exact, ajoutée sans passer par l'éditeur.
/// </summary>
public sealed record SourceCandidate(ProgramEntry Entry, string Label, bool Sure = false);

/// <summary>
/// Recherche d'où mettre à jour un logiciel relevé : winget, puis paquet Chocolatey qui embarque l'installeur, puis
/// dépôt GitHub du même nom ; à défaut, page de téléchargement à compléter.
/// </summary>
public sealed class SourceFinder(Winget winget, GitHubSearchClient search, Func<string, GitHubRepositoryClient> github)
{
    /// <summary>Fichiers d'installation reconnus dans une release GitHub.</summary>
    private static readonly Regex InstallerAsset = new(@"\.(exe|msi|zip)$", RegexOptions.IgnoreCase);

    /// <summary>Fichiers d'une autre plateforme ou annexes (sommes de contrôle, signatures, symboles).</summary>
    private static readonly Regex OtherAsset = new(@"arm|linux|mac|darwin|osx|\.sig|sha\d|checksum|symbols|debug|source", RegexOptions.IgnoreCase);

    /// <summary>Candidats trouvés, le plus probable d'abord ; jamais vide (page à compléter en dernier).</summary>
    public async Task<IReadOnlyList<SourceCandidate>> FindAsync(FoundSoftware found, Profile profile, CancellationToken cancel = default)
    {
        if (SureCandidate(found, profile) is { } sure)
            return [sure];
        var candidates = new List<SourceCandidate>();
        var name = Inventory.Clean(found.Name);
        await Try("winget", cancel, async () =>
        {
            foreach (var package in await winget.SearchAsync(name, cancel).ConfigureAwait(false))
            {
                if (package.Source.ToLowerInvariant() is Sources.Winget or Sources.MsStore && SameName(package.Name, found.Name))
                    candidates.Add(FromPackage(package, profile));
            }
        }).ConfigureAwait(false);
        await Try("Chocolatey", cancel, async () =>
        {
            foreach (var package in await Chocolatey.SearchAsync(name, cancel).ConfigureAwait(false))
            {
                if (package.EmbedsInstaller && (SameName(package.Title, found.Name) || SameName(package.Id, found.Name)))
                    candidates.Add(new SourceCandidate(Entry(found, profile, Sources.Chocolatey, package.Id, e => e.InstallerPattern = @"\.(exe|msi)$"),
                                                       L.T("Chocolatey: {0}", package.Id)));
            }
        }).ConfigureAwait(false);
        await Try("GitHub", cancel, async () =>
        {
            if (await FromGitHubAsync(found, profile, cancel).ConfigureAwait(false) is { } candidate)
                candidates.Add(candidate);
        }).ConfigureAwait(false);
        candidates.Add(new SourceCandidate(Entry(found, profile, Sources.Download, null, _ => { }), L.T("Download page (to fill in)")));
        return candidates;
    }

    /// <summary>Paquet winget ou Store exact, connu sans recherche ; null sinon.</summary>
    public static SourceCandidate? SureCandidate(FoundSoftware found, Profile profile) =>
        found.IsSure ? FromPackage(new WingetPackage(found.Name, found.WingetId!, "", "", found.WingetSource!), profile) with { Sure = true } : null;

    /// <summary>Entrée winget ou Microsoft Store d'un paquet (recherche, relevé).</summary>
    public static SourceCandidate FromPackage(WingetPackage package, Profile profile)
    {
        var store = package.Source.Equals(Sources.MsStore, StringComparison.OrdinalIgnoreCase);
        var entry = new ProgramEntry
        {
            Id = UniqueId(package.Id, profile),
            Name = package.Name,
            Source = store ? Sources.MsStore : Sources.Winget,
            Package = package.Id,
            Detect = store ? new Detection { WingetId = "^" + Regex.Escape(package.Id) + "$" } : null,
        };
        return new SourceCandidate(entry, L.T("winget: {0}", package.Id));
    }

    /// <summary>
    /// Dépôt GitHub dont le nom est celui du logiciel (le plus suivi) et dont la dernière release contient un
    /// installeur Windows ; motif du fichier déduit de son nom, numéros de version remplacés.
    /// </summary>
    private async Task<SourceCandidate?> FromGitHubAsync(FoundSoftware found, Profile profile, CancellationToken cancel)
    {
        var repositories = await search.SearchRepositoriesAsync(Inventory.Clean(found.Name), cancel).ConfigureAwait(false);
        foreach (var repository in repositories.Where(r => !r.Archived && SameName(r.Name, found.Name)).OrderByDescending(r => r.Stars).Take(2))
        {
            using var client = github(repository.FullName);
            var releases = await client.GetReleasesAsync(5, cancel).ConfigureAwait(false);
            if (releases.FirstOrDefault(r => !r.Prerelease) is { } release && AssetPattern(release) is { } pattern)
                return new SourceCandidate(Entry(found, profile, Sources.GitHub, repository.FullName, e => e.AssetPattern = pattern),
                                           L.T("GitHub: {0}", repository.FullName));
        }
        return null;
    }

    /// <summary>Motif du fichier d'installation Windows de la release, de préférence 64 bits ; null s'il n'y en a pas.</summary>
    public static string? AssetPattern(GitHubRelease release)
    {
        var assets = release.Assets.Where(a => InstallerAsset.IsMatch(a.Name) && !OtherAsset.IsMatch(a.Name)).ToList();
        var asset = assets.FirstOrDefault(a => Regex.IsMatch(a.Name, "x64|win64|amd64|64bit", RegexOptions.IgnoreCase))
                    ?? assets.FirstOrDefault(a => !Regex.IsMatch(a.Name, "x86|win32|32bit", RegexOptions.IgnoreCase))
                    ?? assets.FirstOrDefault();
        return asset is null ? null : "^" + Regex.Replace(Regex.Escape(asset.Name), @"v?\d+(?:\\\.\d+)+", @"v?[\d.]+") + "$";
    }

    /// <summary>Entrée préremplie : identifiant, nom et détection tirés du logiciel relevé.</summary>
    private static ProgramEntry Entry(FoundSoftware found, Profile profile, string source, string? package, Action<ProgramEntry> fill)
    {
        var name = Inventory.Clean(found.Name);
        var entry = new ProgramEntry
        {
            Id = UniqueId(name, profile),
            Name = name,
            Source = source,
            Package = package,
            InstallDir = found.Origin == FoundOrigin.Folder ? found.InstallDir : null,
            Detect = found.Exe is { } exe ? new Detection { File = exe } : new Detection { DisplayName = "^" + Regex.Escape(name) },
        };
        fill(entry);
        return entry;
    }

    /// <summary>Identifiant du profil tiré d'un nom ("Notepad++" → "notepad"), suffixé s'il est déjà pris.</summary>
    public static string UniqueId(string text, Profile profile)
    {
        var id = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9.-]+", "-").Trim('-', '.');
        if (id.Length == 0)
            id = "logiciel";
        var unique = id;
        for (var n = 2; profile.Find(unique) is not null; n++)
            unique = $"{id}-{n}";
        return unique;
    }

    private static bool SameName(string a, string b) => Inventory.Normalize(a) is { Length: > 0 } x && x == Inventory.Normalize(b);

    /// <summary>Une source injoignable (hors ligne, limite de requêtes) n'empêche pas les suivantes.</summary>
    private static async Task Try(string source, CancellationToken cancel, Func<Task> search)
    {
        try
        {
            await search().ConfigureAwait(false);
        }
        catch (Exception exc) when (exc is HttpRequestException or GitHubException or TaskCanceledException or InvalidOperationException
                                        or System.Xml.XmlException && !cancel.IsCancellationRequested)
        {
            Log.Info($"Relevé, recherche {source} : {exc.Message}");
        }
    }
}
