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
    /// l'installation de FileZilla est proposée s'il est absent.
    /// </summary>
    public const string AfterInstall = "/installation";
    /// <summary>Suivi du chemin complet du lanceur (fichier copié à l'installation).</summary>
    public const string Launcher = "/lanceur";
}
