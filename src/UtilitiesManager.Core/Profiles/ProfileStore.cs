using System.Text;
using UtilitiesManager.GitHub;

namespace UtilitiesManager.Profiles;

/// <summary>Profil chargé : depuis GitHub ou la copie locale, avec l'erreur rencontrée éventuelle.</summary>
/// <summary>Profil lu ; <paramref name="RepositoryMissing"/> : dépôt du compte introuvable ou sans accès pour l'app.</summary>
public sealed record ProfileLoad(Profile? Profile, bool FromCache, string? Error, bool RepositoryMissing = false);

/// <summary>État du dépôt du profil sur le compte connecté.</summary>
public enum ProfileAccess
{
    Ready,
    /// <summary>Dépôt accessible, sans fichier de profil.</summary>
    NoProfileFile,
    /// <summary>Dépôt absent, ou application GitHub non installée sur ce dépôt.</summary>
    NoRepository,
}

/// <summary>
/// Profil rangé dans le dépôt privé <see cref="RepositoryName"/> du compte GitHub connecté : lecture et écriture
/// par l'API GitHub (chaque modification est un commit), copie locale du dernier profil valide pour travailler hors
/// ligne, import manuel d'un fichier.
/// </summary>
/// <param name="owner">Compte GitHub connecté, vide si aucun.</param>
public sealed class ProfileStore(AppPaths paths, Func<string> owner, Func<string, GitHubRepositoryClient> clientFor)
{
    public const string RepositoryName = "UtilitiesProfile";
    public const string FileName = "profile.json";
    /// <summary>Préfixe d'une adresse qui désigne un fichier du dépôt du profil.</summary>
    public const string ProfileScheme = "profile:";

    /// <summary>Page GitHub de création du dépôt, nom et visibilité privée déjà remplis.</summary>
    public const string CreationUrl = $"https://github.com/new?name={RepositoryName}&visibility=private";

    /// <summary>"compte/UtilitiesProfile", null si aucun compte n'est connecté.</summary>
    public string? Repository => owner() is { Length: > 0 } login ? $"{login}/{RepositoryName}" : null;

    private GitHubRepositoryClient Client() =>
        clientFor(Repository ?? throw new GitHubException(GitHubFailure.NoToken, L.T("Profile not reachable on GitHub: sign in to GitHub or import a profile.")));

    /// <summary>
    /// Lit le profil sur GitHub et met à jour la copie locale. Aucun compte, réseau absent, accès refusé ou profil
    /// invalide : copie locale, et l'erreur est rendue pour être signalée.
    /// </summary>
    public async Task<ProfileLoad> LoadAsync(CancellationToken cancellation = default)
    {
        // Sans compte : profil tenu sur ce PC, vide au premier lancement.
        if (Repository is null)
            return new ProfileLoad(LoadCache() ?? new Profile(), true, null);
        string? error;
        var missing = false;
        try
        {
            using var client = Client();
            var text = Encoding.UTF8.GetString(await client.GetFileAsync(FileName, cancellation).ConfigureAwait(false));
            var (profile, errors) = ProfileValidator.Read(text);
            if (profile is not null)
            {
                AtomicFile.WriteAllText(paths.ProfileCache, text);
                return new ProfileLoad(profile, false, null);
            }
            error = L.T("The profile on GitHub is invalid, nothing is applied from it:") + Environment.NewLine + string.Join(Environment.NewLine, errors);
        }
        catch (GitHubException exc) when (exc.Failure is GitHubFailure.NotFound)
        {
            missing = true;
            error = L.T("Repository {0} not found or not accessible: Settings > Create the repository.", Repository);
        }
        catch (Exception exc) when (exc is GitHubException or HttpRequestException or TaskCanceledException or IOException)
        {
            error = L.T("Profile not reachable on GitHub: {0}", exc.Message);
        }
        Log.Error(error);
        // Dépôt pas encore créé : profil vide sur ce PC en attendant, comme sans compte.
        return new ProfileLoad(LoadCache() ?? (missing ? new Profile() : null), true, error, missing);
    }

    /// <summary>Copie locale, null si elle manque ou est invalide.</summary>
    public Profile? LoadCache()
    {
        if (!File.Exists(paths.ProfileCache))
            return null;
        return ProfileValidator.Read(File.ReadAllText(paths.ProfileCache)).Profile;
    }

    /// <summary>Import manuel d'un fichier : devient la copie locale s'il est valide. Renvoie les erreurs.</summary>
    public IReadOnlyList<string> Import(string file)
    {
        var text = File.ReadAllText(file);
        var (profile, errors) = ProfileValidator.Read(text);
        if (profile is not null)
        {
            AtomicFile.WriteAllText(paths.ProfileCache, text);
            Log.Info($"Profil importé : {file}");
        }
        return errors;
    }

    /// <summary>
    /// Enregistre le profil modifié : commit dans le dépôt du compte connecté, puis copie locale ; sans compte, sur
    /// ce PC seulement.
    /// </summary>
    public async Task SaveAsync(Profile profile, string message, CancellationToken cancellation = default)
    {
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        var text = profile.ToJson();
        if (Repository is not null)
        {
            using var client = Client();
            await client.PutFileAsync(FileName, Encoding.UTF8.GetBytes(text), message, cancellation).ConfigureAwait(false);
        }
        AtomicFile.WriteAllText(paths.ProfileCache, text);
        Log.Info($"Profil enregistré {(Repository is null ? "sur ce PC" : "sur GitHub")} : {message}");
    }

    /// <summary>Télécharge un fichier du dépôt du profil (<c>profile:chemin</c>) vers <paramref name="destination"/>.</summary>
    public async Task DownloadAsync(string url, string destination, CancellationToken cancellation = default)
    {
        using var client = Client();
        var bytes = await client.GetFileAsync(url[ProfileScheme.Length..], cancellation).ConfigureAwait(false);
        await File.WriteAllBytesAsync(destination, bytes, cancellation).ConfigureAwait(false);
    }

    /// <summary>État du dépôt du profil sur le compte connecté (après la connexion ou un import).</summary>
    public async Task<ProfileAccess> CheckAccessAsync(CancellationToken cancellation = default)
    {
        using var client = Client();
        if (await client.GetFileShaAsync(FileName, cancellation).ConfigureAwait(false) is not null)
            return ProfileAccess.Ready;
        return await client.ExistsAsync(cancellation).ConfigureAwait(false) ? ProfileAccess.NoProfileFile : ProfileAccess.NoRepository;
    }
}
