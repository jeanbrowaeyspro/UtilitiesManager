using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtilitiesManager.GitHub;

/// <summary>Code a saisir par l'utilisateur sur la page GitHub de connexion d'un appareil.</summary>
public sealed record DeviceCode(
    [property: JsonPropertyName("device_code")] string Code,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval);

/// <summary>Connexion du compte GitHub de l'utilisateur, gardee chiffree sur le poste.</summary>
public sealed record GitHubCredential(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("login")] string Login)
{
    /// <summary>Jeton expire ou sur le point de l'etre (marge d'une minute).</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } end && now >= end - TimeSpan.FromMinutes(1);
}

public enum SignInFailure
{
    /// <summary>Client ID de l'application GitHub non pose dans cette version.</summary>
    NotConfigured,
    /// <summary>L'utilisateur a refuse l'autorisation sur GitHub.</summary>
    Denied,
    /// <summary>Le code a expire avant l'autorisation.</summary>
    Expired,
    /// <summary>Jeton de renouvellement refuse : nouvelle connexion necessaire.</summary>
    RefreshRefused,
}

public sealed class SignInException(SignInFailure failure, string message) : Exception(message)
{
    public SignInFailure Failure { get; } = failure;
}

/// <summary>
/// Connexion a GitHub par le flux d'appareil de l'application GitHub de UtilitiesManager : code affiche,
/// autorisation dans le navigateur, attente du jeton. Aucun secret embarque : seul le Client ID (public) est connu.
/// </summary>
public sealed class GitHubSignIn : GitHubHttpClient
{
    /// <summary>Client ID public de l'application GitHub de UtilitiesManager ; vide tant qu'elle n'est pas creee.</summary>
    public const string ApplicationClientId = "Iv23liZla5crbTyuBEHK";

    /// <summary>Nom public de l'application GitHub dans son adresse (github.com/apps/...) ; vide tant qu'elle n'est pas creee.</summary>
    public const string ApplicationSlug = "utilitiesmanager";

    /// <summary>Page GitHub qui installe l'application sur les depots choisis par l'utilisateur.</summary>
    public static string InstallationUrl => $"https://github.com/apps/{ApplicationSlug}/installations/new";

    private const string TokenUrl = "https://github.com/login/oauth/access_token";

    private readonly string _clientId;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">Attente entre deux interrogations, remplacee dans les tests.</param>
    public GitHubSignIn(string userAgent, string clientId = ApplicationClientId, HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : base(string.Empty, userAgent, handler, null)
    {
        _clientId = clientId;
        _delay = delay ?? Task.Delay;
    }

    public bool IsConfigured => _clientId.Length > 0;

    /// <summary>Demande un code d'appareil a afficher a l'utilisateur.</summary>
    public Task<DeviceCode> RequestCodeAsync(CancellationToken cancellation = default)
    {
        if (!IsConfigured)
        {
            throw new SignInException(SignInFailure.NotConfigured, "Connexion à GitHub non configurée (Client ID de l'application absent)");
        }

        return SendJsonAsync<DeviceCode>(HttpMethod.Post, "https://github.com/login/device/code", cancellation,
            Form(("client_id", _clientId)), authenticated: false);
    }

    /// <summary>Interroge GitHub jusqu'a l'autorisation, au rythme demande (ralenti sur "slow_down").</summary>
    public async Task<GitHubCredential> WaitForTokenAsync(DeviceCode code, CancellationToken cancellation = default)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(code.Interval, 1));
        var deadline = DateTimeOffset.Now.AddSeconds(code.ExpiresIn);
        while (true)
        {
            await _delay(interval, cancellation).ConfigureAwait(false);
            var answer = await PostTokenAsync(cancellation,
                ("client_id", _clientId), ("device_code", code.Code), ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"))
                .ConfigureAwait(false);
            switch (answer.Error)
            {
                case null:
                    return answer.ToCredential(string.Empty);
                case "authorization_pending" when DateTimeOffset.Now < deadline:
                    continue;
                case "slow_down":
                    interval = answer.Interval > 0 ? TimeSpan.FromSeconds(answer.Interval) : interval + TimeSpan.FromSeconds(5);
                    continue;
                case "access_denied":
                    throw new SignInException(SignInFailure.Denied, "Autorisation refusée sur GitHub");
                case "expired_token" or "authorization_pending":
                    throw new SignInException(SignInFailure.Expired, "Code de connexion expiré");
                default:
                    throw new GitHubException(GitHubFailure.BadResponse, $"Connexion à GitHub refusée : {answer.Error}");
            }
        }
    }

    /// <summary>Renouvelle un jeton expire avec le jeton de renouvellement.</summary>
    public async Task<GitHubCredential> RefreshAsync(GitHubCredential credential, CancellationToken cancellation = default)
    {
        if (!IsConfigured || credential.RefreshToken is not { Length: > 0 } refresh)
        {
            throw new SignInException(SignInFailure.RefreshRefused, "Connexion expirée sans jeton de renouvellement");
        }

        var answer = await PostTokenAsync(cancellation,
            ("client_id", _clientId), ("grant_type", "refresh_token"), ("refresh_token", refresh)).ConfigureAwait(false);
        return answer.Error is null
            ? answer.ToCredential(credential.Login)
            : throw new SignInException(SignInFailure.RefreshRefused, $"Renouvellement de la connexion refusé : {answer.Error}");
    }

    private Task<TokenAnswer> PostTokenAsync(CancellationToken cancellation, params (string Key, string Value)[] fields) =>
        SendJsonAsync<TokenAnswer>(HttpMethod.Post, TokenUrl, cancellation, Form(fields), authenticated: false);

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    /// <summary>Reponse du point d'acces des jetons : jeton, ou erreur dans un statut 200.</summary>
    private sealed record TokenAnswer(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("interval")] int Interval)
    {
        public GitHubCredential ToCredential(string login) =>
            AccessToken is { Length: > 0 } token
                ? new(token, RefreshToken, ExpiresIn > 0 ? DateTimeOffset.Now.AddSeconds(ExpiresIn) : null, login)
                : throw new GitHubException(GitHubFailure.BadResponse, "Réponse de connexion sans jeton");
    }
}
