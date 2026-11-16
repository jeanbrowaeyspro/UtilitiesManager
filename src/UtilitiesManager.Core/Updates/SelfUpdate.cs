using System.Diagnostics;

namespace UtilitiesManager.Updates;

/// <summary>
/// Remplacement du lanceur de UtilitiesManager par une version téléchargée, sans droits administrateur. Le lanceur
/// en place est renommé en <c>.old</c> (un exécutable lancé peut être renommé, pas écrasé), le nouveau prend son
/// nom, l'ancien est supprimé au lancement suivant. L'application extraite étant verrouillée tant qu'elle tourne,
/// la relance passe par une commande cachée différée, lancée juste avant de quitter.
/// </summary>
public sealed class SelfUpdate(string launcher)
{
    public string OldLauncher => launcher + ".old";

    /// <summary>Téléchargement en cours, jamais exécuté sous ce nom.</summary>
    public string DownloadedLauncher => launcher + ".new";

    /// <summary>Supprime l'ancien lanceur laissé par une mise à jour précédente.</summary>
    public void CleanUp()
    {
        if (File.Exists(OldLauncher) && TryDelete(OldLauncher))
            Log.Info($"Ancien lanceur supprimé : {OldLauncher}");
    }

    /// <summary>Met le lanceur téléchargé à la place du lanceur en place (retour arrière en cas d'échec).</summary>
    public void Replace()
    {
        TryDelete(OldLauncher);
        File.Move(launcher, OldLauncher);
        try
        {
            File.Move(DownloadedLauncher, launcher);
        }
        catch
        {
            File.Move(OldLauncher, launcher);
            throw;
        }
        Log.Info($"Lanceur remplacé : {launcher}");
    }

    /// <summary>Commande cachée qui attend la fermeture de l'application puis relance le nouveau lanceur.</summary>
    public ProcessStartInfo Restart() =>
        new("cmd.exe", $"/d /c ping -n 3 127.0.0.1 >nul & start \"\" \"{launcher}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(launcher)!,
        };

    /// <summary>Supprime le fichier s'il existe ; faux (et journalisé) si c'est impossible.</summary>
    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Suppression impossible de {path} : {exc.Message}");
            return false;
        }
    }
}
