using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtilitiesManager.Profiles;

/// <summary>
/// Profil commun à tous les PC : logiciels et réglages Windows souhaités, fichier <c>profile.json</c> du dépôt
/// privé UtilitiesProfile (clés en snake_case). Tout logiciel est décrit par des données : aucun code propre à un
/// logiciel dans l'outil.
/// </summary>
public sealed class Profile
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public ProfileDefaults Defaults { get; set; } = new();
    public List<ProgramEntry> Programs { get; set; } = [];
    public List<TweakEntry> Tweaks { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Lit le texte JSON ; lève <see cref="JsonException"/> s'il est mal formé.</summary>
    public static Profile Parse(string json) =>
        JsonSerializer.Deserialize<Profile>(json, JsonOptions) ?? throw new JsonException("profil vide");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions).ReplaceLineEndings("\n") + "\n";

    public ProgramEntry? Find(string id) => Programs.FirstOrDefault(p => p.Id == id);

    /// <summary>Copie indépendante (modifications de la fenêtre avant enregistrement).</summary>
    public Profile Clone() => Parse(ToJson());
}

public sealed class ProfileDefaults
{
    public string Mode { get; set; } = Modes.Confirm;
    public int MinAgeDays { get; set; }
}

public static class Modes
{
    /// <summary>Mise à jour installée sans question, notification à la fin.</summary>
    public const string Auto = "auto";
    /// <summary>Mise à jour proposée dans la fenêtre, à cocher.</summary>
    public const string Confirm = "confirm";
}

public static class Sources
{
    public const string Winget = "winget";
    public const string MsStore = "msstore";
    public const string Chocolatey = "chocolatey";
    public const string GitHub = "github";
    public const string Zip = "zip";
    public const string Download = "download";
    public const string Command = "command";

    public static readonly string[] All = [Winget, MsStore, Chocolatey, GitHub, Zip, Download, Command];
}

/// <summary>Contrôle d'un fichier téléchargé avant de l'installer.</summary>
public static class Verifications
{
    /// <summary>Signature Authenticode valide (et éditeur attendu s'il est donné) : cas général.</summary>
    public const string Signature = "signature";
    /// <summary>Empreinte SHA-256 figée dans le profil ou publiée par l'éditeur (<c>checksum_url</c>).</summary>
    public const string Sha256 = "sha256";
    /// <summary>Fichier venant du dépôt du profil ou d'une release d'un dépôt de Jean : origine de confiance.</summary>
    public const string Origin = "origin";

    public static readonly string[] All = [Signature, Sha256, Origin];
}

/// <summary>Un logiciel du profil. Les champs inutiles à sa source restent absents du fichier.</summary>
public sealed class ProgramEntry
{
    /// <summary>Identifiant stable (minuscules, chiffres, <c>.-_</c>).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = Sources.Winget;
    /// <summary>Identifiant winget ou Store, paquet Chocolatey, ou dépôt GitHub <c>propriétaire/dépôt</c>.</summary>
    public string? Package { get; set; }
    /// <summary><see cref="Modes"/> ; absent = mode par défaut du profil.</summary>
    public string? Mode { get; set; }
    /// <summary>Jours entre la sortie d'une version et sa proposition ; 0 = immédiat ; absent = défaut du profil.</summary>
    public int? MinAgeDays { get; set; }
    /// <summary>Version figée : aucune mise à jour au-delà n'est proposée.</summary>
    public string? BlockedVersion { get; set; }
    /// <summary>Version connue d'un outil sans recherche de version (zip, téléchargement à adresse fixe).</summary>
    public string? Version { get; set; }
    /// <summary><see cref="Verifications"/> ; absent = signature.</summary>
    public string? Verify { get; set; }
    /// <summary>Éditeur attendu (nom commun du certificat) ; absent = toute signature valide.</summary>
    public string? Signer { get; set; }
    /// <summary>Empreinte SHA-256 attendue du fichier téléchargé.</summary>
    public string? Sha256 { get; set; }
    /// <summary>Fichier d'empreintes de l'éditeur (format sha256sum), lu pour vérifier le fichier téléchargé.</summary>
    public string? ChecksumUrl { get; set; }
    /// <summary>Processus à fermer avant l'installation (sans <c>.exe</c>), relancés après.</summary>
    public List<string>? Close { get; set; }
    /// <summary>Arguments de l'installeur en silencieux (ou de winget, ajoutés à sa commande).</summary>
    public string? SilentArgs { get; set; }
    /// <summary>Installeur ouvert pour Jean, avec ses fenêtres.</summary>
    public bool Interactive { get; set; }
    /// <summary>Commande lancée avec les droits administrateur (fenêtre UAC).</summary>
    public bool Elevate { get; set; }
    /// <summary>
    /// Adresse du fichier à télécharger : <c>{version}</c>, <c>{dep_version}</c> remplacés ; <c>profile:chemin</c> pour
    /// un fichier du dépôt du profil.
    /// </summary>
    public string? Url { get; set; }
    /// <summary>Motif du nom de l'asset de la release GitHub.</summary>
    public string? AssetPattern { get; set; }
    /// <summary>Motif du chemin de l'installeur dans le paquet Chocolatey.</summary>
    public string? InstallerPattern { get; set; }
    /// <summary>Releases GitHub publiées en préversion acceptées.</summary>
    public bool Prerelease { get; set; }
    /// <summary>Dossier d'un outil portable (zip décompressé ou exe copié).</summary>
    public string? InstallDir { get; set; }
    /// <summary>Logiciel installé et mis à jour juste avant celui-ci ; sa version donne <c>{dep_version}</c>.</summary>
    public string? DependsOn { get; set; }
    public Detection? Detect { get; set; }
    public LatestQuery? Latest { get; set; }
    public string? InstallCommand { get; set; }
    public string? UpdateCommand { get; set; }
    public string? UninstallCommand { get; set; }
    /// <summary>Clés de fichiers <c>.ini</c> posées après l'installation.</summary>
    public List<IniSetting>? Ini { get; set; }

