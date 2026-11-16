using System.Text;
using UtilitiesManager.GitHub;

namespace UtilitiesManager.Profiles;

/// <summary>Profil chargé : depuis GitHub ou la copie locale, avec l'erreur rencontrée éventuelle.</summary>
public sealed record ProfileLoad(Profile? Profile, bool FromCache, string? Error);

/// <summary>
/// Profil rangé dans le dépôt privé <see cref="Repository"/> : lecture et écriture par l'API GitHub (chaque
/// modification est un commit), copie locale du dernier profil valide pour travailler hors ligne, import manuel
/// d'un fichier.
/// </summary>
public sealed class ProfileStore(AppPaths paths, Func<string, GitHubRepositoryClient> clientFor)
{
    public const string Repository = "jeanbrowaeyspro/UtilitiesProfile";
    public const string FileName = "profile.json";
    /// <summary>Préfixe d'une adresse qui désigne un fichier du dépôt du profil.</summary>
    public const string ProfileScheme = "profile:";

    /// <summary>
    /// Lit le profil sur GitHub et met à jour la copie locale. Réseau absent, accès refusé ou profil invalide :
    /// copie locale, et l'erreur est rendue pour être signalée.
    /// </summary>
    public async Task<ProfileLoad> LoadAsync(CancellationToken cancellation = default)
    {
        string? error;
        try
        {
            using var client = clientFor(Repository);
            var text = Encoding.UTF8.GetString(await client.GetFileAsync(FileName, cancellation).ConfigureAwait(false));
            var (profile, errors) = ProfileValidator.Read(text);
            if (profile is not null)
            {
                AtomicFile.WriteAllText(paths.ProfileCache, text);
                return new ProfileLoad(profile, false, null);
            }
            error = L.T("The profile on GitHub is invalid, nothing is applied from it:") + Environment.NewLine + string.Join(Environment.NewLine, errors);
        }
        catch (GitHubException exc) when (exc.Failure == GitHubFailure.NotFound)
        {
            error = L.T("Profile not reachable on GitHub: sign in to GitHub or import a profile.");
        }
        catch (Exception exc) when (exc is GitHubException or HttpRequestException or TaskCanceledException or IOException)
        {
            error = L.T("Profile not reachable on GitHub: {0}", exc.Message);
        }
        Log.Error(error);
        return LoadCache() is { } cached ? new ProfileLoad(cached, true, error) : new ProfileLoad(null, true, error);
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

    /// <summary>Enregistre le profil modifié : commit dans le dépôt, puis copie locale.</summary>
    public async Task SaveAsync(Profile profile, string message, CancellationToken cancellation = default)
    {
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        var text = profile.ToJson();
        using (var client = clientFor(Repository))
            await client.PutFileAsync(FileName, Encoding.UTF8.GetBytes(text), message, cancellation).ConfigureAwait(false);
        AtomicFile.WriteAllText(paths.ProfileCache, text);
        Log.Info($"Profil enregistré sur GitHub : {message}");
    }

    /// <summary>Télécharge un fichier du dépôt du profil (<c>profile:chemin</c>) vers <paramref name="destination"/>.</summary>
    public async Task DownloadAsync(string url, string destination, CancellationToken cancellation = default)
    {
        using var client = clientFor(Repository);
        var bytes = await client.GetFileAsync(url[ProfileScheme.Length..], cancellation).ConfigureAwait(false);
        await File.WriteAllBytesAsync(destination, bytes, cancellation).ConfigureAwait(false);
    }
}
