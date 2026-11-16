namespace UtilitiesManager;

/// <summary>
/// Arguments de ligne de commande échangés entre le lanceur et l'application.
/// Fichier lié aux deux projets : interne à chacun.
/// </summary>
internal static class Switches
{
    /// <summary>Démarrage de session Windows : vérification silencieuse.</summary>
    public const string Startup = "/demarrage";
    /// <summary>
    /// Avec <c>/demarrage</c>, première vérification juste après l'installation :
    /// les logiciels du profil absents de ce PC sont proposés à l'installation.
    /// </summary>
    public const string AfterInstall = "/installation";
    /// <summary>Suivi du chemin complet du lanceur (fichier copié à l'installation).</summary>
    public const string Launcher = "/lanceur";

    /// <summary>
    /// Commandes du pilotage en ligne de commande (premier argument) : l'application les exécute sans fenêtre et
    /// le lanceur attend sa fin pour rendre son code de sortie.
    /// </summary>
    public static readonly string[] Commands = { "check", "install", "update", "revert", "settings", "profile", "help" };

    /// <summary>Vrai si <paramref name="first"/> (premier argument utile) est une commande.</summary>
    public static bool IsCommand(string? first) =>
        first != null && System.Array.Exists(Commands, c => c.Equals(first, System.StringComparison.OrdinalIgnoreCase));
}
