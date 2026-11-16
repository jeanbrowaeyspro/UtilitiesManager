namespace UtilitiesManager;

/// <summary>
/// Emplacements utilisés par l'application. Les tests en construisent un jeu complet
/// dans un dossier temporaire : rien n'est lu ni écrit dans les vrais dossiers.
/// </summary>
/// <param name="DataDir">Dossier de données locales : application extraite par le lanceur, journal.</param>
public sealed record AppPaths(
    string InstallDir,
    string SettingsFile,
    string DataDir,
    string DesktopDir,
    string StartMenuDir,
    string TempDir)
{
    public const string AppName = AppLocations.AppName;
    public const string LauncherFileName = AppName + ".exe";
    public const string AppFileName = AppLocations.AppFileName;
    public const string ShortcutFileName = AppName + ".lnk";

    public string InstalledLauncher => Path.Combine(InstallDir, LauncherFileName);
    public string ExtractedApp => AppLocations.ExtractedApp(DataDir);
    public string LogFile => Path.Combine(DataDir, "journal.log");
    public string DesktopShortcut => Path.Combine(DesktopDir, ShortcutFileName);
    public string StartMenuShortcut => Path.Combine(StartMenuDir, ShortcutFileName);

    /// <summary>Emplacements réels de l'utilisateur courant.</summary>
    public static AppPaths FromEnvironment()
    {
        var local = AppLocations.LocalAppData;
        var roaming = AppLocations.Folder("APPDATA", Environment.SpecialFolder.ApplicationData);
        return new AppPaths(
            InstallDir: Path.Combine(local, "Programs", AppName),
            SettingsFile: AppLocations.SettingsFile(roaming),
            DataDir: AppLocations.DataDir(local),
            DesktopDir: Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            StartMenuDir: Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            TempDir: Path.GetTempPath());
    }

    /// <summary>Vrai si le fichier donné se trouve dans le dossier d'installation.</summary>
    public bool IsInInstallDir(string? file) =>
        file is not null
        && string.Equals(Path.GetFullPath(Path.GetDirectoryName(file) ?? "").TrimEnd('\\'),
                         Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
