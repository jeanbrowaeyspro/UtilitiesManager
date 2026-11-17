using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

public enum ProgramState
{
    UpToDate,
    UpdateAvailable,
    /// <summary>Nouvelle version plus récente que l'âge minimum : proposée plus tard.</summary>
    Waiting,
    /// <summary>Version figée par le profil.</summary>
    Blocked,
    /// <summary>Logiciel du profil absent de ce PC : proposé à l'installation.</summary>
    Missing,
    Error,
    /// <summary>Logiciel retiré du profil, encore installé ici : proposé à la désinstallation.</summary>
    Removed,
    /// <summary>
    /// Logiciel absent qui ne sert que de dépendance : installé avec le logiciel qui en dépend, jamais proposé seul.
    /// Message : noms des logiciels qui en dépendent.
    /// </summary>
    Dependency,
}

/// <summary>État d'un logiciel après vérification.</summary>
public sealed record ProgramStatus(ProgramEntry Program, InstalledInfo Installed, LatestInfo? Latest, ProgramState State,
                                   string? Message = null, DateTimeOffset? AvailableOn = null)
{
    /// <summary>Action à proposer (ou à faire seule en mode auto).</summary>
    public bool NeedsAction => State is ProgramState.UpdateAvailable or ProgramState.Missing or ProgramState.Removed;
}

public sealed record CheckResult(IReadOnlyList<ProgramStatus> Programs, string? WingetError)
{
    /// <summary>Mises à jour à installer sans question (mode auto, logiciel déjà installé).</summary>
    public IEnumerable<ProgramStatus> AutoUpdates(ProfileDefaults defaults) =>
        Programs.Where(p => p.State == ProgramState.UpdateAvailable && p.Program.ModeOr(defaults) == Modes.Auto);

    /// <summary>Actions à proposer : mises à jour en mode confirm, logiciels absents, logiciels retirés du profil.</summary>
    public IEnumerable<ProgramStatus> Pending(ProfileDefaults defaults) =>
        Programs.Where(p => p.NeedsAction && !(p.State == ProgramState.UpdateAvailable && p.Program.ModeOr(defaults) == Modes.Auto));
}

/// <summary>
/// Vérification de tous les logiciels du profil : version installée, dernière version, âge minimum, version
/// bloquée ; logiciels retirés du profil encore installés.
/// </summary>
public sealed class CheckEngine(Winget winget, Detector detector, LatestResolver latest, ShellCommands shell, TimeProvider? clock = null)
{
    private const int Parallel = 6;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<CheckResult> CheckAsync(Profile profile, MachineState state, IReadOnlyCollection<string> declinedUninstalls,
                                              CancellationToken cancel = default)
    {
        shell.Reset();
        IReadOnlyList<WingetPackage> packages = [];
        string? wingetError = null;
        try
        {
            packages = await winget.ListAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            wingetError = exc.Message;
            Log.Error($"winget list : {exc.Message}");
        }

        var removed = state.Managed.Where(m => profile.Find(m.Id) is null && !declinedUninstalls.Contains(m.Id)).ToList();
        var entries = profile.Programs.Concat(removed).ToList();
        using var gate = new SemaphoreSlim(Parallel);
        var found = await Task.WhenAll(entries.Select(async program =>
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                return await ProbeAsync(program, packages, state, wingetError, cancel).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var results = new List<ProgramStatus>();
        for (var i = 0; i < entries.Count; i++)
        {
            var (installed, newest, newer, error) = found[i];
            var program = entries[i];
            var status = profile.Find(program.Id) is null
                ? Removed(program, installed, state)
                : Evaluate(program, installed, newest, newer, error, profile.Defaults, state, now,
                           profile.Programs.Where(p => p.DependsOn == program.Id).Select(p => p.Name).ToList());
            if (status is null)
                continue;
            if (installed.Present && status.State != ProgramState.Removed)
                state.Remember(program);
            results.Add(status);
        }
        return new CheckResult(results, wingetError);
    }

    private async Task<(InstalledInfo, LatestInfo?, bool, string?)> ProbeAsync(ProgramEntry program, IReadOnlyList<WingetPackage> packages,
                                                                               MachineState state, string? wingetError, CancellationToken cancel)
    {
        InstalledInfo installed;
        try
        {
            installed = await detector.DetectAsync(program, packages, state, cancel).ConfigureAwait(false);
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            Log.Error($"{program.Id} : détection : {exc.Message}");
            return (InstalledInfo.Absent, null, false, exc.Message);
        }
        if (program.IsWinget && wingetError is not null)
            return (installed, null, false, wingetError);
        try
        {
            var newest = await latest.ResolveAsync(program, packages, installed, cancel).ConfigureAwait(false);
            var newer = newest is not null && installed.Present && (installed.Version is { } version
                ? VersionText.IsNewer(newest.Version, version)
                : program.IsWinget && Winget.Match(program, packages)?.Available.Length > 0);
            return (installed, newest, newer, null);
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            Log.Error($"{program.Id} : dernière version : {exc.Message}");
            return (installed, null, false, exc.Message);
        }
    }

    /// <summary>État d'un logiciel du profil ; <paramref name="dependents"/> : noms des logiciels qui en dépendent.</summary>
    public static ProgramStatus Evaluate(ProgramEntry program, InstalledInfo installed, LatestInfo? newest, bool newer, string? error,
                                         ProfileDefaults defaults, MachineState state, DateTimeOffset now,
                                         IReadOnlyList<string>? dependents = null)
    {
        if (!installed.Present)
        {
            if (error is not null)
                return new ProgramStatus(program, installed, newest, ProgramState.Error, error);
            return dependents is { Count: > 0 }
                ? new ProgramStatus(program, installed, newest, ProgramState.Dependency, string.Join(", ", dependents))
                : new ProgramStatus(program, installed, newest, ProgramState.Missing);
        }
        if (program.BlockedVersion is not null)
            return new ProgramStatus(program, installed, newest, ProgramState.Blocked);
        if (error is not null)
            return new ProgramStatus(program, installed, newest, ProgramState.Error, error);
        if (newest is null || !newer)
            return new ProgramStatus(program, installed, newest, ProgramState.UpToDate);
        var published = newest.Published ?? state.Seen(program.Id, newest.Version, now);
        var availableOn = published.AddDays(program.MinAgeOr(defaults));
        return availableOn > now
            ? new ProgramStatus(program, installed, newest, ProgramState.Waiting, AvailableOn: availableOn)
            : new ProgramStatus(program, installed, newest, ProgramState.UpdateAvailable);
    }

    /// <summary>Logiciel sorti du profil : proposé à la désinstallation s'il est encore là, oublié sinon.</summary>
    private static ProgramStatus? Removed(ProgramEntry program, InstalledInfo installed, MachineState state)
    {
        if (installed.Present)
            return new ProgramStatus(program, installed, null, ProgramState.Removed);
        state.Forget(program.Id);
        return null;
    }
}
