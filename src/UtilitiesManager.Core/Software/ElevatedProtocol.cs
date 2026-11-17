using System.Text.Json;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>
/// Logiciel d'une série ; <paramref name="Removed"/> : à désinstaller ; <paramref name="Update"/> : déjà installé
/// (mise à jour, les icônes ajoutées au Bureau sont retirées).
/// </summary>
public sealed record ElevatedItem(ProgramEntry Program, InstalledInfo? Removed, bool Update = false);

/// <summary>Série d'installations confiée au processus administrateur (profil en JSON).</summary>
public sealed record ElevatedRequest(string Profile, List<ElevatedItem> Items);

public sealed record ElevatedFailure(string Id, string Message);

/// <summary>
/// Message du processus administrateur : étape (<paramref name="Text"/>, <paramref name="Fraction"/>), ou bilan
/// final (<paramref name="Done"/>, <paramref name="Failed"/>), ou étape d'un logiciel (<paramref name="Step"/>).
/// </summary>
public sealed record ElevatedReport(string? Text = null, double? Fraction = null, List<string>? Done = null,
                                   List<ElevatedFailure>? Failed = null, BatchStep? Step = null);

/// <summary>
/// Échanges entre la fenêtre et le processus administrateur qui mène toute une série d'installations (une seule
/// demande d'élévation) : un objet JSON par ligne sur un canal nommé, dans les deux sens ; la fenêtre envoie la
/// demande puis les ordres <see cref="Pause"/>, <see cref="Resume"/> et <see cref="Stop"/>.
/// </summary>
public static class ElevatedProtocol
{
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Stop = "stop";

    private static readonly JsonSerializerOptions Options = new(Profile.JsonOptions) { WriteIndented = false };

    public static string Write<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static T Read<T>(string line) =>
        JsonSerializer.Deserialize<T>(line, Options) ?? throw new JsonException("message vide");

    /// <summary>Applique un ordre reçu de la fenêtre.</summary>
    public static void Apply(string order, BatchControl control)
    {
        switch (order)
        {
            case Pause:
                control.Pause();
                break;
            case Resume:
                control.Resume();
                break;
            case Stop:
                control.Stop();
                break;
        }
    }

    /// <summary>Ordre à transmettre après un changement de <paramref name="control"/>.</summary>
    public static string Order(BatchControl control) =>
        control.Stopped ? Stop : control.Paused ? Pause : Resume;
}
