namespace UtilitiesManager.Software;

/// <summary>
/// Icônes du Bureau (raccourcis .lnk et .url, Bureau de l'utilisateur et Bureau commun) : relevées avant une mise
/// à jour, celles que l'installeur a ajoutées sont retirées juste après, pour ne pas remettre une icône que
/// l'utilisateur avait enlevée.
/// </summary>
public sealed class DesktopIcons(IReadOnlyList<string> folders)
{
    public static DesktopIcons OfThisUser() => new([
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    ]);

    public HashSet<string> Snapshot() =>
        folders.Where(f => f.Length > 0 && Directory.Exists(f))
               .SelectMany(f => Directory.EnumerateFiles(f))
               .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".lnk" or ".url")
               .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Retire les icônes apparues depuis <paramref name="before"/> ; renvoie leur nom et l'erreur éventuelle.</summary>
    public List<(string Name, string? Error)> RemoveAdded(HashSet<string> before)
    {
        var result = new List<(string, string?)>();
        foreach (var file in Snapshot().Where(f => !before.Contains(f)))
        {
            string? error = null;
            try
            {
                File.Delete(file);
            }
            catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
            {
                error = exc.Message;
            }
            result.Add((Path.GetFileNameWithoutExtension(file), error));
        }
        return result;
    }
}
