using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;

namespace UtilitiesManager;

/// <summary>
/// Pilotage en ligne de commande, sans fenêtre : même profil, mêmes actions et même journal que la fenêtre. Le
/// résultat va sur la sortie standard (texte ou JSON), l'avancement sur la sortie standard en texte et sur la sortie
/// d'erreur en JSON. Les invites UAC des installeurs restent affichées par Windows.
/// </summary>
public sealed class CommandRunner(AppServices services, TextWriter output, TextWriter error)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SoftwareActions _actions = new(services);
    private bool _json;

    public async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        if (CliCommand.Parse(args, out var usageError) is not { } command)
        {
            error.WriteLine(usageError);
            error.WriteLine(CliCommand.Usage);
            return CliCommand.ExitUsage;
        }
        _json = command.Json;
        if (command.Verb == "help")
        {
            output.WriteLine(CliCommand.Usage);
            return CliCommand.ExitOk;
        }
        Log.Info($"Commande : {command}");
        using var operation = OperationLock.TryAcquire();
        if (operation is null)
        {
            error.WriteLine(L.T("Another UtilitiesManager operation is in progress."));
            return CliCommand.ExitBusy;
        }
        try
        {
            return command.Verb switch
            {
                "check" => await CheckAsync(),
                "install" or "update" => await InstallAsync(command),
                "revert" => await RevertAsync(command.Target!),
                "settings" => await SettingsAsync(command.Action == "apply", command.Target!),
                _ => command.Action == "import" ? await ImportAsync(command.Target!) : await SyncAsync(),
            };
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            Log.Error($"Commande : {exc}");
            error.WriteLine(L.T("Failed: {0}", exc.Message));
            return CliCommand.ExitFailed;
        }
    }

    // ---- Commandes -------------------------------------------------------------------------------------------

    private async Task<int> CheckAsync()
    {
        if (await LoadProfileAsync() is not { } profile)
            return CliCommand.ExitFailed;
        var result = await _actions.CheckAsync(profile);
        if (_json)
        {
            WriteJson(new JsonObject
            {
                ["wingetError"] = result.WingetError,
                ["programs"] = Programs(result, profile),
                ["settings"] = new JsonArray([.. profile.Tweaks.Select(t => (JsonNode)new JsonObject
                {
                    ["id"] = t.Id,
                    ["name"] = t.Name,
                    ["applied"] = services.Tweaks.IsApplied(t),
                })]),
            });
        }
        else
        {
            WritePrograms(result, profile);
            if (profile.Tweaks.Count > 0)
            {
                output.WriteLine();
                output.WriteLine(L.T("Windows settings"));
                foreach (var tweak in profile.Tweaks)
                    output.WriteLine(new TweakRow(tweak, services.Tweaks.IsApplied(tweak)).Text + $"  [{tweak.Id}]");
            }
        }
        if (result.WingetError is not null)
            return CliCommand.ExitFailed;
        return result.Programs.Any(p => p.NeedsAction) ? CliCommand.ExitPending : CliCommand.ExitOk;
    }

    /// <summary><c>install</c> : logiciels absents de ce PC ; <c>update</c> : mises à jour disponibles.</summary>
    private async Task<int> InstallAsync(CliCommand command)
    {
        if (await LoadProfileAsync() is not { } profile)
            return CliCommand.ExitFailed;
        var wanted = command.Verb == "install" ? ProgramState.Missing : ProgramState.UpdateAvailable;
        if (!command.All && profile.Find(command.Target!) is null)
            return Fail(L.T("{0} is not in the profile.", command.Target));
        var last = await _actions.CheckAsync(profile);
        var targets = last.Programs.Where(p => command.All ? p.State == wanted : p.Program.Id == command.Target).ToList();
        if (!command.All && targets.Count == 1 && targets[0].State != wanted)
        {
            var row = new ProgramRow(targets[0], profile.Defaults);
            return Fail($"{row.Name} : {row.State}", targets[0].State == ProgramState.UpToDate ? CliCommand.ExitOk : CliCommand.ExitPending);
        }
        var (done, failed) = await _actions.RunAsync(targets.Select(t => t.Program), last, profile, Progress);
        var after = await _actions.CheckAsync(profile);
        if (_json)
        {
            WriteJson(new JsonObject
            {
                ["done"] = new JsonArray([.. done.Select(p => (JsonNode)p.Id)]),
                ["failed"] = new JsonArray([.. failed.Select(f => (JsonNode)new JsonObject { ["id"] = f.Program.Id, ["message"] = f.Message })]),
                ["programs"] = Programs(after, profile),
            });
        }
        else if (targets.Count == 0)
        {
            output.WriteLine(L.T("Nothing to do."));
        }
        return failed.Count > 0 ? CliCommand.ExitFailed : CliCommand.ExitOk;
    }

    private async Task<int> RevertAsync(string id)
    {
        if (await LoadProfileAsync() is not { } profile)
            return CliCommand.ExitFailed;
        if (profile.Find(id) is not { } program)
            return Fail(L.T("{0} is not in the profile.", id));
        if (!services.Installer.CanRevert(program))
            return Fail(L.T("No previous version of {0} is kept on this PC.", program.Name));
        var outcome = await _actions.RevertAsync(program, Progress);
        if (_json)
            WriteJson(new JsonObject { ["id"] = id, ["succeeded"] = outcome.Succeeded, ["version"] = outcome.Version, ["message"] = outcome.Message });
        return outcome.Succeeded ? CliCommand.ExitOk : CliCommand.ExitFailed;
    }

    private async Task<int> SettingsAsync(bool apply, string id)
    {
        if (await LoadProfileAsync() is not { } profile)
            return CliCommand.ExitFailed;
        if (profile.Tweaks.FirstOrDefault(t => t.Id == id) is not { } tweak)
            return Fail(L.T("{0} is not in the profile.", id));
        try
        {
            _actions.ChangeTweaks([tweak], apply, Progress);
        }
        catch (Exception exc) when (SoftwareActions.IsTweakFailure(exc))
        {
            Log.Error($"Réglages Windows : {exc.Message}");
            return Fail(L.T("The setting could not be changed:\n\n{0}", exc.Message));
        }
        if (_json)
            WriteJson(new JsonObject { ["id"] = id, ["applied"] = services.Tweaks.IsApplied(tweak) });
        return CliCommand.ExitOk;
    }

    private Task<int> ImportAsync(string file)
    {
        var errors = services.Profiles.Import(Path.GetFullPath(file));
        if (_json)
            WriteJson(new JsonObject { ["imported"] = errors.Count == 0, ["errors"] = new JsonArray([.. errors.Select(e => (JsonNode)e)]) });
        if (errors.Count > 0)
            return Task.FromResult(Fail(L.T("The profile is not valid, nothing was imported:\n\n{0}", string.Join("\n", errors))));
        if (!_json)
            output.WriteLine(L.T("Profile imported."));
        return Task.FromResult(CliCommand.ExitOk);
    }

    private async Task<int> SyncAsync()
    {
        var load = await services.Profiles.LoadAsync();
        var synced = load.Profile is not null && !load.FromCache;
        var failure = services.Profiles.Repository is null ? L.T("Profile: on this PC (no GitHub account)") : load.Error;
        if (_json)
            WriteJson(new JsonObject { ["synced"] = synced, ["error"] = synced ? null : failure });
        else
            output.WriteLine(synced ? L.T("Profile: GitHub repository {0}", services.Profiles.Repository) : failure);
        return synced ? CliCommand.ExitOk : CliCommand.ExitFailed;
    }

    // ---- Sorties ---------------------------------------------------------------------------------------------

    /// <summary>Profil du dépôt, sinon copie locale (signalée sur la sortie d'erreur) ; null s'il n'y en a aucun.</summary>
    private async Task<Profile?> LoadProfileAsync()
    {
        var load = await services.Profiles.LoadAsync();
        if (load.Error is not null)
            error.WriteLine(load.Profile is null ? load.Error : L.T("Profile: local copy ({0})", load.Error));
        return load.Profile;
    }

    private void Progress(string text, double? fraction)
    {
        Log.Info(text);
        (_json ? error : output).WriteLine(text);
    }

    /// <summary>Message sur la sortie d'erreur et dans le journal ; renvoie le code de sortie donné.</summary>
    private int Fail(string message, int code = CliCommand.ExitFailed)
    {
        Log.Info(message);
        error.WriteLine(message);
        return code;
    }

    private void WritePrograms(CheckResult result, Profile profile)
    {
        if (result.WingetError is { } wingetError)
            error.WriteLine(L.T("winget: {0}", wingetError));
        var rows = result.Programs.Select(p => new ProgramRow(p, profile.Defaults)).ToList();
        var name = rows.Select(r => r.Name.Length).DefaultIfEmpty(0).Max();
        var installed = rows.Select(r => r.Installed.Length).DefaultIfEmpty(0).Max();
        var available = rows.Select(r => r.Available.Length).DefaultIfEmpty(0).Max();
        foreach (var row in rows)
            output.WriteLine($"{row.Name.PadRight(name)}  {row.Installed.PadRight(installed)}  {row.Available.PadRight(available)}  {row.State}  [{row.Status.Program.Id}]");
    }

    private static JsonArray Programs(CheckResult result, Profile profile) =>
        new([.. result.Programs.Select(p => (JsonNode)new JsonObject
        {
            ["id"] = p.Program.Id,
            ["name"] = p.Program.Name,
            ["installed"] = p.Installed.Present ? p.Installed.Version ?? "" : null,
            ["available"] = p.Latest?.Version,
            ["state"] = JsonNamingPolicy.CamelCase.ConvertName(p.State.ToString()),
            ["mode"] = p.Program.ModeOr(profile.Defaults),
            ["availableOn"] = p.AvailableOn,
            ["message"] = p.Message,
        })]);

    private void WriteJson(JsonNode node) => output.WriteLine(node.ToJsonString(JsonOptions));
}
