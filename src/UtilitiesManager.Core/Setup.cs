using System.Diagnostics;

namespace UtilitiesManager;

/// <summary>
/// Installation dans <c>%LOCALAPPDATA%\Programs\UtilitiesManager</c>, application des
/// options et désinstallation. Aucun droit administrateur n'est nécessaire.
/// </summary>
public sealed class Setup(AppPaths paths, IRegistryStore registry, IShortcutWriter shortcuts)
{
    public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = AppPaths.AppName;
    public static string ShortcutDescription => L.T("UtilitiesManager settings");

    public AppPaths Paths => paths;

    /// <summary>Vrai si le lanceur est présent dans le dossier d'installation.</summary>
    public bool IsInstalled => File.Exists(paths.InstalledLauncher);

    /// <summary>Commande inscrite dans Run : lanceur installé, vérification silencieuse.</summary>
    public string RunCommand => $"\"{paths.InstalledLauncher}\" {Switches.Startup}";

    /// <summary>
    /// Première vérification après l'installation : lanceur installé avec
    /// <c>/demarrage</c>, comme à l'ouverture de session, et <c>/installation</c> pour
    /// proposer d'installer FileZilla s'il est absent.
    /// </summary>
    public ProcessStartInfo StartupCheck =>
        new(paths.InstalledLauncher, $"{Switches.Startup} {Switches.AfterInstall}")
        {
            UseShellExecute = false,
            WorkingDirectory = paths.InstallDir,
        };

    /// <summary>
    /// Copie le lanceur (seul fichier du dossier d'installation, l'application étant
    /// extraite par lui dans le dossier de données locales), puis applique les options.
    /// </summary>
    public void Install(string launcherSource, AppSettings settings)
    {
        Directory.CreateDirectory(paths.InstallDir);
        if (!string.Equals(Path.GetFullPath(launcherSource), Path.GetFullPath(paths.InstalledLauncher),
                           StringComparison.OrdinalIgnoreCase))
            File.Copy(launcherSource, paths.InstalledLauncher, overwrite: true);
        Log.Info($"Installé dans {paths.InstallDir}");
        Apply(settings);
    }

    /// <summary>Applique les options et les mémorise.</summary>
    public void Apply(AppSettings settings)
    {
        if (settings.RunAtStartup)
            registry.SetString(RegistryRoot.CurrentUser, RunPath, RunValue, RunCommand);
        else
            registry.DeleteValue(RegistryRoot.CurrentUser, RunPath, RunValue);
        SetShortcut(paths.DesktopShortcut, settings.DesktopShortcut);
        SetShortcut(paths.StartMenuShortcut, settings.StartMenuShortcut);
        settings.Save(paths.SettingsFile);
        Log.Info($"Options appliquées : {settings.ToJson().ReplaceLineEndings(" ")}");
    }

    private void SetShortcut(string path, bool wanted)
    {
        if (wanted)
            shortcuts.Create(path, paths.InstalledLauncher, ShortcutDescription);
        else
            Delete(path);
    }

    /// <summary>Supprime le fichier s'il existe (son dossier peut manquer).</summary>
    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Retire l'entrée Run, supprime raccourcis et
    /// réglages. Le dossier d'installation et l'application extraite (exécutable en
    /// cours) sont supprimés après la fermeture par la commande renvoyée (null si aucun
    /// des deux n'existe). Le journal est conservé.
    /// </summary>
    public ProcessStartInfo? Uninstall()
    {
        registry.DeleteValue(RegistryRoot.CurrentUser, RunPath, RunValue);
        Delete(paths.DesktopShortcut);
        Delete(paths.StartMenuShortcut);
        Delete(paths.SettingsFile);
        var settingsDir = Path.GetDirectoryName(paths.SettingsFile)!;
        if (Directory.Exists(settingsDir) && !Directory.EnumerateFileSystemEntries(settingsDir).Any())
            Directory.Delete(settingsDir);
        Log.Info("Désinstallation");
        return Directory.Exists(paths.InstallDir) || File.Exists(paths.ExtractedApp)
            ? DeferredDeletion(paths.InstallDir, paths.ExtractedApp)
            : null;
    }

    /// <summary>
    /// Commande cachée qui attend quelques secondes (fermeture de l'application) puis
    /// supprime le dossier et le fichier.
    /// </summary>
    public static ProcessStartInfo DeferredDeletion(string folder, string file) =>
        new("cmd.exe", $"/d /c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{folder}\" & del /f /q \"{file}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        };
}
