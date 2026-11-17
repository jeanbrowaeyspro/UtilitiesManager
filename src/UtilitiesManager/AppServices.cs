using System.Windows;
using UtilitiesManager.GitHub;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;
using UtilitiesManager.Updates;
using UtilitiesManager.ViewModels;

namespace UtilitiesManager;

/// <summary>Services réels de l'application, assemblés une fois au démarrage.</summary>
public sealed class AppServices
{
    public AppServices(AppPaths paths, AppSettings settings, IDialogs dialogs)
    {
        Paths = paths;
        Settings = settings;
        Dialogs = dialogs;
        var registry = new WindowsRegistry();
        UserAgent = $"{AppPaths.AppName}/{AppInfo.Version}";
        Account = new GitHubAccount(new GitHubCredentialStore(paths.CredentialFile), new GitHubSignIn(UserAgent),
                                    (repository, token) => new GitHubRepositoryClient(repository, token, UserAgent));
        Func<string, GitHubRepositoryClient> github = Account.CreateClient;
        Profiles = new ProfileStore(paths, () => Account.Login, github);
        State = MachineState.Load(paths.StateFile);
        var shell = new ShellCommands(Runner);
        Winget = new Winget(Runner);
        Detector = new Detector(registry, shell);
        Latest = new LatestResolver(shell, github);
        Checker = new CheckEngine(Winget, Detector, Latest, shell);
        Installer = new InstallService(Winget, Detector, Latest, shell, new Integrity(new AuthenticodeVerifier()), Runner,
                                       new AppProcesses(Runner), new VersionCache(paths.CacheDir), Profiles, github, State);
        Tweaks = new WindowsTweaks(registry, command => shell.RunElevatedAsync(command));
        Inventory = new Inventory(registry);
        SourceFinder = new SourceFinder(Winget, new GitHubSearchClient(Account.TokenAsync, UserAgent), github);
        Setup = new Setup(paths, registry, new ShellLinkWriter());
        SelfUpdate = new SelfUpdate(paths.InstalledLauncher);
    }

    public AppPaths Paths { get; }
    public AppSettings Settings { get; }
    public IDialogs Dialogs { get; }
    public string UserAgent { get; }
    public ProcessRunner Runner { get; } = new();
    public GitHubAccount Account { get; }
    public ProfileStore Profiles { get; }
    public MachineState State { get; }
    public Winget Winget { get; }
    public Detector Detector { get; }
    public LatestResolver Latest { get; }
    public CheckEngine Checker { get; }
    public InstallService Installer { get; }
    public WindowsTweaks Tweaks { get; }
    public Inventory Inventory { get; }
    public SourceFinder SourceFinder { get; }
    public Setup Setup { get; }
    public SelfUpdate SelfUpdate { get; }

    /// <summary>Client anonyme du dépôt public de l'outil (recherche de sa propre mise à jour).</summary>
    public UpdateService CreateUpdateService() =>
        new(new GitHubRepositoryClient(UpdateService.Repository, string.Empty, UserAgent));

    public void SaveSettings() => Settings.Save(Paths.SettingsFile);

    public void SaveState() => State.Save(Paths.StateFile);
}

/// <summary>
/// Messages affichés dans une <see cref="DialogWindow"/> modale, centrée sur la fenêtre
/// principale si elle est visible, sinon à l'écran.
/// </summary>
public sealed class WpfDialogs(Func<Window?> owner) : IDialogs
{
    /// <summary>Affiche la boîte et renvoie le rang du bouton choisi (null : fermée autrement).</summary>
    private int? Show(DialogKind kind, string text, params string[] buttons)
    {
        var viewModel = new DialogViewModel(kind, text, buttons);
        DialogWindow.Create(viewModel, owner()).ShowDialog();
        return viewModel.Choice;
    }

    public bool Confirm(string text, string? yes = null, string? no = null) =>
        Show(DialogKind.Question, text, yes ?? L.T("Yes"), no ?? L.T("No")) == 0;

    public void Info(string text) => Show(DialogKind.Info, text, L.T("OK"));

    public void Error(string text) => Show(DialogKind.Error, text, L.T("OK"));
}
