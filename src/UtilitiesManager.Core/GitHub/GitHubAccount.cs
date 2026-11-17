namespace UtilitiesManager.GitHub;

/// <summary>
/// Compte GitHub connecté dans UtilitiesManager : connexion par le flux d'appareil, jeton gardé chiffré (DPAPI),
/// renouvelé quand il expire, déconnexion. Sert à lire et modifier le profil privé.
/// </summary>
public sealed class GitHubAccount
{
    private readonly GitHubCredentialStore _store;
    private readonly GitHubSignIn _signIn;
    private readonly Func<string, Func<CancellationToken, ValueTask<string>>, GitHubRepositoryClient> _clientFor;
    private readonly SemaphoreSlim _refresh = new(1);
    private GitHubCredential? _credential;

    /// <param name="clientFor">Client d'un dépôt (dépôt, lecture du jeton).</param>
    public GitHubAccount(GitHubCredentialStore store, GitHubSignIn signIn,
                         Func<string, Func<CancellationToken, ValueTask<string>>, GitHubRepositoryClient> clientFor)
    {
        _store = store;
        _signIn = signIn;
        _clientFor = clientFor;
        _credential = store.Load();
    }

    public bool IsConfigured => _signIn.IsConfigured;

    public bool IsSignedIn => _credential is not null;

    public string Login => _credential?.Login ?? string.Empty;

    /// <summary>Connexion ou déconnexion : pages et actions à mettre à jour.</summary>
    public event Action? Changed;

    public Task<DeviceCode> RequestCodeAsync(CancellationToken cancellation = default) => _signIn.RequestCodeAsync(cancellation);

    /// <summary>Attend l'autorisation sur GitHub, lit le compte et garde la connexion chiffrée.</summary>
    public async Task SignInAsync(DeviceCode code, CancellationToken cancellation = default)
    {
        var credential = await _signIn.WaitForTokenAsync(code, cancellation).ConfigureAwait(false);
        using (var client = _clientFor(string.Empty, _ => ValueTask.FromResult(credential.AccessToken)))
        {
            credential = credential with { Login = await client.GetUserLoginAsync(cancellation).ConfigureAwait(false) };
        }

        _store.Save(credential);
        _credential = credential;
        Log.Info($"Connecté à GitHub : {credential.Login}");
        Changed?.Invoke();
    }

    public void SignOut()
    {
        _store.Delete();
        _credential = null;
        Log.Info("Déconnecté de GitHub");
        Changed?.Invoke();
    }

    /// <summary>
    /// Client d'un dépôt avec le jeton du compte, renouvelé avant chaque requête s'il a expiré (requêtes anonymes si
    /// déconnecté).
    /// </summary>
    public GitHubRepositoryClient CreateClient(string repository) => _clientFor(repository, TokenAsync);

    private async ValueTask<string> TokenAsync(CancellationToken cancellation)
    {
        await EnsureFreshAsync(cancellation).ConfigureAwait(false);
        return _credential?.AccessToken ?? string.Empty;
    }

    /// <summary>
    /// Renouvelle le jeton s'il a expiré. Un seul renouvellement à la fois : le jeton de renouvellement ne sert
    /// qu'une fois. Renouvellement refusé : déconnexion, l'utilisateur doit se reconnecter.
    /// </summary>
    public async Task EnsureFreshAsync(CancellationToken cancellation = default)
    {
        if (_credential?.IsExpired(DateTimeOffset.Now) != true)
            return;
        await _refresh.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_credential is { } credential && credential.IsExpired(DateTimeOffset.Now))
            {
                _credential = await _signIn.RefreshAsync(credential, cancellation).ConfigureAwait(false);
                _store.Save(_credential);
                Log.Info("Connexion GitHub renouvelée");
            }
        }
        catch (SignInException ex)
        {
            Log.Error(ex.Message);
            SignOut();
        }
        finally
        {
            _refresh.Release();
        }
    }
}
