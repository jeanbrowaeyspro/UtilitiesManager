using UtilitiesManager.Profiles;
using UtilitiesManager.Software;

namespace UtilitiesManager;

/// <summary>
/// Actions sur les logiciels et les réglages Windows, communes à la fenêtre et à la ligne de commande :
/// vérification, installations dans l'ordre des dépendances, retour arrière, réglages. Chaque étape est signalée à
/// <c>report</c> (texte, avancement entre 0 et 1).
/// </summary>
public sealed class SoftwareActions(AppServices services)
{
    /// <summary>Vérifie tous les logiciels du profil et retient l'état et l'heure de la vérification.</summary>
    public async Task<CheckResult> CheckAsync(Profile profile)
    {
        var result = await services.Checker.CheckAsync(profile, services.State, services.Settings.DeclinedUninstalls);
        services.SaveState();
        services.Settings.LastCheck = DateTimeOffset.Now;
        services.SaveSettings();
        return result;
    }

    /// <summary>
    /// Logiciels installés sur ce PC et absents du profil (winget, programmes inscrits dans Windows, dossier des
    /// portables), y compris ceux à ne plus proposer : <see cref="AppSettings.IsIgnored"/> les distingue.
    /// </summary>
    public async Task<IReadOnlyList<FoundSoftware>> ScanAsync(Profile profile)
    {
        var winget = await services.Winget.ListAsync();
        return await Task.Run(() => services.Inventory.Scan(profile, winget, services.Settings.PortableFolder));
    }

    /// <summary>Téléchargements menés en même temps que les installations.</summary>
    private const int ParallelDownloads = 2;

    /// <summary>
    /// Installe, met à jour ou désinstalle (logiciel retiré du profil) chaque logiciel, dans l'ordre des
    /// dépendances. À partir de deux logiciels, la série est confiée à un processus administrateur : une seule
    /// demande d'élévation pour toute la série (sauf Microsoft Store, qui s'installe mal en administrateur, traité
    /// ici ensuite) ; refusée, chaque installeur la demande lui-même comme avant. <paramref name="control"/> met en
    /// pause ou arrête la série. Renvoie les logiciels réussis et ceux en échec avec leur message.
    /// </summary>
    public async Task<(List<ProgramEntry> Done, List<(ProgramEntry Program, string Message)> Failed)> RunAsync(
        IEnumerable<ProgramEntry> chosen, CheckResult last, Profile profile, Action<string, double?> report,
        Action<BatchStep> step, BatchControl control)
    {
        var items = InstallService.Order(chosen).Select(p => Item(p, last)).ToList();
        foreach (var item in items)
            step(new BatchStep(item.Program.Id, StepState.Waiting));
        var done = new List<ProgramEntry>();
        var failed = new List<(ProgramEntry, string)>();
        var here = items;
        var elevated = items.Where(i => i.Program.Source != Sources.MsStore).ToList();
        if (elevated.Count >= 2 && !ElevatedRunner.IsAdministrator
            && await ElevatedRunner.RunAsync(elevated, profile, Scaled(report, 0, elevated.Count, items.Count), step, control) is { } result)
        {
            services.State.Reload(services.Paths.StateFile);
            var byId = elevated.ToDictionary(i => i.Program.Id, i => i.Program);
            done.AddRange(result.Done!.Select(id => byId[id]));
            failed.AddRange(result.Failed!.Select(f => (byId[f.Id], f.Message)));
            here = items.Except(elevated).ToList();
        }
        var offset = items.Count - here.Count;
        var (hereDone, hereFailed) = await RunHereAsync(here, profile, Scaled(report, offset, here.Count, items.Count), step, control);
        done.AddRange(hereDone);
        failed.AddRange(hereFailed);
        var processed = done.Concat(failed.Select(f => f.Item1)).Select(p => p.Id).ToHashSet();
        var skipped = items.Where(i => !processed.Contains(i.Program.Id)).ToList();
        foreach (var item in skipped)
            step(new BatchStep(item.Program.Id, StepState.Skipped));
        if (skipped.Count > 0)
            report(L.T("Stopped: {0} software not processed.", skipped.Count), 1);
        return (done, failed);
    }

    /// <summary>Logiciel de la série : à désinstaller (retiré du profil), à mettre à jour ou à installer.</summary>
    private static ElevatedItem Item(ProgramEntry program, CheckResult last) =>
        last.Programs.FirstOrDefault(s => s.Program.Id == program.Id) switch
        {
            { State: ProgramState.Removed } s => new ElevatedItem(program, s.Installed),
            { Installed.Present: true } => new ElevatedItem(program, null, Update: true),
            _ => new ElevatedItem(program, null),
        };

    /// <summary>Avancement d'une partie de la série ramené à la série entière.</summary>
    private static Action<string, double?> Scaled(Action<string, double?> report, int offset, int count, int total) =>
        (text, fraction) => report(text, fraction is { } value ? (offset + value * count) / total : null);

