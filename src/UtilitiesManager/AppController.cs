using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using UtilitiesManager.GitHub;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;
using UtilitiesManager.Updates;
using UtilitiesManager.ViewModels;

namespace UtilitiesManager;

/// <summary>
/// Cœur de l'application en arrière-plan : profil, vérification au démarrage puis chaque jour, installations
/// (seules en mode auto, sur confirmation sinon), icône de notifications, fenêtre principale, mise à jour de
/// l'outil lui-même (jamais silencieuse).
/// </summary>
public sealed class AppController
{
    /// <summary>Intervalle des contrôles du minuteur (vérification quotidienne, mise à jour horaire).</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly Application _app;
    private readonly DispatcherTimer _timer;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private UpdateWindow? _updateWindow;
    private readonly SoftwareActions _actions;
    private OperationLock? _lock;

    public AppController(Application app, AppServices services)
    {
        _app = app;
        Services = services;
        _actions = new SoftwareActions(services);
        _timer = new DispatcherTimer { Interval = Tick };
        _timer.Tick += async (_, _) => await OnTickAsync();
    }

    public AppServices Services { get; }
    public AppSettings Settings => Services.Settings;
    public IDialogs Dialogs => Services.Dialogs;

    public Profile? Profile { get; private set; }

    /// <summary>Origine du profil affiché (dépôt, copie locale, ou erreur).</summary>
    public string ProfileOrigin { get; private set; } = "";
    /// <summary>Compte connecté mais dépôt du profil introuvable ou sans accès : création guidée à proposer.</summary>
    public bool ProfileRepositoryMissing { get; private set; }

    /// <summary>Dernière vérification ; la remplacer efface les étapes de la série précédente.</summary>
    public CheckResult? Last
    {
        get => _last;
        private set
        {
            _last = value;
            _steps.Clear();
        }
    }

    private CheckResult? _last;
    private readonly Dictionary<string, BatchStep> _steps = [];

    /// <summary>Étape d'un logiciel dans la série en cours ou la dernière (jusqu'à la vérification suivante).</summary>
    public BatchStep? StepOf(string id) => _steps.GetValueOrDefault(id);

    /// <summary>Un logiciel de la série change d'étape.</summary>
    public event Action<BatchStep>? Stepped;

    private void OnStep(BatchStep step)
    {
        _steps[step.Id] = step;
        Stepped?.Invoke(step);
    }

    public bool Busy { get; private set; }

    /// <summary>Dernières lignes du journal de la session (affichées à l'ouverture de la fenêtre).</summary>
    public IReadOnlyList<string> RecentLog => _recent;
    private readonly List<string> _recent = [];

    /// <summary>Nombre d'actions proposées (mises à jour à confirmer, logiciels à installer ou à désinstaller).</summary>
    public int PendingCount => Profile is { } profile && Last is { } last ? last.Pending(profile.Defaults).Count() : 0;

    public event Action? StateChanged;
    public event Action<string>? LogLine;
    public event Action<double?>? Progress;

