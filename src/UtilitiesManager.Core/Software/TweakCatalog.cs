using System.Text.Json;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>
/// Réglages Windows connus de l'outil (données de <c>TweakCatalog.json</c>, noms en anglais traduits à l'ajout) :
/// ceux déjà posés sur un PC peuvent être relevés pour remplir le profil.
/// </summary>
public static class TweakCatalog
{
    private const string Resource = "UtilitiesManager.Software.TweakCatalog.json";

    /// <summary>Nouvelle copie du catalogue à chaque appel (les entrées rendues peuvent être modifiées).</summary>
    public static List<TweakEntry> Load()
    {
        using var stream = typeof(TweakCatalog).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Ressource {Resource} absente");
        return JsonSerializer.Deserialize<List<TweakEntry>>(stream, Profile.JsonOptions) ?? [];
    }
}