    /// <summary>
    /// Mène la série dans ce processus, un logiciel à la fois (Windows n'accepte qu'une installation MSI à la fois) ;
    /// les fichiers des logiciels suivants se téléchargent pendant ce temps. Pause et arrêt prennent effet entre
    /// deux logiciels.
    /// </summary>
    public async Task<(List<ProgramEntry> Done, List<(ProgramEntry Program, string Message)> Failed)> RunHereAsync(
        IReadOnlyList<ElevatedItem> items, Profile profile, Action<string, double?> report, Action<BatchStep> step,
        BatchControl control)
    {
        var desktop = DesktopIcons.OfThisUser();
        var done = new List<ProgramEntry>();
        var failed = new List<(ProgramEntry, string)>();
        // Le premier logiciel se télécharge à son tour, avec sa progression ; les suivants à l'avance.
        using var slots = new SemaphoreSlim(ParallelDownloads);
        var ahead = items.Skip(1).Where(i => i.Removed is null && InstallService.CanDownloadAhead(i.Program))
                         .ToDictionary(i => i.Program, i => DownloadAheadAsync(i.Program, slots, control));
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var (program, removed, update) = items[i];
                if (control.Paused)
                    step(new BatchStep(program.Id, StepState.Paused));
                await control.WaitIfPausedAsync();
                if (control.Stopped)
                    break;
                step(new BatchStep(program.Id, StepState.Running));
                var icons = update ? desktop.Snapshot() : null;
                var index = i;
                var progress = new Progress<InstallStep>(step =>
                    report($"{step.Program} : {step.Text}", (index + (step.Fraction ?? 0)) / items.Count));
                var download = ahead.GetValueOrDefault(program);
                InstallOutcome outcome;
                try
                {
                    outcome = removed is not null
                        ? await services.Installer.UninstallAsync(program, removed, progress)
                        : await services.Installer.InstallAsync(program, profile, progress, download);
                }
                catch (OperationCanceledException) when (control.Stopped)
                {
                    // Son téléchargement à l'avance a été annulé par l'arrêt : rien n'a été installé.
                    break;
                }
                catch (Exception exc) when (exc is not OutOfMemoryException)
                {
                    Log.Error($"{program.Id} : {exc}");
                    outcome = new InstallOutcome(InstallResult.Failed, exc.Message);
                }
                if (download is { IsCompletedSuccessfully: true })
                    download.Result.Discard();
                if (icons is not null)
                    RemoveIcons(program, desktop, icons, report, (i + 1.0) / items.Count);
                Record(program, outcome, i);
            }
        }
        finally
        {
            foreach (var download in ahead.Values)
                await DiscardAsync(download);
        }
        return (done, failed);

        void Record(ProgramEntry program, InstallOutcome outcome, int i)
        {
            services.SaveState();
            if (outcome.Succeeded)
            {
                step(new BatchStep(program.Id, StepState.Done, outcome.Version));
                done.Add(program);
                report(L.T("{0}: done.", program.Name), (i + 1.0) / items.Count);
            }
            else
            {
                var message = outcome.Message ?? L.T("cancelled");
                step(new BatchStep(program.Id, StepState.Failed, message));
                failed.Add((program, message));
                report(L.T("{0}: {1}", program.Name, message), (i + 1.0) / items.Count);
            }
        }
    }

    /// <summary>Retire les icônes qu'une mise à jour vient d'ajouter au Bureau.</summary>
    private static void RemoveIcons(ProgramEntry program, DesktopIcons desktop, HashSet<string> before,
                                    Action<string, double?> report, double fraction)
    {
        foreach (var (name, error) in desktop.RemoveAdded(before))
            report(error is null
                ? L.T("{0}: icon \"{1}\" removed from the Desktop.", program.Name, name)
                : L.T("{0}: icon \"{1}\" left on the Desktop: {2}", program.Name, name, error), fraction);
    }

    /// <summary>Téléchargement à l'avance : attend la fin d'une pause pour commencer, annulé par l'arrêt.</summary>
    private async Task<Download> DownloadAheadAsync(ProgramEntry program, SemaphoreSlim slots, BatchControl control)
    {
        await control.WaitIfPausedAsync().ConfigureAwait(false);
        await slots.WaitAsync(control.StopToken).ConfigureAwait(false);
        try
        {
            return await services.Installer.DownloadAheadAsync(program, control.StopToken).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>Supprime un téléchargement à l'avance resté inutilisé (son échec est déjà signalé à l'installation).</summary>
    private static async Task DiscardAsync(Task<Download> download)
    {
        try
        {
            (await download.ConfigureAwait(false)).Discard();
        }
        catch (OperationCanceledException)
        {
            // Arrêt demandé : le dossier a déjà été supprimé.
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            Log.Error($"Téléchargement à l'avance : {exc.Message}");
        }
    }

    /// <summary>Revient à la version précédente gardée sur ce PC.</summary>
    public async Task<InstallOutcome> RevertAsync(ProgramEntry program, Action<string, double?> report)
    {
        var progress = new Progress<InstallStep>(step => report($"{step.Program} : {step.Text}", step.Fraction));
        var outcome = await services.Installer.RevertAsync(program, progress);
        services.SaveState();
        report(outcome.Succeeded ? L.T("{0}: done.", program.Name) : L.T("{0}: {1}", program.Name, outcome.Message ?? ""), null);
        return outcome;
    }

    /// <summary>
    /// Applique ou rétablit les réglages ; redémarre l'Explorateur si l'un d'eux le demande. Lève l'exception
    /// d'accès au registre en cas d'échec.
    /// </summary>
    public async Task ChangeTweaksAsync(IReadOnlyList<TweakEntry> tweaks, bool apply, Action<string, double?> report)
    {
        foreach (var tweak in tweaks)
        {
            await (apply ? services.Tweaks.ApplyAsync(tweak) : services.Tweaks.RestoreAsync(tweak));
            report(L.T("{0}: {1}", tweak.Name, apply ? L.T("applied") : L.T("restored")), null);
        }
        if (tweaks.Any(t => t.RestartExplorer))
            WindowsTweaks.RestartExplorer();
    }

    /// <summary>Exceptions d'accès au registre attendues d'un réglage Windows.</summary>
    public static bool IsTweakFailure(Exception exc) =>
        exc is UnauthorizedAccessException or IOException or System.Security.SecurityException;
}