    [JsonIgnore]
    public bool IsWinget => Source is Sources.Winget or Sources.MsStore;

    public string ModeOr(ProfileDefaults defaults) => Mode ?? defaults.Mode;

    public int MinAgeOr(ProfileDefaults defaults) => MinAgeDays ?? defaults.MinAgeDays;

    public string VerifyOrDefault => Verify ?? Verifications.Signature;
}

/// <summary>Détection de la version installée (logiciels hors winget, ou Store par motif d'identifiant).</summary>
public sealed class Detection
{
    /// <summary>Motif du nom affiché dans la liste des programmes installés (registre Uninstall).</summary>
    public string? DisplayName { get; set; }
    /// <summary>Fichier présent si le logiciel est installé (variables d'environnement acceptées).</summary>
    public string? File { get; set; }
    /// <summary>Motif de l'identifiant dans <c>winget list</c> (applications du Store : <c>MSIX\...</c>).</summary>
    public string? WingetId { get; set; }
    /// <summary>Commande dont la sortie donne la version.</summary>
    public string? Command { get; set; }
    /// <summary>Motif appliqué à la sortie de la commande, version dans le premier groupe.</summary>
    public string? Regex { get; set; }
}

/// <summary>Recherche de la dernière version : page web ou commande, lue par un motif.</summary>
public sealed class LatestQuery
{
    /// <summary>Page lue : premier groupe = version, groupe nommé <c>url</c> = lien de téléchargement.</summary>
    public string? Url { get; set; }
    public string? Command { get; set; }
    public string? Regex { get; set; }
}

public sealed class IniSetting
{
    public string File { get; set; } = "";
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Réglage Windows : valeurs du registre posées, valeurs rétablies.</summary>
public sealed class TweakEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    /// <summary>Appliqué sur chaque PC avec le profil.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Redémarrage de l'Explorateur après application ou rétablissement.</summary>
    public bool RestartExplorer { get; set; }
    public List<RegistrySetting> Values { get; set; } = [];
}

public sealed class RegistrySetting
{
    /// <summary>Clé complète : <c>HKCU\...</c> ou <c>HKLM\...</c>.</summary>
    public string Key { get; set; } = "";
    /// <summary>Nom de la valeur ; vide = valeur par défaut de la clé.</summary>
    public string Name { get; set; } = "";
    /// <summary><c>dword</c> ou <c>string</c>.</summary>
    public string Type { get; set; } = "dword";
    public string Value { get; set; } = "";
    /// <summary>Valeur rétablie ; absente = valeur supprimée (défaut de Windows).</summary>
    public string? Restore { get; set; }
    /// <summary>Rétablissement par suppression de cette clé et de ses sous-clés (cas du menu contextuel classique).</summary>
    public string? RestoreDeleteKey { get; set; }
}