    /// <summary>Fenêtre propriétaire des messages : la fenêtre principale si elle est affichée.</summary>
    public Window? DialogOwner =>
        _app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible) ?? (_window is { IsVisible: true } ? _window : null);

    public async Task StartAsync(AppArguments args)
    {
        if (args.LauncherPath is { } launcher && !Services.Setup.IsInstalled && !Services.Paths.IsInInstallDir(launcher)
            && Dialogs.Confirm(L.T("Install UtilitiesManager on this PC?\n\nIt will check your software at each Windows start and stay in the background."),
                               L.T("Install"), L.T("Not now")))
        {
            Services.Setup.Install(launcher, Settings);
        }
        _tray = new TrayIcon(ShowMainWindow, () => _ = CheckAsync(manual: true), Quit, Settings.TrayIcon,
                             () => Batch, PauseOrResume, StopBatch);
        if (!args.Startup)
            ShowMainWindow();
        ShowNewsAfterUpdate(args.Startup);
        _timer.Start();
        await CheckAsync(manual: !args.Startup);
        await CheckSelfUpdateAsync(manual: false);
    }

    /// <summary>Ligne de commande d'une deuxième instance : ouvre la fenêtre (sauf lancement de session).</summary>
    public void OnForwarded(AppArguments args)
    {
        if (!args.Startup)
            ShowMainWindow();
    }

    private async Task OnTickAsync()
    {
        var now = DateTimeOffset.Now;
        if (Settings.LastCheck is not { } last || now - last >= CheckInterval)
            await CheckAsync(manual: false);
        await CheckSelfUpdateAsync(manual: false);
    }

    public void ShowMainWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow(this);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Quit()
    {
        if (Busy && !Dialogs.Confirm(L.T("An installation is in progress. Quit anyway?")))
            return;
        _timer.Stop();
        _tray?.Dispose();
        _tray = null;
        _app.Shutdown();
    }

    // ---- Profil ----------------------------------------------------------------------------------------------

    /// <summary>Origine du profil quand il est à jour : sur ce PC sans compte, sinon le dépôt GitHub.</summary>
    private string SavedOrigin() => Services.Profiles.Repository is { } repository
        ? L.T("Profile: GitHub repository {0}", repository)
        : L.T("Profile: on this PC (no GitHub account)");

    private async Task LoadProfileAsync()
    {
        var load = await Services.Profiles.LoadAsync();
        ProfileRepositoryMissing = load.RepositoryMissing;
        if (load.Profile is not null)
        {
            Profile = load.Profile;
            ProfileOrigin = Services.Profiles.Repository is null || !load.FromCache ? SavedOrigin()
                : load.RepositoryMissing ? load.Error!
                : L.T("Profile: local copy ({0})", load.Error ?? L.T("repository unreachable"));
        }
        else
        {
            ProfileOrigin = load.Error ?? L.T("No profile: sign in to GitHub or import a profile file (Settings tab).");
        }
    }

    /// <summary>
    /// Modifie le profil (copie), le contrôle et l'enregistre dans le dépôt ; rien n'est appliqué s'il est invalide.
    /// Renvoie vrai si c'est fait.
    /// </summary>
    public async Task<bool> EditProfileAsync(Action<Profile> edit, string message)
    {
        if (Profile is null)
            return false;
        var changed = Profile.Clone();
        edit(changed);
        var errors = ProfileValidator.Validate(changed);
        if (errors.Count > 0)
        {
            Dialogs.Error(L.T("The profile is not valid, nothing was saved:\n\n{0}", string.Join("\n", errors)));
            return false;
        }
        try
        {
            await Services.Profiles.SaveAsync(changed, message);
        }
        catch (Exception exc) when (exc is GitHubException or HttpRequestException or IOException)
        {
            Log.Error($"Enregistrement du profil : {exc.Message}");
            Dialogs.Error(L.T("The profile could not be saved:\n\n{0}", exc.Message));
            return false;
        }
        Profile = changed;
        // Enregistré là où il doit l'être : le bandeau ne parle plus de copie locale ni de dépôt manquant.
        ProfileOrigin = SavedOrigin();
        ProfileRepositoryMissing = false;
        await CheckAsync(manual: true);
        return true;
    }

    public async Task ImportProfileAsync(string file)
    {
        IReadOnlyList<string> errors;
        try
        {
            errors = Services.Profiles.Import(file);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            errors = [exc.Message];
        }
        if (errors.Count > 0)
        {
            Dialogs.Error(L.T("The profile is not valid, nothing was imported:\n\n{0}", string.Join("\n", errors)));
            return;
        }
        if (Services.Account.IsSignedIn)
            await ConnectProfileAsync(offerRepository: false);
        else
            await CheckAsync(manual: true);
    }

    /// <summary>
    /// Après la connexion ou un import : guide la création du dépôt privé du profil sur le compte de l'utilisateur
    /// et l'accès de l'application à ce seul dépôt, propose d'y envoyer la copie locale s'il est vide, puis vérifie.
    /// </summary>
    public async Task ConnectProfileAsync(bool offerRepository = true)
    {
        var repository = Services.Profiles.Repository;
        try
        {
            switch (await Services.Profiles.CheckAccessAsync())
            {
                case ProfileAccess.NoRepository when offerRepository:
                    if (Dialogs.Confirm(L.T("The private repository {0} does not exist on your GitHub account yet, or UtilitiesManager has no access to it.\n\nCreate it on GitHub (private), then allow access.", repository),
                                        L.T("Create the repository"), L.T("It already exists")))
                        Ui.OpenExternal(ProfileStore.CreationUrl);
                    if (Dialogs.Confirm(L.T("Allow UtilitiesManager to access the repository {0} only (\"Only select repositories\"), then click \"Check now\".", repository),
                                        L.T("Allow access"), L.T("Later")))
                        Ui.OpenExternal(GitHubSignIn.InstallationUrl);
                    return;
                case ProfileAccess.NoProfileFile when Profile is null:
                    await Services.Profiles.SaveAsync(new Profile(), "Profil initial");
                    break;
                case ProfileAccess.NoProfileFile when Profile is { } local:
                    if (Dialogs.Confirm(L.T("The repository {0} does not contain a profile yet. Send the local copy of the profile to it?", repository),
                                        L.T("Send"), L.T("Later")))
                        await Services.Profiles.SaveAsync(local, "Profil initial");
                    break;
            }
        }
        catch (Exception exc) when (exc is GitHubException or HttpRequestException or IOException)
        {
            Log.Error($"Dépôt du profil : {exc.Message}");
            Dialogs.Error(L.T("The profile repository could not be reached:\n\n{0}", exc.Message));
        }
        await CheckAsync(manual: true);
    }

    // ---- Vérification et installations -----------------------------------------------------------------------

    /// <summary>
    /// Recharge le profil, vérifie tous les logiciels, installe les mises à jour en mode auto (notification à la
    /// fin) et ouvre la fenêtre si une action attend une confirmation.
    /// </summary>
    public async Task CheckAsync(bool manual)
    {
        if (!Begin())
            return;
        try
        {
            Report(L.T("Checking the software…"), null);
            await LoadProfileAsync();
            if (Profile is not { } profile)
            {
                Last = null;
                return;
            }
            Last = await _actions.CheckAsync(profile);
            if (Last.WingetError is { } error)
                Report(L.T("winget: {0}", error), null);
            StateChanged?.Invoke();

            var auto = Last.AutoUpdates(profile.Defaults).Select(s => s.Program).ToList();
            if (auto.Count > 0)
            {
                var (done, failed) = await RunBatchAsync(auto, Last, profile);
                var doneNames = string.Join(", ", done.Select(p => p.Name));
                _tray?.Notify(AppPaths.AppName, failed.Count == 0
                    ? L.T("Updated: {0}", doneNames)
                    : L.T("Updated: {0}\nFailed: {1}", done.Count > 0 ? doneNames : L.T("none"), string.Join(", ", failed.Select(f => f.Program.Name))),
                    ShowMainWindow);
                Last = await _actions.CheckAsync(profile);
            }
            Report(L.T("Check finished."), null);
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            Log.Error($"Vérification : {exc}");
            Report(L.T("Check failed: {0}", exc.Message), null);
        }
        finally
        {
            End();
        }
        _tray?.SetPending(PendingCount);
        if (!manual && PendingCount > 0)
            ShowMainWindow();
        AskRemovedPrograms();
    }

    /// <summary>
    /// Logiciels retirés du profil, encore installés : désinstallation proposée ; un refus est retenu pour ce PC.
    /// Seulement quand la fenêtre est affichée (jamais de question surgissant sans fenêtre).
    /// </summary>
    private void AskRemovedPrograms()
    {
        if (_window is null || Last is null || Busy)
            return;
        var removed = Last.Programs.Where(p => p.State == ProgramState.Removed).ToList();
        var accepted = new List<ProgramStatus>();
        foreach (var status in removed)
        {
            if (Dialogs.Confirm(L.T("{0} is no longer in the profile. Uninstall it from this PC?", status.Program.Name),
                                L.T("Uninstall"), L.T("No")))
            {
                accepted.Add(status);
            }
            else
            {
                Settings.DeclinedUninstalls.Add(status.Program.Id);
                Services.SaveSettings();
            }
        }
        if (accepted.Count < removed.Count)
        {
            Last = Last with { Programs = Last.Programs.Where(p => p.State != ProgramState.Removed || accepted.Contains(p)).ToList() };
            StateChanged?.Invoke();
            _tray?.SetPending(PendingCount);
        }
        if (accepted.Count > 0)
            _ = InstallAsync(accepted);
    }

    /// <summary>Installe, met à jour ou désinstalle (logiciels retirés du profil) les logiciels choisis, puis revérifie.</summary>
    public async Task InstallAsync(IReadOnlyList<ProgramStatus> chosen)
    {
        if (Profile is not { } profile || Last is not { } last || chosen.Count == 0 || !Begin())
            return;
        try
        {
            var (_, failed) = await RunBatchAsync(chosen.Select(s => s.Program), last, profile);
            if (failed.Count > 0)
                Dialogs.Error(L.T("Failed: {0}\n\nSee the log below the list.", string.Join(", ", failed.Select(f => f.Program.Name))));
        }
        finally
        {
            End();
        }
        await CheckAsync(manual: true);
    }

    /// <summary>Série d'installations en cours (pause, arrêt), null en dehors.</summary>
    public BatchControl? Batch { get; private set; }

    private async Task<(List<ProgramEntry> Done, List<(ProgramEntry Program, string Message)> Failed)> RunBatchAsync(
        IEnumerable<ProgramEntry> programs, CheckResult last, Profile profile)
    {
        Batch = new BatchControl();
        Batch.Changed += () => StateChanged?.Invoke();
        StateChanged?.Invoke();
        try
        {
            return await _actions.RunAsync(programs, last, profile, Report, OnStep, Batch);
        }
        finally
        {
            Batch = null;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Met en pause la série en cours (après le logiciel en cours) ou la reprend.</summary>
    public void PauseOrResume()
    {
        if (Batch is not { Stopped: false } batch)
            return;
        if (batch.Paused)
        {
            Note(L.T("Installations resumed."));
            batch.Resume();
        }
        else
        {
            Note(L.T("Pause: the installations wait after the current software."));
            batch.Pause();
        }
    }

    /// <summary>Arrête la série en cours : le logiciel en cours va jusqu'au bout, les suivants ne sont pas traités.</summary>
    public void StopBatch()
    {
        if (Batch is not { Stopped: false } batch)
            return;
        Note(L.T("Stop: the current software finishes, the next ones are not processed."));
        batch.Stop();
    }

    public bool CanRevert(ProgramStatus status) => Services.Installer.CanRevert(status.Program);

    public async Task RevertAsync(ProgramStatus status)
    {
        if (!Begin())
            return;
        try
        {
            var outcome = await _actions.RevertAsync(status.Program, Report);
            if (!outcome.Succeeded)
                Dialogs.Error(outcome.Message ?? L.T("Failed."));
        }
        finally
        {
            End();
        }
        await CheckAsync(manual: true);
    }

    /// <summary>
    /// Début d'une opération : refusée si une autre est en cours ici ou dans une commande (verrou commun).
    /// </summary>
    private bool Begin()
    {
        if (Busy)
            return false;
        _lock = OperationLock.TryAcquire();
        if (_lock is null)
        {
            Report(L.T("Another UtilitiesManager operation is in progress."), null);
            return false;
        }
        SetBusy(true);
        return true;
    }

    private void End()
    {
        _lock?.Dispose();
        _lock = null;
        SetBusy(false);
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        if (!busy)
            Progress?.Invoke(null);
        StateChanged?.Invoke();
    }

    private void Report(string text, double? fraction)
    {
        Note(text);
        Progress?.Invoke(fraction);
    }

    /// <summary>Ligne du journal, sans toucher à la barre d'avancement.</summary>
    private void Note(string text)
    {
        Log.Info(text);
        _recent.Add(text);
        if (_recent.Count > 500)
            _recent.RemoveRange(0, 100);
        LogLine?.Invoke(text);
    }

    // ---- Réglages Windows ------------------------------------------------------------------------------------

    public bool IsApplied(TweakEntry tweak) => Services.Tweaks.IsApplied(tweak);

    /// <summary>
    /// Ajoute au profil, après confirmation, les réglages du catalogue déjà posés sur ce PC et absents du profil.
    /// </summary>
    public async Task CaptureTweaksAsync()
    {
        if (Profile is null)
            return;
        var known = Profile.Tweaks.Select(t => t.Id).ToHashSet();
        var found = TweakCatalog.Load().Where(t => !known.Contains(t.Id) && IsApplied(t)).ToList();
        if (found.Count == 0)
        {
            Dialogs.Info(L.T("No other known Windows setting is changed on this PC."));
            return;
        }
        foreach (var tweak in found)
            tweak.Name = L.T(tweak.Name);
        if (Dialogs.Confirm(L.T("Add these settings of this PC to the profile?\n\n{0}", string.Join("\n", found.Select(t => "- " + t.Name)))))
            await EditProfileAsync(p => p.Tweaks.AddRange(found), "Réglages Windows relevés sur ce PC");
    }

    // ---- Relevé des logiciels de ce PC -----------------------------------------------------------------------

    public Task<IReadOnlyList<FoundSoftware>> ScanAsync(Profile profile) => _actions.ScanAsync(profile);

    /// <summary>
    /// Sources possibles d'un logiciel relevé (winget, Chocolatey, GitHub, page à compléter), barre d'avancement
    /// animée pendant la recherche ; null si une autre opération est en cours.
    /// </summary>
    public async Task<IReadOnlyList<SourceCandidate>?> FindSourcesAsync(FoundSoftware found, Profile profile)
    {
        if (!Begin())
            return null;
        try
        {
            Report(L.T("Searching for a source for {0}…", found.Name), null);
            return await Services.SourceFinder.FindAsync(found, profile);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Applique ou rétablit les réglages choisis ; redémarre l'Explorateur si l'un d'eux le demande.</summary>
    public void ChangeTweaks(IReadOnlyList<TweakEntry> tweaks, bool apply)
    {
        try
        {
            _actions.ChangeTweaks(tweaks, apply, Report);
        }
        catch (Exception exc) when (SoftwareActions.IsTweakFailure(exc))
        {
            Log.Error($"Réglages Windows : {exc.Message}");
            Dialogs.Error(L.T("The setting could not be changed:\n\n{0}", exc.Message));
        }
        StateChanged?.Invoke();
    }

    // ---- Paramètres ------------------------------------------------------------------------------------------

    /// <summary>Enregistre les réglages du poste et applique le démarrage avec Windows et l'icône.</summary>
    public void SaveSettings()
    {
        if (Services.Setup.IsInstalled)
            Services.Setup.Apply(Settings);
        else
            Services.SaveSettings();
        if (_tray is not null)
            _tray.Visible = Settings.TrayIcon;
    }

    public void SetTheme(string theme)
    {
        Settings.Theme = theme;
        Services.SaveSettings();
        ThemeManager.Apply(_app, theme);
    }

    /// <summary>Change la langue : menu de l'icône refait, fenêtre principale reconstruite.</summary>
    public void SetLanguage(string language)
    {
        Settings.Language = language;
        Services.SaveSettings();
        L.Use(language);
        _tray?.Rebuild();
        _tray?.SetPending(PendingCount);
        if (_window is { } window)
        {
            _window = null;
            window.Close();
            ShowMainWindow();
        }
    }

    // ---- Mise à jour de UtilitiesManager ----------------------------------------------------------------------

    /// <summary>
    /// Recherche (au plus une fois par heure, sauf demande) une nouvelle version : notification puis fenêtre de
    /// proposition, jamais d'installation silencieuse.
    /// </summary>
    public async Task CheckSelfUpdateAsync(bool manual)
    {
        var now = DateTimeOffset.Now;
        if (!manual && !UpdateService.IsCheckDue(Settings.LastUpdateCheck, now))
            return;
        AvailableUpdate? update;
        try
        {
            update = await Services.CreateUpdateService().CheckAsync(SemVersion.Parse(AppInfo.Version), Settings.IncludePrereleases,
                                                                      Settings.SkippedVersion, manual);
            Settings.LastUpdateCheck = now;
            Services.SaveSettings();
        }
        catch (Exception exc) when (exc is GitHubException or HttpRequestException or TaskCanceledException)
        {
            Log.Error($"Recherche de mise à jour : {exc.Message}");
            if (manual)
                Dialogs.Error(L.T("The update check failed:\n\n{0}", exc.Message));
            return;
        }
        if (update is null)
        {
            if (manual)
                Dialogs.Info(L.T("UtilitiesManager {0} is up to date.", AppInfo.Version));
            return;
        }
        if (!manual)
            _tray?.Notify(AppPaths.AppName, L.T("UtilitiesManager {0} is available", update.Version), () => ShowUpdate(update));
        ShowUpdate(update);
    }

    private void ShowUpdate(AvailableUpdate update)
    {
        if (_updateWindow is not null)
        {
            _updateWindow.Activate();
            return;
        }
        _updateWindow = new UpdateWindow(update, Settings.ShowReleaseNotes, progress => InstallUpdateAsync(update, progress));
        var choice = _updateWindow.ShowDialog() == true ? _updateWindow.Choice : UpdateChoice.Later;
        _updateWindow = null;
        if (choice == UpdateChoice.Skip)
        {
            Settings.SkippedVersion = update.Version.ToString();
            Services.SaveSettings();
        }
        else if (choice == UpdateChoice.Install && Services.Setup.IsInstalled)
        {
            Log.Info($"Mise à jour vers {update.Version} : redémarrage");
            Process.Start(Services.SelfUpdate.Restart())?.Dispose();
            _timer.Stop();
            _tray?.Dispose();
            _tray = null;
            _app.Shutdown();
        }
    }

    /// <summary>Télécharge et contrôle (SHA-256) le nouveau lanceur et le met en place (relance après la fenêtre).</summary>
    private async Task InstallUpdateAsync(AvailableUpdate update, IProgress<double> progress)
    {
        if (!Services.Setup.IsInstalled)
        {
            Ui.OpenExternal(update.Release.PageUrl ?? $"https://github.com/{UpdateService.Repository}/releases");
            return;
        }
        await Services.CreateUpdateService().DownloadAsync(update, Services.SelfUpdate.DownloadedLauncher, progress);
        Services.SelfUpdate.Replace();
    }

    /// <summary>Après une mise à jour de l'outil : nouveautés des versions passées depuis la dernière lancée.</summary>
    private void ShowNewsAfterUpdate(bool startup)
    {
        var previous = Settings.LastSeenVersion;
        if (startup || previous == AppInfo.Version)
            return;
        Settings.LastSeenVersion = AppInfo.Version;
        Services.SaveSettings();
        if (!SemVersion.TryParse(previous, out var old))
            return;
        var entries = Changelog.Between(NewsWindow.Embedded(), old, SemVersion.Parse(AppInfo.Version));
        if (entries.Count > 0)
            new NewsWindow(entries) { Owner = _window }.Show();
    }
}
