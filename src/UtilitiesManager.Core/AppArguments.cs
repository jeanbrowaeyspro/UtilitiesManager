namespace UtilitiesManager;

/// <summary>Ligne de commande reçue par l'application (transmise par le lanceur).</summary>
/// <param name="Startup">Lancement à l'ouverture de session (<c>/demarrage</c>).</param>
/// <param name="LauncherPath">Chemin du lanceur (<c>/lanceur "chemin"</c>), absent si l'application est lancée directement.</param>
/// <param name="AfterInstall">Première vérification après l'installation (<c>/installation</c>).</param>
public sealed record AppArguments(bool Startup, string? LauncherPath, bool AfterInstall = false)
{
    public static AppArguments Parse(IReadOnlyList<string> args)
    {
        var startup = false;
        var afterInstall = false;
        string? launcher = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(Switches.Startup, StringComparison.OrdinalIgnoreCase))
                startup = true;
            else if (args[i].Equals(Switches.AfterInstall, StringComparison.OrdinalIgnoreCase))
                afterInstall = true;
            else if (args[i].Equals(Switches.Launcher, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                launcher = args[++i];
        }
        return new AppArguments(startup, launcher, afterInstall);
    }
}
