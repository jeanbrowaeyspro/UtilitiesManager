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
    /// Installe, met à jour ou désinstalle (logiciel retiré du profil) chaque logiciel, dans l'ordre des
    /// dépendances ; renvoie les logiciels réussis et ceux en échec avec leur message.
    /// </summary>
    public async Task<(List<ProgramEntry> Done, List<(ProgramEntry Program, string Message)> Failed)> RunAsync(
        IEnumerable<ProgramEntry> chosen, CheckResult last, Profile profile, Action<string, double?> report)
    {
        var programs = InstallService.Order(chosen);
        var done = new List<ProgramEntry>();
        var failed = new List<(ProgramEntry, string)>();
        for (var i = 0; i < programs.Count; i++)
        {
            var program = programs[i];
            var index = i;
            var progress = new Progress<InstallStep>(step =>
                report($"{step.Program} : {step.Text}", (index + (step.Fraction ?? 0)) / programs.Count));
            var status = last.Programs.FirstOrDefault(p => p.Program.Id == program.Id);
            InstallOutcome outcome;
            try
            {
                outcome = status?.State == ProgramState.Removed
                    ? await services.Installer.UninstallAsync(program, status.Installed, progress)
                    : await services.Installer.InstallAsync(program, profile, progress);
            }
            catch (Exception exc) when (exc is not OutOfMemoryException)
            {
                Log.Error($"{program.Id} : {exc}");
                outcome = new InstallOutcome(InstallResult.Failed, exc.Message);
            }
            services.SaveState();
            if (outcome.Succeeded)
            {
                done.Add(program);
                report(L.T("{0}: done.", program.Name), (i + 1.0) / programs.Count);
            }
            else
            {
                var message = outcome.Message ?? L.T("cancelled");
                failed.Add((program, message));
                report(L.T("{0}: {1}", program.Name, message), (i + 1.0) / programs.Count);
            }
        }
        return (done, failed);
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
    public void ChangeTweaks(IReadOnlyList<TweakEntry> tweaks, bool apply, Action<string, double?> report)
    {
        foreach (var tweak in tweaks)
        {
            if (apply)
                services.Tweaks.Apply(tweak);
            else
                services.Tweaks.Restore(tweak);
            report(L.T("{0}: {1}", tweak.Name, apply ? L.T("applied") : L.T("restored")), null);
        }
        if (tweaks.Any(t => t.RestartExplorer))
            WindowsTweaks.RestartExplorer();
    }

    /// <summary>Exceptions d'accès au registre attendues d'un réglage Windows.</summary>
    public static bool IsTweakFailure(Exception exc) =>
        exc is UnauthorizedAccessException or IOException or System.Security.SecurityException;
}
