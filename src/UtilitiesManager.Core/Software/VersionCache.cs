using System.Text.Json;

namespace UtilitiesManager.Software;

/// <summary>Version gardée pour revenir en arrière : numéro et fichier d'installation (absent pour winget).</summary>
public sealed record CachedVersion(string Version, string? File);

/// <summary>
/// Une seule version précédente par logiciel : <c>cache\&lt;id&gt;\current</c> (installée) et
/// <c>cache\&lt;id&gt;\previous</c> (celle d'avant). Chaque installation fait glisser current vers previous ; la
/// version d'avant-hier est supprimée.
/// </summary>
public sealed class VersionCache(string root)
{
    private const string Current = "current";
    private const string Previous = "previous";
    private const string InfoFile = "version.json";

    public CachedVersion? PreviousOf(string id) => Read(Path.Combine(root, id, Previous));

    public CachedVersion? CurrentOf(string id) => Read(Path.Combine(root, id, Current));

    /// <summary>
    /// Retient la version qui vient d'être installée : l'ancienne courante devient la précédente (si son numéro
    /// diffère). <paramref name="file"/> est copié dans le cache.
    /// </summary>
    public void Push(string id, string version, string? file)
    {
        var folder = Path.Combine(root, id);
        var current = Path.Combine(folder, Current);
        var previous = Path.Combine(folder, Previous);
        if (Read(current) is { } old && old.Version != version)
        {
            if (Directory.Exists(previous))
                Directory.Delete(previous, recursive: true);
            Directory.Move(current, previous);
        }
        else if (Directory.Exists(current))
        {
            Directory.Delete(current, recursive: true);
        }
        Directory.CreateDirectory(current);
        string? kept = null;
        if (file is not null)
        {
            kept = Path.Combine(current, Path.GetFileName(file));
            File.Copy(file, kept, overwrite: true);
        }
        File.WriteAllText(Path.Combine(current, InfoFile), JsonSerializer.Serialize(new CachedVersion(version, kept)));
    }

    /// <summary>Retour à la version précédente réussi : elle redevient la courante, il n'y a plus de précédente.</summary>
    public void Reverted(string id)
    {
        var folder = Path.Combine(root, id);
        var current = Path.Combine(folder, Current);
        var previous = Path.Combine(folder, Previous);
        if (!Directory.Exists(previous))
            return;
        if (Directory.Exists(current))
            Directory.Delete(current, recursive: true);
        Directory.Move(previous, current);
        if (Read(current) is { File: { } file } info)
        {
            var moved = Path.Combine(current, Path.GetFileName(file));
            File.WriteAllText(Path.Combine(current, InfoFile), JsonSerializer.Serialize(info with { File = moved }));
        }
    }

    public void Forget(string id)
    {
        var folder = Path.Combine(root, id);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private static CachedVersion? Read(string folder)
    {
        var info = Path.Combine(folder, InfoFile);
        try
        {
            return File.Exists(info) ? JsonSerializer.Deserialize<CachedVersion>(File.ReadAllText(info)) : null;
        }
        catch (Exception exc) when (exc is JsonException or IOException)
        {
            return null;
        }
    }
}
