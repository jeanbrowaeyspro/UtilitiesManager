namespace UtilitiesManager;

/// <summary>
/// Commande du pilotage en ligne de commande (<c>UtilitiesManager.exe check</c>, <c>update --all</c>…), exécutée
/// sans fenêtre. <see cref="Action"/> : sous-commande de <c>settings</c> et <c>profile</c>.
/// </summary>
public sealed record CliCommand(string Verb, string? Action = null, string? Target = null, bool All = false, bool Json = false)
{
    public const int ExitOk = 0;
    /// <summary>Échec : profil illisible, installation ou réglage en échec.</summary>
    public const int ExitFailed = 1;
    /// <summary>Ligne de commande incorrecte.</summary>
    public const int ExitUsage = 2;
    /// <summary>Actions en attente (mise à jour à faire, logiciel à installer ou à désinstaller sur confirmation).</summary>
    public const int ExitPending = 3;
    /// <summary>Une autre opération de UtilitiesManager est en cours (fenêtre ou autre commande).</summary>
    public const int ExitBusy = 4;

    /// <summary>Arguments sans le chemin du lanceur (<c>/lanceur "chemin"</c>, ajouté par le lanceur).</summary>
    private static List<string> Useful(IReadOnlyList<string> args)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(Switches.Launcher, StringComparison.OrdinalIgnoreCase))
                i++;
            else
                list.Add(args[i]);
        }
        return list;
    }

    /// <summary>Vrai si la ligne de commande demande une commande (et non la fenêtre).</summary>
    public static bool IsCommand(IReadOnlyList<string> args) => Switches.IsCommand(Useful(args).FirstOrDefault());

    /// <summary>Analyse la commande ; null et <paramref name="error"/> renseigné si elle est incorrecte.</summary>
    public static CliCommand? Parse(IReadOnlyList<string> args, out string? error)
    {
        var words = Useful(args);
        var json = words.RemoveAll(w => w.Equals("--json", StringComparison.OrdinalIgnoreCase)) > 0;
        var all = words.RemoveAll(w => w.Equals("--all", StringComparison.OrdinalIgnoreCase)) > 0;
        error = words.FirstOrDefault(w => w.StartsWith("--", StringComparison.Ordinal)) is { } unknown
            ? L.T("Unknown option: {0}", unknown)
            : null;
        if (error is not null || words.Count == 0 || !Switches.IsCommand(words[0]))
        {
            error ??= L.T("Unknown command.");
            return null;
        }
        var verb = words[0].ToLowerInvariant();
        var rest = words.Skip(1).ToList();
        CliCommand? command = verb switch
        {
            "check" or "scan" or "help" when rest.Count == 0 && !all => new CliCommand(verb),
            "install" or "update" when rest.Count == 1 ^ all && rest.Count <= 1 => new CliCommand(verb, Target: rest.FirstOrDefault(), All: all),
            "revert" when rest.Count == 1 && !all => new CliCommand(verb, Target: rest[0]),
            "settings" when rest.Count == 2 && !all && rest[0].ToLowerInvariant() is "apply" or "restore" =>
                new CliCommand(verb, rest[0].ToLowerInvariant(), rest[1]),
            "profile" when !all && rest.Count == 2 && rest[0].Equals("import", StringComparison.OrdinalIgnoreCase) =>
                new CliCommand(verb, "import", rest[1]),
            "profile" when !all && rest.Count == 1 && rest[0].Equals("sync", StringComparison.OrdinalIgnoreCase) =>
                new CliCommand(verb, "sync"),
            _ => null,
        };
        if (command is null)
        {
            error = L.T("Incorrect arguments for {0}.", verb);
            return null;
        }
        return command with { Json = json };
    }

    /// <summary>Aide de la ligne de commande.</summary>
    public static string Usage => L.T("Usage: UtilitiesManager.exe <command> [--json]\n\n  check                    Check the software of the profile and the Windows settings\n  scan                     List the software of this PC that is not in the profile\n  install <id> | --all     Install a software of the profile missing on this PC (or all of them)\n  update <id> | --all      Install an available update (or all of them)\n  revert <id>              Go back to the previous version kept on this PC\n  settings apply <id>      Apply a Windows setting of the profile\n  settings restore <id>    Restore a Windows setting\n  profile import <file>    Import a profile file (local copy)\n  profile sync             Read the profile again from GitHub\n  help                     This help\n\n  --json                   Result in JSON on the standard output (progress on the error output)\n\nExit code: 0 done, 1 failure, 2 incorrect command line, 3 actions waiting, 4 another operation in progress.");
}
