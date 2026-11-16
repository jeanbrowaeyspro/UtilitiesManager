using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UtilitiesManager.GitHub;

/// <summary>
/// Base des clients GitHub : HTTPS, User-Agent UtilitiesManager/&lt;version&gt;, delai limite, erreurs classees
/// en <see cref="GitHubFailure"/>. Le jeton est fourni par l'appelant (vide : requetes anonymes, depots publics).
/// </summary>
public abstract class GitHubHttpClient : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    protected static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _token;

    /// <param name="handler">Remplace la pile reseau dans les tests.</param>
    protected GitHubHttpClient(string token, string userAgent, HttpMessageHandler? handler, TimeSpan? timeout)
    {
        _token = token;
        _http = handler is null
            ? new HttpClient(new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials })
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        RequestTimeout = timeout ?? DefaultTimeout;
    }

    public TimeSpan RequestTimeout { get; }

    public bool HasToken => _token.Length > 0;

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Requete JSON, reponse lue et convertie en <typeparamref name="T"/>. Sans authentification (pages de connexion
    /// de github.com), la reponse est demandee en JSON simple.
    /// </summary>
    protected async Task<T> SendJsonAsync<T>(HttpMethod method, string url, CancellationToken cancellation,
        HttpContent? content = null, bool authenticated = true)
    {
        var accept = authenticated ? "application/vnd.github+json" : "application/json";
        using var response = await SendAsync(method, url, accept, cancellation, content, authenticated).ConfigureAwait(false);
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellation).ConfigureAwait(false)
                ?? throw new JsonException("Réponse vide");
        }
        catch (JsonException ex)
        {
            throw new GitHubException(GitHubFailure.BadResponse, "Réponse illisible de GitHub", ex);
        }
    }

    /// <summary>
    /// Envoie une requete et renvoie la reponse si elle est un succes, sinon leve une <see cref="GitHubException"/>.
    /// <paramref name="whole"/> faux : delai non limite et reponse lue au fil de l'eau (telechargement, depot de fichier).
    /// </summary>
    protected async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accept, CancellationToken cancellation,
        HttpContent? content = null, bool authenticated = true, bool whole = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        if (whole)
        {
            timeout.CancelAfter(RequestTimeout);
        }

        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        if (authenticated)
        {
            if (HasToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request,
                whole ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested)
        {
            throw new GitHubException(GitHubFailure.Cancelled, "Opération annulée", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new GitHubException(GitHubFailure.Timeout, $"GitHub ne répond pas (délai de {RequestTimeout.TotalSeconds:0} s dépassé)", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubException(GitHubFailure.Network, $"GitHub injoignable : {ex.Message}", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var status = (int)response.StatusCode;
        var rateLimited = response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0";
        response.Dispose();
        throw status switch
        {
            401 => new GitHubException(GitHubFailure.Unauthorized, "Jeton GitHub refusé ou expiré (401)"),
            403 or 429 when rateLimited || status == 429 => new GitHubException(GitHubFailure.RateLimited, $"GitHub limite le nombre de requêtes ({status})"),
            403 => new GitHubException(GitHubFailure.Unauthorized, "Jeton GitHub refusé (403)"),
            404 => new GitHubException(GitHubFailure.NotFound, "Dépôt GitHub introuvable ou inaccessible avec ce jeton (404)"),
            _ => new GitHubException(GitHubFailure.BadResponse, $"GitHub a répondu par une erreur ({status})"),
        };
    }
}
