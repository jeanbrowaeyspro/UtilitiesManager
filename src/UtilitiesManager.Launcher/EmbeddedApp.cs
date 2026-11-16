using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace UtilitiesManager.Launcher;

/// <summary>
/// Application UtilitiesManager.App.exe incorporée au lanceur, extraite dans
/// %LOCALAPPDATA%\UtilitiesManager quand elle manque ou diffère (taille puis empreinte SHA-256).
/// </summary>
public static class EmbeddedApp
{
    public const string ResourceName = AppLocations.AppFileName;

    /// <summary>Vrai si le fichier existe et a exactement le contenu donné.</summary>
    public static bool IsUpToDate(string path, byte[] content)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != content.Length)
            return false;
        using var sha = SHA256.Create();
        using var stream = file.OpenRead();
        return sha.ComputeHash(stream).SequenceEqual(sha.ComputeHash(content));
    }

    /// <summary>
    /// Écrit le contenu s'il diffère du fichier en place ; renvoie vrai si le fichier a
    /// été écrit (son dossier est créé au besoin). Passe par un fichier temporaire pour ne
    /// jamais laisser d'exe tronqué.
    /// </summary>
    public static bool Extract(byte[] content, string path)
    {
        if (IsUpToDate(path, content))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, content);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
        return true;
    }
}
