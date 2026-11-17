using System.Text.Json.Serialization;

namespace UtilitiesManager.GitHub;

public sealed record GitHubRepository(
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("stargazers_count")] int Stars,
    [property: JsonPropertyName("archived")] bool Archived);

/// <summary>
/// Recherche de dépôts publics sur GitHub. Sans compte connecté, GitHub limite la recherche à 10 requêtes par minute.
/// </summary>
public sealed class GitHubSearchClient(Func<CancellationToken, ValueTask<string>> token, string userAgent, HttpMessageHandler? handler = null)
    : GitHubHttpClient(token, userAgent, handler, null)
{
    private sealed record SearchResult([property: JsonPropertyName("items")] IReadOnlyList<GitHubRepository> Items);

    /// <summary>Dépôts dont le nom contient <paramref name="query"/>, les plus suivis d'abord.</summary>
    public async Task<IReadOnlyList<GitHubRepository>> SearchRepositoriesAsync(string query, CancellationToken cancellation = default)
    {
        var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query + " in:name")}&sort=stars&per_page=10";
        var result = await SendJsonAsync<SearchResult>(HttpMethod.Get, url, cancellation).ConfigureAwait(false);
        return result.Items;
    }
}
