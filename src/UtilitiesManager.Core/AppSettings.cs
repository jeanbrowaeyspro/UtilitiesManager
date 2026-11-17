using System.Text.Json;

namespace UtilitiesManager;

/// <summary>
/// Préférences propres au poste, mémorisées dans <c>%APPDATA%\UtilitiesManager\settings.json</c>
/// (clés en snake_case). Tout ce qui vaut pour tous les PC est dans le profil.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Entrée dans HKCU\...\Run.</summary>
    public bool RunAtStartup { get; set; } = true;
    public bool DesktopShortcut { get; set; }
    public bool StartMenuShortcut { get; set; } = true;
    /// <summary>Thème : <c>system</c> (celui de Windows), <c>light</c> ou <c>dark</c>.</summary>
    public string Theme { get; set; } = L.System;
    /// <summary>Langue : <c>system</c> (celle de Windows), <c>fr</c> ou <c>en</c>.</summary>
    public string Language { get; set; } = L.System;
    /// <summary>Icône près de l'horloge.</summary>
    public bool TrayIcon { get; set; } = true;
    /// <summary>Nouveautés de la release affichées avant de valider une mise à jour.</summary>
    public bool ShowReleaseNotes { get; set; } = true;
    /// <summary>Versions d'essai (préversions GitHub) de UtilitiesManager proposées.</summary>
    public bool IncludePrereleases { get; set; }
    /// <summary>Version de UtilitiesManager ignorée : plus proposée automatiquement.</summary>
    public string? SkippedVersion { get; set; }
    /// <summary>Dernière recherche de mise à jour de UtilitiesManager (au plus une par heure).</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }
    /// <summary>Dernière vérification des logiciels (une par jour).</summary>
    public DateTimeOffset? LastCheck { get; set; }
    /// <summary>Logiciels retirés du profil dont la désinstallation a été refusée sur ce poste.</summary>
    public List<string> DeclinedUninstalls { get; set; } = [];
    /// <summary>Logiciels du relevé à ne plus proposer sur ce poste (<see cref="Software.FoundSoftware.Key"/>).</summary>
    public List<string> IgnoredSoftware { get; set; } = [];
    /// <summary>Dossier des logiciels portables parcouru par le relevé.</summary>
    public string PortableFolder { get; set; } = @"C:\Logiciels";
    /// <summary>Dernière version lancée : nouveautés proposées après une mise à jour.</summary>
    public string? LastSeenVersion { get; set; }
    /// <summary>Position, taille et état de la fenêtre principale à sa dernière fermeture.</summary>
    public WindowPlacement? MainWindow { get; set; }

    public bool IsIgnored(Software.FoundSoftware found) => IgnoredSoftware.Contains(found.Key);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    /// <summary>Réglages du fichier ; valeurs par défaut s'il manque ou est illisible.</summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error($"Réglages illisibles, valeurs par défaut : {exc.Message}");
            return new AppSettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Écriture atomique : pas de fichier tronqué si l'application est fermée pendant l'écriture.</summary>
    public void Save(string path) => AtomicFile.WriteAllText(path, ToJson());
}

/// <summary>Position et taille normales d'une fenêtre (pixels, comme les donne Windows), état agrandi.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
