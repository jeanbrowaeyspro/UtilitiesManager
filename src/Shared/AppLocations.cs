using System;
using System.IO;

namespace UtilitiesManager;

/// <summary>
/// Noms et dossier de données locales communs au lanceur et à l'application.
/// Fichier lié aux deux projets : interne à chacun. AppPaths en tire ses emplacements.
/// </summary>
internal static class AppLocations
{
    public const string AppName = "UtilitiesManager";
    /// <summary>Application incorporée au lanceur, extraite dans le dossier de données locales.</summary>
    public const string AppFileName = AppName + ".App.exe";

    /// <summary>Dossier lu dans la variable d'environnement, sinon dossier spécial de Windows.</summary>
    public static string Folder(string variable, Environment.SpecialFolder fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) ? Environment.GetFolderPath(fallback) : value!;
    }

    /// <summary>%LOCALAPPDATA% de l'utilisateur courant.</summary>
    public static string LocalAppData => Folder("LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%APPDATA% de l'utilisateur courant.</summary>
    public static string RoamingAppData => Folder("APPDATA", Environment.SpecialFolder.ApplicationData);

    /// <summary>Réglages : %APPDATA%\UtilitiesManager\settings.json (lus aussi par le lanceur pour la langue).</summary>
    public static string SettingsFile(string roamingAppData) => Path.Combine(roamingAppData, AppName, "settings.json");

    /// <summary>Dossier de données locales (application extraite, journal) : %LOCALAPPDATA%\UtilitiesManager.</summary>
    public static string DataDir(string localAppData) => Path.Combine(localAppData, AppName);

    /// <summary>Application extraite par le lanceur dans le dossier de données locales.</summary>
    public static string ExtractedApp(string dataDir) => Path.Combine(dataDir, AppFileName);
}
