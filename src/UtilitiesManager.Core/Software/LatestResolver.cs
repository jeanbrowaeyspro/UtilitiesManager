using System.Text.RegularExpressions;
using UtilitiesManager.GitHub;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>
/// Dernière version publiée : numéro, adresse du fichier à télécharger si elle est déjà connue, date de
/// publication si la source la donne (sinon l'âge part de la première fois où la version est vue sur ce PC).
/// </summary>
public sealed record LatestInfo(string Version, string? DownloadUrl = null, DateTimeOffset? Published = null, GitHubAsset? Asset = null);

/// <summary>Recherche de la dernière version d'un logiciel du profil, selon sa source.</summary>
public sealed class LatestResolver(ShellCommands shell, Func<string, GitHubRepositoryClient> github)
{
    /// <summary>Null si la source ne permet pas de la connaître (le logiciel n'est alors que installé, jamais mis à jour).</summary>
    public async Task<LatestInfo?> ResolveAsync(ProgramEntry program, IReadOnlyList<WingetPackage> winget, InstalledInfo installed,
                                                CancellationToken cancel = default)
    {
        switch (program.Source)
        {
            case Sources.Winget or Sources.MsStore:
                var package = Winget.Match(program, winget);
                if (package is null)
                    return null;
                return package.Available.Length > 0 ? new LatestInfo(package.Available) : installed.Version is { } v ? new LatestInfo(v) : null;
            case Sources.Chocolatey:
                var release = await Chocolatey.GetLatestAsync(program.Package!, cancel).ConfigureAwait(false);
                return new LatestInfo(release.Version, release.PackageUrl);
            case Sources.GitHub:
                return await FromGitHubAsync(program, cancel).ConfigureAwait(false);
        }
        if (program.Latest?.Url is { } page)
            return await FromPageAsync(page, program.Latest.Regex!, cancel).ConfigureAwait(false);
        if (program.Latest?.Command is { } command)
        {
            var output = await shell.ReadAsync(ShellCommands.Expand(command), cancel).ConfigureAwait(false);
            var found = Detector.Match(output, program.Latest.Regex);
            // Motif absent de la sortie (pip --outdated ne liste que les paquets en retard) : à jour.
            return found.Version is { } latest ? new LatestInfo(latest) : installed.Version is { } current ? new LatestInfo(current) : null;
        }
        return program.Version is { } fixedVersion ? new LatestInfo(fixedVersion) : null;
    }

    private async Task<LatestInfo?> FromGitHubAsync(ProgramEntry program, CancellationToken cancel)
    {
        using var client = github(program.Package!);
        var releases = await client.GetReleasesAsync(30, cancel).ConfigureAwait(false);
        var pattern = new Regex(program.AssetPattern!, RegexOptions.IgnoreCase);
        foreach (var release in releases)
        {
            if (release.Prerelease && !program.Prerelease)
                continue;
            if (release.FindAsset(pattern.IsMatch) is { } asset)
                return new LatestInfo(release.TagName.TrimStart('v', 'V'), null, release.PublishedAt, asset);
        }
        return null;
    }

    /// <summary>
    /// Page de l'éditeur lue par un motif : premier groupe = version, groupe nommé <c>url</c> (ou correspondance
    /// entière si elle ressemble à un chemin) = fichier à télécharger.
    /// </summary>
    public static async Task<LatestInfo?> FromPageAsync(string page, string pattern, CancellationToken cancel)
    {
        var text = await Web.GetStringAsync(page, cancel).ConfigureAwait(false);
        return ParsePage(page, text, pattern);
    }

    public static LatestInfo? ParsePage(string page, string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var link = match.Groups["url"].Success ? match.Groups["url"].Value
            : match.Value.Contains('/') ? match.Value : null;
        var version = match.Groups.Count > 1 && match.Groups[1].Success && match.Groups[1].Name != "url"
            ? match.Groups[1].Value
            : VersionText.Parse(match.Value) is { } parsed ? VersionText.Display(parsed) : match.Value;
        var url = link is null ? null : new Uri(new Uri(page), System.Net.WebUtility.HtmlDecode(link)).AbsoluteUri;
        return new LatestInfo(version, url);
    }
}
