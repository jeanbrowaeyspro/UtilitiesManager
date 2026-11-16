namespace UtilitiesManager.Launcher;

/// <summary>Transmission de la ligne de commande du lanceur à l'application.</summary>
public static class CommandLine
{
    /// <summary>Ligne de commande sans le nom du lanceur.</summary>
    public static string Arguments(string commandLine)
    {
        var line = commandLine.TrimStart();
        int end;
        if (line.StartsWith("\""))
        {
            end = line.IndexOf('"', 1);
            end = end < 0 ? line.Length : end + 1;
        }
        else
        {
            end = line.IndexOf(' ');
            if (end < 0)
                end = line.Length;
        }
        return line.Substring(end).TrimStart();
    }

    /// <summary>
    /// Arguments de l'application : chemin du lanceur (<c>/lanceur "chemin"</c>, copié à
    /// l'installation) suivi de la ligne de commande reçue.
    /// </summary>
    public static string ForApp(string launcherPath, string commandLine) =>
        $"{Switches.Launcher} \"{launcherPath}\" {Arguments(commandLine)}".TrimEnd();
}
