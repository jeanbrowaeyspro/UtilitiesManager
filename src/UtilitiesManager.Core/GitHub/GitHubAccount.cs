
namespace UtilitiesManager.GitHub;

/// <summary>
/// Compte GitHub connecte dans TwinCATPlus : connexion par le flux d'appareil, jeton garde chiffre, renouvele
/// quand il expire, deconnexion. Sert a lire et publier les bibliotheques.
/// </summary>
public sealed class GitHubAccount
{
    private const string LogSource = "Bibliothèques";

    private readonly GitHubCredentialStore _store;
    private readonly GitHubSignIn _signIn;
    private readonly AppLog _log;
    private readonly Func<string, string, GitHubReleasesClient> _clientFor;
    private readonly Func<string, string, GitHubRepositoryClient> _repositoryClientFor;
    private GitHubCredential? _credential;

    /// <param name="clientFor">Client des Releases (depot, jeton), meme User-Agent que la coquille.</param>
    /// <param name="repositoryClientFor">Client des depots (depot, jeton), par defaut sans numero de version dans le User-Agent.</param>
    public GitHubAccount(GitHubCredentialStore store, GitHubSignIn signIn, AppLog log, Func<string, string, GitHubReleasesClient> clientFor,
        Func<string, string, GitHubRepositoryClient>? repositoryClientFor = null)
    {
        _store = store;
        _signIn = signIn;
        _log = log;
        _clientFor = clientFor;
        _repositoryClientFor = repositoryClientFor ?? ((repository, token) => new GitHubRepositoryClient(repository, token, AppPaths.ProductName));
        _credential = store.Load();
    }

    public bool IsConfigured => _signIn.IsConfigured;

    public bool IsSignedIn => _credential is not null;

    public string Login => _credential?.Login ?? string.Empty;

    /// <summary>Jeton du compte, pour les operations git sur les copies locales (vide si deconnecte).</summary>
    public string AccessToken => _credential?.AccessToken ?? string.Empty;

    /// <summary>Connexion ou deconnexion : pages et actions a mettre a jour.</summary>
    public event Action? Changed;

    public Task<DeviceCode> RequestCodeAsync(CancellationToken cancellation = default) => _signIn.RequestCodeAsync(cancellation);

    /// <summary>Attend l'autorisation sur GitHub, lit le compte et garde la connexion chiffree.</summary>
    public async Task SignInAsync(DeviceCode code, CancellationToken cancellation = default)
    {
        var credential = await _signIn.WaitForTokenAsync(code, cancellation).ConfigureAwait(false);
        using (var client = _clientFor(string.Empty, credential.AccessToken))
        {
            credential = credential with { Login = await client.GetUserLoginAsync(cancellation).ConfigureAwait(false) };
        }

        _store.Save(credential);
        _credential = credential;
        _log.Info(LogSource, $"Connecté à GitHub : {credential.Login}");
        Changed?.Invoke();
    }

    public void SignOut()
    {
        _store.Delete();
        _credential = null;
        _log.Info(LogSource, "Déconnecté de GitHub");
        Changed?.Invoke();
    }

    /// <summary>Client d'un depot avec le jeton du compte (vide si deconnecte : echec NoToken a la premiere requete).</summary>
    public GitHubReleasesClient CreateClient(string repository) => _clientFor(repository, AccessToken);

    /// <summary>Client des depots avec le jeton du compte, <paramref name="repository"/> vide pour les requetes du compte.</summary>
    public GitHubRepositoryClient CreateRepositoryClient(string repository) => _repositoryClientFor(repository, AccessToken);

    /// <summary>
    /// Renouvelle le jeton s'il a expire, avant une serie de requetes. Renouvellement refuse : deconnexion,
    /// l'utilisateur doit se reconnecter.
    /// </summary>
    public async Task EnsureFreshAsync(CancellationToken cancellation = default)
    {
        if (_credential is { } credential && credential.IsExpired(DateTimeOffset.Now))
        {
            try
            {
                _credential = await _signIn.RefreshAsync(credential, cancellation).ConfigureAwait(false);
                _store.Save(_credential);
                _log.Info(LogSource, "Connexion GitHub renouvelée");
            }
            catch (SignInException ex)
            {
                _log.Warning(LogSource, ex.Message);
                SignOut();
            }
        }
    }
}
