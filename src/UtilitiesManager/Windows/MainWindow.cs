using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;

namespace UtilitiesManager;

/// <summary>
/// Fenêtre principale : onglets Logiciels, Réglages Windows et Paramètres, barre de progression et journal de
/// l'action en cours. Tout passe par <see cref="AppController"/> ; la fenêtre ne fait qu'afficher.
/// </summary>
public sealed class MainWindow : ThemedWindow
{
    private readonly AppController _controller;
    private readonly DataGrid _programs = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly StackPanel _tweaks = new();
    private readonly StackPanel _settings = new() { Margin = new Thickness(20, 16, 20, 16) };
    private readonly TextBlock _status = Ui.Muted("");
    private readonly ProgressBar _progress = new() { Height = 4, Maximum = 1, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        Height = 110,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        FontSize = 12,
    };
    private readonly List<Button> _actions = [];
    private Button? _revert;
    private Button? _install;
    private Button? _pause;

    public MainWindow(AppController controller)
    {
        _controller = controller;
        Title = AppInfo.Title;
        Width = 1100;
        Height = 720;
        MinWidth = 700;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowPosition.Restore(this, controller.Settings.MainWindow);

        var tabs = new TabControl { Margin = new Thickness(12, 4, 12, 0) };
        tabs.Items.Add(new TabItem { Header = L.T("Software"), Content = BuildProgramsTab() });
        tabs.Items.Add(new TabItem { Header = L.T("Windows settings"), Content = BuildTweaksTab() });
        tabs.Items.Add(new TabItem { Header = L.T("Settings"), Content = new ScrollViewer { Content = _settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        var bottom = new StackPanel { Margin = new Thickness(12, 0, 12, 12) };
        bottom.Children.Add(_progress);
        bottom.Children.Add(_log);
        _status.Margin = new Thickness(16, 6, 16, 4);

        var root = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(menu);
        root.Children.Add(_status);
        root.Children.Add(bottom);
        root.Children.Add(tabs);
        Content = root;

        _log.Text = string.Join(Environment.NewLine, _controller.RecentLog);
        _log.ScrollToEnd();
        _controller.StateChanged += Refresh;
        _controller.LogLine += AppendLog;
        _controller.Progress += ShowProgress;
        _controller.Stepped += ShowStep;
        _controller.Services.Account.Changed += OnAccountChanged;
        Closing += (_, _) => SavePlacement();
        Closed += (_, _) =>
        {
            _controller.StateChanged -= Refresh;
            _controller.LogLine -= AppendLog;
            _controller.Progress -= ShowProgress;
            _controller.Stepped -= ShowStep;
            _controller.Services.Account.Changed -= OnAccountChanged;
        };
        Refresh();
    }

    // ---- Menu ------------------------------------------------------------------------------------------------

    private Menu BuildMenu()
    {
        var menu = new Menu { Margin = new Thickness(4, 2, 4, 0) };
        var view = new MenuItem { Header = L.T("_View") };
        view.Items.Add(Radio(L.T("Theme"), [(ThemeManager.System, L.T("System")), (ThemeManager.Light, L.T("Light")), (ThemeManager.Dark, L.T("Dark"))],
                             _controller.Settings.Theme, _controller.SetTheme));
        view.Items.Add(Radio(L.T("Language"), [(L.System, L.T("System")), (L.French, "Français"), (L.English, "English")],
                             _controller.Settings.Language, language => Dispatcher.BeginInvoke(() => _controller.SetLanguage(language))));
        menu.Items.Add(view);

        var help = new MenuItem { Header = L.T("_Help") };
        help.Items.Add(Item(L.T("Check for updates"), () => _ = _controller.CheckSelfUpdateAsync(manual: true)));
        help.Items.Add(Item(L.T("What's new"), () => new NewsWindow(NewsWindow.Embedded()) { Owner = this }.ShowDialog()));
        help.Items.Add(new Separator());
        help.Items.Add(Item(L.T("About"), () => new AboutWindow(_controller.Services.Paths) { Owner = this }.ShowDialog()));
        menu.Items.Add(help);
        return menu;
    }

    private static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Sous-menu à choix unique (coche sur l'option en cours).</summary>
    private static MenuItem Radio(string header, IReadOnlyList<(string Value, string Text)> options, string current, Action<string> chosen)
    {
        var parent = new MenuItem { Header = header };
        foreach (var (value, text) in options)
        {
            var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = value == current };
            item.Click += (_, _) =>
            {
                foreach (MenuItem other in parent.Items)
                    other.IsChecked = other == item;
                chosen(value);
            };
            parent.Items.Add(item);
        }
        return parent;
    }

    // ---- Logiciels -------------------------------------------------------------------------------------------

    private FrameworkElement BuildProgramsTab()
    {
        _programs.Columns.Add(Ui.CheckColumn(nameof(ProgramRow.Selected)));
        Column(L.T("Software"), nameof(ProgramRow.Name), 220);
        Column(L.T("Source"), nameof(ProgramRow.Source), 100);
        Column(L.T("Installed"), nameof(ProgramRow.Installed), 130);
        Column(L.T("Available"), nameof(ProgramRow.Available), 130);
        Column(L.T("Mode"), nameof(ProgramRow.Mode), 150);
        Column(L.T("State"), nameof(ProgramRow.State), double.NaN);
        _programs.SelectionChanged += (_, _) => UpdateButtons();
        _programs.MouseDoubleClick += (_, _) => _ = EditAsync();

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        // Pendant une série : "Arrêter" à la place, et "Pause" / "Reprendre" à côté.
        _install = Ui.TextButton(L.T("Install / update"), () =>
        {
            if (_controller.Batch is null)
                _ = InstallAsync();
            else
                _controller.StopBatch();
        }, 150);
        _install.Margin = new Thickness(0, 0, 8, 6);
        buttons.Children.Add(_install);
        _pause = Ui.TextButton(L.T("Pause"), _controller.PauseOrResume);
        _pause.Margin = new Thickness(0, 0, 8, 6);
        _pause.Visibility = Visibility.Collapsed;
        buttons.Children.Add(_pause);
        buttons.Children.Add(Action(L.T("Check now"), () => _ = _controller.CheckAsync(manual: true)));
        buttons.Children.Add(Action(L.T("Add…"), () => _ = AddAsync()));
        buttons.Children.Add(Action(L.T("Scan this PC…"), () => _ = ScanAsync(), 130));
        buttons.Children.Add(Action(L.T("Change…"), () => _ = EditAsync()));
        buttons.Children.Add(Action(L.T("Remove from profile"), () => _ = RemoveAsync(), 160));
        _revert = Action(L.T("Revert to previous version"), () => _ = RevertAsync(), 200);
        buttons.Children.Add(_revert);

        var panel = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(_programs);
        return panel;
    }

    /// <summary>Texte des cellules centré en hauteur, aligné sur les cases à cocher.</summary>
    private static readonly Style CenteredText = new(typeof(TextBlock))
    {
        Setters = { new Setter(VerticalAlignmentProperty, VerticalAlignment.Center) },
    };

    private void Column(string header, string property, double width) =>
        _programs.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(property),
            ElementStyle = CenteredText,
            IsReadOnly = true,
            Width = double.IsNaN(width) ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            MinWidth = 80,
        });

    private Button Action(string text, Action click, double minWidth = 110)
    {
        var button = Ui.TextButton(text, click, minWidth);
        button.Margin = new Thickness(0, 0, 8, 6);
        _actions.Add(button);
        return button;
    }

    private ProgramRow? Current => _programs.SelectedItem as ProgramRow;

    private IEnumerable<ProgramRow> Rows => _programs.ItemsSource as IEnumerable<ProgramRow> ?? [];

    private async Task InstallAsync()
    {
        var chosen = Rows.Where(r => r.Selected && r.Status.State is ProgramState.UpdateAvailable or ProgramState.Missing
                                                                      or ProgramState.Removed or ProgramState.Waiting
                                                                      or ProgramState.Dependency)
                         .Select(r => r.Status).ToList();
        if (chosen.Count == 0)
        {
            _controller.Dialogs.Info(L.T("Tick the software to install or update."));
            return;
        }
        await _controller.InstallAsync(chosen);
    }

    private async Task AddAsync()
    {
        if (_controller.Profile is not { } profile)
            return;
        var search = new WingetSearchWindow(_controller.Services.Winget) { Owner = this };
        if (search.ShowDialog() != true)
            return;
        var entry = search.Chosen is { } package ? SourceFinder.FromPackage(package, profile).Entry : new ProgramEntry { Source = Sources.Download };
        await EditEntryAsync(profile, entry, isNew: true);
    }

    /// <summary>
    /// Relevé des logiciels de ce PC absents du profil : les paquets winget exacts sont ajoutés directement, les
    /// autres ouverts un par un dans l'éditeur, prérempli avec la source trouvée.
    /// </summary>
    private async Task ScanAsync()
    {
        if (_controller.Profile is not { } profile)
            return;
        var scan = new ScanWindow(_controller.Services, () => _controller.ScanAsync(profile)) { Owner = this };
        if (scan.ShowDialog() != true)
            return;
        var sure = scan.Chosen.Where(f => f.IsSure).ToList();
        if (sure.Count > 0 && !await _controller.EditProfileAsync(changed =>
            {
                foreach (var found in sure)
                    changed.Programs.Add(SourceFinder.SureCandidate(found, changed)!.Entry);
            }, $"Ajout de {string.Join(", ", sure.Select(f => f.Name))} relevés sur ce PC"))
            return;
        foreach (var found in scan.Chosen.Where(f => !f.IsSure))
        {
            if (_controller.Profile is not { } current
                || await _controller.FindSourcesAsync(found, current) is not { } candidates
                || !await EditEntryAsync(current, candidates[0].Entry, isNew: true, candidates))
                return;
        }
    }

    private async Task EditAsync()
    {
        if (_controller.Profile is not { } profile || Current is not { } row || row.Status.State == ProgramState.Removed)
            return;
        var entry = profile.Clone().Find(row.Status.Program.Id);
        if (entry is not null)
            await EditEntryAsync(profile, entry, isNew: false);
    }

    /// <summary>Éditeur du logiciel puis enregistrement dans le profil ; faux si annulé ou refusé.</summary>
    private async Task<bool> EditEntryAsync(Profile profile, ProgramEntry entry, bool isNew,
                                            IReadOnlyList<SourceCandidate>? candidates = null)
    {
        var editor = new ProgramEditorWindow(_controller.Services, profile, entry, isNew, candidates) { Owner = this };
        if (editor.ShowDialog() != true)
            return false;
        var saved = editor.Entry;
        return await _controller.EditProfileAsync(changed =>
        {
            var index = changed.Programs.FindIndex(p => p.Id == (isNew ? saved.Id : entry.Id));
            if (index >= 0 && !isNew)
                changed.Programs[index] = saved;
            else
                changed.Programs.Add(saved);
        }, isNew ? $"Ajout de {saved.Name}" : $"Modification de {saved.Name}");
    }

    private async Task RemoveAsync()
    {
        if (Current is not { } row || row.Status.State == ProgramState.Removed)
            return;
        var program = row.Status.Program;
        if (!_controller.Dialogs.Confirm(L.T("Remove {0} from the profile?\n\nEach PC will then offer to uninstall it.", program.Name),
                                         L.T("Remove"), L.T("Cancel")))
            return;
        await _controller.EditProfileAsync(changed => changed.Programs.RemoveAll(p => p.Id == program.Id), $"Retrait de {program.Name}");
    }

    private async Task RevertAsync()
    {
        if (Current is not { } row || !_controller.CanRevert(row.Status))
            return;
        if (_controller.Dialogs.Confirm(L.T("Reinstall the previous version of {0}?", row.Name), L.T("Revert"), L.T("Cancel")))
            await _controller.RevertAsync(row.Status);
    }

    // ---- Réglages Windows ------------------------------------------------------------------------------------

    private FrameworkElement BuildTweaksTab()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Action(L.T("Apply"), () => ChangeTweaks(apply: true)));
        buttons.Children.Add(Action(L.T("Restore"), () => ChangeTweaks(apply: false)));
        buttons.Children.Add(Action(L.T("Capture from this PC"), () => _ = _controller.CaptureTweaksAsync()));
        var panel = new DockPanel { Margin = new Thickness(20, 16, 20, 10) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer { Content = _tweaks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return panel;
    }

    private void BuildTweaks()
    {
        _tweaks.Children.Clear();
        var tweaks = _controller.Profile?.Tweaks ?? [];
        if (tweaks.Count == 0)
            _tweaks.Children.Add(Ui.Muted(L.T("The profile contains no Windows setting.")));
        foreach (var tweak in tweaks)
        {
            var row = new TweakRow(tweak, _controller.IsApplied(tweak));
            var box = Ui.Check(row.Text, false, value => row.Selected = value);
            box.Tag = row;
            _tweaks.Children.Add(box);
        }
    }

    private void ChangeTweaks(bool apply)
    {
        var chosen = _tweaks.Children.OfType<CheckBox>().Select(b => b.Tag).OfType<TweakRow>().Where(r => r.Selected).Select(r => r.Tweak).ToList();
        if (chosen.Count == 0)
        {
            _controller.Dialogs.Info(L.T("Tick the settings to change."));
            return;
        }
        _controller.ChangeTweaks(chosen, apply);
    }

    // ---- Paramètres ------------------------------------------------------------------------------------------

    /// <summary>Connexion ou déconnexion, signalée depuis n'importe quel thread : page refaite sur celui de la fenêtre.</summary>
    private void OnAccountChanged() => Dispatcher.BeginInvoke(BuildSettings);

    private void BuildSettings()
    {
        var settings = _controller.Settings;
        var account = _controller.Services.Account;
        _settings.Children.Clear();

        _settings.Children.Add(Section(L.T("Profile")));
        var github = new StackPanel { Orientation = Orientation.Horizontal };
        github.Children.Add(new TextBlock
        {
            Text = account.IsSignedIn ? L.T("Signed in as {0}", account.Login) : L.T("Not signed in"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        github.Children.Add(account.IsSignedIn
            ? Ui.TextButton(L.T("Sign out"), account.SignOut)
            : Ui.TextButton(L.T("Sign in"), SignIn));
        _settings.Children.Add(Ui.Row(L.T("GitHub account"), github));
        if (account.IsSignedIn && _controller.ProfileRepositoryMissing)
            _settings.Children.Add(Ui.Row(L.T("Profile repository"), Ui.TextButton(L.T("Create the repository"), () => _ = _controller.ConnectProfileAsync())));
        _settings.Children.Add(Ui.Row(L.T("Profile file"), Ui.TextButton(L.T("Import…"), () => _ = ImportAsync())));
        if (_controller.Profile is { } profile)
        {
            var minAge = Ui.Choices(Texts.MinAgeChoices(profile.Defaults.MinAgeDays), profile.Defaults.MinAgeDays,
                                    days => _ = _controller.EditProfileAsync(p => p.Defaults.MinAgeDays = days, "Âge minimum par défaut"));
            _settings.Children.Add(Ui.Row(L.T("Default minimum age"), minAge));
        }

        _settings.Children.Add(Section(L.T("This PC")));
        _settings.Children.Add(Ui.Check(L.T("Start with Windows and check at each session"), settings.RunAtStartup, v => Save(() => settings.RunAtStartup = v)));
        _settings.Children.Add(Ui.Check(L.T("Show the notification area icon"), settings.TrayIcon, v => Save(() => settings.TrayIcon = v)));
        _settings.Children.Add(Ui.Check(L.T("Desktop shortcut"), settings.DesktopShortcut, v => Save(() => settings.DesktopShortcut = v)));
        _settings.Children.Add(Ui.Check(L.T("Start menu shortcut"), settings.StartMenuShortcut, v => Save(() => settings.StartMenuShortcut = v)));
        _settings.Children.Add(Ui.Row(L.T("Portable software folder"), Ui.TextButton(settings.PortableFolder, ChoosePortableFolder)));

        _settings.Children.Add(Section(L.T("UtilitiesManager updates")));
        _settings.Children.Add(Ui.Check(L.T("Show the release notes"), settings.ShowReleaseNotes, v => Save(() => settings.ShowReleaseNotes = v)));
        _settings.Children.Add(Ui.Check(L.T("Offer test versions (prereleases)"), settings.IncludePrereleases, v => Save(() => settings.IncludePrereleases = v)));

        _settings.Children.Add(Section(L.T("Display")));
        _settings.Children.Add(Ui.Row(L.T("Theme"), Ui.Choices([(ThemeManager.System, L.T("System")), (ThemeManager.Light, L.T("Light")), (ThemeManager.Dark, L.T("Dark"))],
                                                               settings.Theme, _controller.SetTheme)));
        _settings.Children.Add(Ui.Row(L.T("Language"), Ui.Choices([(L.System, L.T("System")), (L.French, "Français"), (L.English, "English")],
                                                                  settings.Language, language => Dispatcher.BeginInvoke(() => _controller.SetLanguage(language)))));
    }

    /// <summary>Dossier des logiciels portables parcouru par le relevé.</summary>
    private void ChoosePortableFolder()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Portable software folder"), InitialDirectory = _controller.Settings.PortableFolder };
        if (dialog.ShowDialog(this) != true)
            return;
        Save(() => _controller.Settings.PortableFolder = dialog.FolderName);
        BuildSettings();
    }

    private static TextBlock Section(string title)
    {
        var heading = Ui.Heading(title, 16);
        heading.Margin = new Thickness(0, 8, 0, 10);
        return heading;
    }

    private void SavePlacement()
    {
        if (WindowPosition.Read(this) is { } placement)
            Save(() => _controller.Settings.MainWindow = placement);
    }

    private void Save(Action change)
    {
        change();
        _controller.SaveSettings();
    }

    private void SignIn()
    {
        if (!_controller.Services.Account.IsConfigured)
        {
            _controller.Dialogs.Error(L.T("GitHub sign-in is not available in this version.\n\nImport the profile file instead."));
            return;
        }
        if (new SignInWindow(_controller.Services.Account) { Owner = this }.ShowDialog() == true)
            _ = _controller.ConnectProfileAsync();
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = L.T("Profile (*.json)|*.json"), Title = L.T("Import a profile") };
        if (dialog.ShowDialog(this) == true)
            await _controller.ImportProfileAsync(dialog.FileName);
    }

    // ---- Affichage de l'état ---------------------------------------------------------------------------------

    private void Refresh()
    {
        var profile = _controller.Profile;
        var last = _controller.Last;
        var selected = Current?.Status.Program.Id;
        var rows = profile is not null && last is not null
            ? last.Programs.Select(s => new ProgramRow(s, profile.Defaults, _controller.StepOf(s.Program.Id))).ToList()
            : [];
        _programs.ItemsSource = rows;
        _programs.SelectedItem = rows.FirstOrDefault(r => r.Status.Program.Id == selected);
        var pending = _controller.PendingCount;
        _status.Text = _controller.ProfileOrigin + (last is null ? "" : "   " + (pending == 0
            ? L.T("Everything is up to date.")
            : L.T("{0} action(s) waiting", pending)));
        BuildTweaks();
        BuildSettings();
        UpdateButtons();
        if (!_controller.Busy)
            ShowProgress(null);
    }

    /// <summary>Met à jour la seule ligne du logiciel (sélection et défilement conservés).</summary>
    private void ShowStep(BatchStep step)
    {
        if (Rows.FirstOrDefault(r => r.Status.Program.Id == step.Id) is { } row)
            row.Step = step;
    }

    private void UpdateButtons()
    {
        foreach (var button in _actions)
            button.IsEnabled = !_controller.Busy;
        var batch = _controller.Batch;
        if (_install is not null && _pause is not null)
        {
            _install.Content = batch is null ? L.T("Install / update") : L.T("Stop");
            _install.IsEnabled = batch is null ? !_controller.Busy : !batch.Stopped;
            _pause.Visibility = batch is null ? Visibility.Collapsed : Visibility.Visible;
            _pause.Content = batch is { Paused: true } ? L.T("Resume") : L.T("Pause");
            _pause.IsEnabled = batch is { Stopped: false };
        }
        if (_revert is not null)
            _revert.IsEnabled = !_controller.Busy && Current is { } row && _controller.CanRevert(row.Status);
    }

    private void AppendLog(string line)
    {
        _log.AppendText((_log.Text.Length == 0 ? "" : Environment.NewLine) + line);
        _log.ScrollToEnd();
    }

    private void ShowProgress(double? fraction)
    {
        _progress.IsIndeterminate = _controller.Busy && fraction is null;
        _progress.Value = fraction ?? 0;
    }
}
