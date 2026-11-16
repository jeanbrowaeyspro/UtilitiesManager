using System.Text.Json;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>Version vue pour la première fois sur ce PC, point de départ de l'âge minimum.</summary>
public sealed record SeenVersion(string Version, DateTimeOffset Date);

/// <summary>
/// Mémoire propre à ce PC (<c>state.json</c>, à côté de la copie du profil) : dates de première apparition des
/// versions, versions posées par l'outil pour les outils portables, et logiciels du profil installés ici (pour
/// proposer leur désinstallation s'ils sortent du profil).
/// </summary>
public sealed class MachineState
{
    public Dictionary<string, SeenVersion> FirstSeen { get; set; } = [];
    /// <summary>Version installée par l'outil, quand le logiciel ne l'indique nulle part (outil portable).</summary>
    public Dictionary<string, string> InstalledVersions { get; set; } = [];
    /// <summary>Entrées du profil installées sur ce PC lors de la dernière vérification.</summary>
    public List<ProgramEntry> Managed { get; set; } = [];

    public static MachineState Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<MachineState>(File.ReadAllText(path), Profile.JsonOptions) ?? new MachineState();
        }
        catch (Exception exc) when (exc is JsonException or IOException)
        {
            Log.Error($"État du PC illisible, repris à zéro : {exc.Message}");
        }
        return new MachineState();
    }

    public void Save(string path) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Profile.JsonOptions));

    /// <summary>Date de première apparition de <paramref name="version"/>, notée maintenant si elle est nouvelle.</summary>
    public DateTimeOffset Seen(string id, string version, DateTimeOffset now)
    {
        if (FirstSeen.TryGetValue(id, out var seen) && seen.Version == version)
            return seen.Date;
        FirstSeen[id] = new SeenVersion(version, now);
        return now;
    }

    /// <summary>Retient l'entrée comme installée ici (remplace l'entrée précédente du même logiciel).</summary>
    public void Remember(ProgramEntry program)
    {
        Managed.RemoveAll(p => p.Id == program.Id);
        Managed.Add(program);
    }

    /// <summary>Relit le fichier, écrit entre-temps par un autre processus (installations en administrateur).</summary>
    public void Reload(string path)
    {
        var saved = Load(path);
        FirstSeen = saved.FirstSeen;
        InstalledVersions = saved.InstalledVersions;
        Managed = saved.Managed;
    }

    public void Forget(string id)
    {
        Managed.RemoveAll(p => p.Id == id);
        FirstSeen.Remove(id);
        InstalledVersions.Remove(id);
    }
}
