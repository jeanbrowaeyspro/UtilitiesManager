using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtilitiesManager.GitHub;

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("url")] string ApiUrl);

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("html_url")] string? PageUrl,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets)
{
    /// <summary>Identifiant de la Release (depot de fichiers, suppression).</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    public GitHubAsset? FindAsset(Func<string, bool> predicate) => Assets.FirstOrDefault(a => predicate(a.Name));
}

public enum GitHubFailure
{
    /// <summary>Aucun jeton : requete impossible (recherche des mises a jour desactivee, compte non connecte).</summary>
    NoToken,
    Network,
    Timeout,
    /// <summary>Jeton refuse ou expire (401, 403 hors limite).</summary>
    Unauthorized,
    RateLimited,
    /// <summary>Depot introuvable ou jeton sans acces au depot.</summary>
    NotFound,
    BadResponse,
    Cancelled,
}

public sealed class GitHubException : Exception
{
    public GitHubException(GitHubFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    public GitHubFailure Failure { get; }
}

/// <summary>
/// Client d'un depot GitHub : Releases (mise a jour de UtilitiesManager, logiciels publies sur GitHub) et fichiers
/// (profil prive, lu et modifie avec le jeton du compte connecte).
/// </summary>
public sealed class GitHubRepositoryClient : GitHubHttpClient
{
    private const string Api = "https://api.github.com";

    private readonly string _repository;

    /// <param name="repository">"proprietaire/depot".</param>
    /// <param name="handler">Remplace la pile reseau dans les tests.</param>
    public GitHubRepositoryClient(string repository, string token, string userAgent, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
        : base(token, userAgent, handler, timeout)
    {
        _repository = repository;
    }

    public string ReleasesPageUrl => $"https://github.com/{_repository}/releases";

    private string RepositoryUrl => $"{Api}/repos/{_repository}";

    /// <summary>Releases publiees (brouillons exclus), plus recentes d'abord, au plus <paramref name="count"/>.</summary>
    public async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(int count = 30, CancellationToken cancellation = default)
    {
        var releases = await SendJsonAsync<List<GitHubRelease>>(HttpMethod.Get, $"{RepositoryUrl}/releases?per_page={count}", cancellation)
            .ConfigureAwait(false);
        return releases.Where(r => !r.Draft).ToList();
    }

    /// <summary>Telecharge un fichier joint dans <paramref name="destination"/> (ecrase).</summary>
    public async Task DownloadAssetAsync(GitHubAsset asset, string destination, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Get, asset.ApiUrl, "application/octet-stream", cancellation, whole: false)
            .ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            await using var target = File.Create(destination);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                done += read;
                if (total > 0)
                {
                    progress?.Report((double)done / total);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new GitHubException(GitHubFailure.Network, $"Téléchargement de {asset.Name} interrompu : {ex.Message}", ex);
        }
    }

    /// <summary>Contenu texte d'un fichier joint (empreinte .sha256, versions.json).</summary>
    public async Task<string> ReadAssetTextAsync(GitHubAsset asset, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Get, asset.ApiUrl, "application/octet-stream", cancellation).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }

    /// <summary>Identifiant du compte qui porte le jeton.</summary>
    public async Task<string> GetUserLoginAsync(CancellationToken cancellation = default)
    {
        var user = await SendJsonAsync<JsonElement>(HttpMethod.Get, $"{Api}/user", cancellation).ConfigureAwait(false);
        return user.TryGetProperty("login", out var login) ? login.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>Vrai si le depot existe et que le jeton y a acces (sinon GitHub repond 404 dans les deux cas).</summary>
    public async Task<bool> ExistsAsync(CancellationToken cancellation = default)
    {
        try
        {
            await SendJsonAsync<JsonElement>(HttpMethod.Get, RepositoryUrl, cancellation).ConfigureAwait(false);
            return true;
        }
        catch (GitHubException ex) when (ex.Failure == GitHubFailure.NotFound)
        {
            return false;
        }
    }

    private string ContentsUrl(string path) =>
        $"{RepositoryUrl}/contents/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";

    /// <summary>Contenu brut d'un fichier du depot (branche par defaut), jusqu'a 100 Mo.</summary>
    public async Task<byte[]> GetFileAsync(string path, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Get, ContentsUrl(path), "application/vnd.github.raw+json", cancellation)
            .ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
    }

    /// <summary>Empreinte git (sha) du fichier, necessaire pour le remplacer ; null s'il n'existe pas.</summary>
    public async Task<string?> GetFileShaAsync(string path, CancellationToken cancellation = default)
    {
        try
        {
            var file = await SendJsonAsync<JsonElement>(HttpMethod.Get, ContentsUrl(path), cancellation).ConfigureAwait(false);
            return file.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
        }
        catch (GitHubException ex) when (ex.Failure == GitHubFailure.NotFound)
        {
            return null;
        }
    }

    /// <summary>Cree ou remplace un fichier du depot par un commit sur la branche par defaut.</summary>
    public async Task PutFileAsync(string path, byte[] content, string message, CancellationToken cancellation = default)
    {
        var sha = await GetFileShaAsync(path, cancellation).ConfigureAwait(false);
        using (await SendAsync(HttpMethod.Put, ContentsUrl(path), "application/vnd.github+json", cancellation,
            JsonContent.Create(new { message, content = Convert.ToBase64String(content), sha })).ConfigureAwait(false))
        {
        }
    }
}
