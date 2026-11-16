using System.Text.Json;

namespace UtilitiesManager;

/// <summary>
/// Options choisies dans la fenêtre de réglages, mémorisées dans
/// <c>%APPDATA%\UtilitiesManager\settings.json</c> (clés en snake_case).
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

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Écriture atomique : pas de fichier tronqué si l'application est fermée pendant l'écriture.
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }
}
