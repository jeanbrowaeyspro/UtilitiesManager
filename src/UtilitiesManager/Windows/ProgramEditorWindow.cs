using System.Windows;
using System.Windows.Controls;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;
using S = UtilitiesManager.Profiles.Sources;

namespace UtilitiesManager;

/// <summary>
/// Définition d'un logiciel du profil : options (mode, âge minimum, version bloquée), champs de sa source, seuls
/// ceux de la source choisie étant affichés. Enregistrer contrôle le profil et teste la détection et la dernière
/// version : rien n'est enregistré si le test échoue.
/// </summary>
public sealed class ProgramEditorWindow : ThemedWindow
{
    private static readonly string[] All = S.All;
    private static readonly string[] Downloaded = [S.Chocolatey, S.GitHub, S.Zip, S.Download];
    private static readonly string[] Detected = [S.Chocolatey, S.GitHub, S.Zip, S.Download, S.Command];

    /// <summary>Champ du formulaire : contrôle, sources où il s'affiche, lecture et écriture dans l'entrée.</summary>
    private sealed record Field(FrameworkElement Row, string[] Sources, Action<ProgramEntry> Load, Action<ProgramEntry> Store);

    private readonly AppServices _services;
    private readonly Profile _profile;
    private readonly string? _originalId;
    private readonly List<Field> _fields = [];
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
    private string _source;

    /// <param name="candidates">Sources proposées par le relevé : la liste "Source proposée" recharge le formulaire.</param>
    public ProgramEditorWindow(AppServices services, Profile profile, ProgramEntry entry, bool isNew,
                               IReadOnlyList<SourceCandidate>? candidates = null)
    {
        _services = services;
        _profile = profile;
        _originalId = isNew ? null : entry.Id;
        _source = entry.Source;
        Title = $"{AppPaths.AppName} - {(isNew ? L.T("Add software") : entry.Name)}";
        Width = 720;
        Height = 720;
        MinWidth = 520;
        MinHeight = 400;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        if (candidates is { Count: > 1 })
            form.Children.Add(Ui.Row(L.T("Proposed source"), Ui.Choices(candidates.Select(c => (c, c.Label)).ToList(), candidates[0], c => Load(c.Entry))));
        Section(form, L.T("Software"));
        Text(form, L.T("Identifier"), All, e => e.Id, (e, v) => e.Id = v ?? "", enabled: isNew);
        Text(form, L.T("Name"), All, e => e.Name, (e, v) => e.Name = v ?? "");
        var source = Ui.Choices(S.All.Select(s => (s, s)).ToList(), entry.Source, value =>
        {
            _source = value;
            ShowFields();
        });
        AddRow(form, L.T("Source"), source, All,
               e => source.SelectedItem = source.Items.OfType<ComboBoxItem>().First(i => Equals(i.Tag, e.Source)), e => e.Source = _source);

        Section(form, L.T("Options"));
        var mode = Ui.Choices([(null, L.T("Profile default ({0})", Texts.Mode(profile.Defaults.Mode))),
                               .. Texts.ModeChoices.Select(c => ((string?)c.Value, c.Text))], entry.Mode, _ => { });
        AddRow(form, L.T("Mode"), mode, All, _ => { }, e => e.Mode = (mode.SelectedItem as ComboBoxItem)?.Tag as string);
        var minAge = Ui.Choices([(null, L.T("Profile default ({0})", Texts.MinAge(profile.Defaults.MinAgeDays))),
                                 .. Texts.MinAgeChoices(entry.MinAgeDays ?? 0).Select(c => ((int?)c.Value, c.Text))], entry.MinAgeDays, _ => { });
        AddRow(form, L.T("Minimum age"), minAge, All, _ => { }, e => e.MinAgeDays = (minAge.SelectedItem as ComboBoxItem)?.Tag as int?);
        Text(form, L.T("Blocked version"), All, e => e.BlockedVersion, (e, v) => e.BlockedVersion = v);
        Text(form, L.T("Processes to close (comma)"), All, e => e.Close is null ? null : string.Join(", ", e.Close),
             (e, v) => e.Close = v?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());
        Text(form, L.T("Silent arguments"), [S.Winget, S.Chocolatey, S.GitHub, S.Download], e => e.SilentArgs, (e, v) => e.SilentArgs = v);
        Flag(form, L.T("Interactive installer"), [S.Winget, S.Chocolatey, S.GitHub, S.Download], e => e.Interactive, (e, v) => e.Interactive = v);
        Flag(form, L.T("Run as administrator"), Detected, e => e.Elevate, (e, v) => e.Elevate = v);
        Text(form, L.T("Depends on (identifier)"), All, e => e.DependsOn, (e, v) => e.DependsOn = v);

        Section(form, L.T("Source"));
        Text(form, L.T("Package"), [S.Winget, S.MsStore, S.Chocolatey, S.GitHub], e => e.Package, (e, v) => e.Package = v);
        Text(form, L.T("Download address"), [S.Zip, S.Download], e => e.Url, (e, v) => e.Url = v);
        Text(form, L.T("Known version"), [S.Zip, S.Download], e => e.Version, (e, v) => e.Version = v);
        Text(form, L.T("Release file pattern"), [S.GitHub], e => e.AssetPattern, (e, v) => e.AssetPattern = v);
        Flag(form, L.T("Accept prereleases"), [S.GitHub], e => e.Prerelease, (e, v) => e.Prerelease = v);
        Text(form, L.T("Installer path pattern"), [S.Chocolatey], e => e.InstallerPattern, (e, v) => e.InstallerPattern = v);
        Text(form, L.T("Install folder"), [S.GitHub, S.Zip, S.Download], e => e.InstallDir, (e, v) => e.InstallDir = v);
        Text(form, L.T("Install command"), [S.Command], e => e.InstallCommand, (e, v) => e.InstallCommand = v);
        Text(form, L.T("Update command"), [S.Command], e => e.UpdateCommand, (e, v) => e.UpdateCommand = v);
        Text(form, L.T("Uninstall command"), Detected, e => e.UninstallCommand, (e, v) => e.UninstallCommand = v);

        Section(form, L.T("Verification"), Downloaded);
        var verify = Ui.Choices(Verifications.All.Select(v => (v, v)).ToList(), entry.VerifyOrDefault, _ => { });
        AddRow(form, L.T("Verification"), verify, Downloaded, _ => { },
               e => e.Verify = (verify.SelectedItem as ComboBoxItem)?.Tag is string v && v != Verifications.Signature ? v : null);
        Text(form, L.T("Expected signer"), Downloaded, e => e.Signer, (e, v) => e.Signer = v);
        Text(form, L.T("SHA-256"), Downloaded, e => e.Sha256, (e, v) => e.Sha256 = v);
        Text(form, L.T("Checksum file address"), Downloaded, e => e.ChecksumUrl, (e, v) => e.ChecksumUrl = v);

        Section(form, L.T("Detection"), [.. Detected, S.MsStore]);
        Text(form, L.T("Name in installed programs (pattern)"), Detected, e => e.Detect?.DisplayName, (e, v) => Detect(e).DisplayName = v);
        Text(form, L.T("File present when installed"), Detected, e => e.Detect?.File, (e, v) => Detect(e).File = v);
        Text(form, L.T("winget identifier (pattern)"), [.. Detected, S.MsStore], e => e.Detect?.WingetId, (e, v) => Detect(e).WingetId = v);
        Text(form, L.T("Version command"), Detected, e => e.Detect?.Command, (e, v) => Detect(e).Command = v);
        Text(form, L.T("Version pattern"), Detected, e => e.Detect?.Regex, (e, v) => Detect(e).Regex = v);

        Section(form, L.T("Latest version"), [S.Zip, S.Download, S.Command]);
        Text(form, L.T("Page to read"), [S.Zip, S.Download, S.Command], e => e.Latest?.Url, (e, v) => Latest(e).Url = v);
        Text(form, L.T("Command"), [S.Zip, S.Download, S.Command], e => e.Latest?.Command, (e, v) => Latest(e).Command = v);
        Text(form, L.T("Version pattern"), [S.Zip, S.Download, S.Command], e => e.Latest?.Regex, (e, v) => Latest(e).Regex = v);

        Section(form, L.T(".ini settings after installation"));
        var ini = new EditableTable<IniSetting>();
        ini.Text(L.T("File"), nameof(IniSetting.File), 220);
        ini.Text(L.T("Section"), nameof(IniSetting.Section), 120);
        ini.Text(L.T("Key"), nameof(IniSetting.Key), 120);
        ini.Text(L.T("Value"), nameof(IniSetting.Value));
        form.Children.Add(ini.View);
        _fields.Add(new Field(ini.View, All, e => ini.Show(e.Ini), e => e.Ini = ini.Read() is { Count: > 0 } rows ? rows : null));

        Entry = entry;
        Load(entry);

        _buttons.Children.Add(Ui.TextButton(L.T("Test"), () => _ = TestAsync(save: false)));
        var save = Ui.TextButton(L.T("Save"), () => _ = TestAsync(save: true));
        save.IsDefault = true;
        _buttons.Children.Add(save);
        _buttons.Children.Add(Ui.TextButton(L.T("Cancel"), Close));
        var bottom = new StackPanel { Margin = new Thickness(20, 0, 20, 16) };
        bottom.Children.Add(_result);
        bottom.Children.Add(_buttons);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    /// <summary>Entrée enregistrée (après Enregistrer réussi).</summary>
    public ProgramEntry Entry { get; private set; }

    private static Detection Detect(ProgramEntry entry) => entry.Detect ??= new Detection();

    private static LatestQuery Latest(ProgramEntry entry) => entry.Latest ??= new LatestQuery();

    private void Section(Panel form, string title, string[]? sources = null)
    {
        var heading = Ui.Heading(title, 16);
        heading.Margin = new Thickness(0, form.Children.Count == 0 ? 0 : 14, 0, 8);
        form.Children.Add(heading);
        if (sources is not null)
            _fields.Add(new Field(heading, sources, _ => { }, _ => { }));
    }

    private void AddRow(Panel form, string label, FrameworkElement control, string[] sources, Action<ProgramEntry> load, Action<ProgramEntry> store)
    {
        var row = Ui.Row(label, control);
        form.Children.Add(row);
        _fields.Add(new Field(row, sources, load, store));
    }

    private void Text(Panel form, string label, string[] sources, Func<ProgramEntry, string?> get, Action<ProgramEntry, string?> set, bool enabled = true)
    {
        var box = new TextBox { IsEnabled = enabled };
        AddRow(form, label, box, sources, e => box.Text = get(e) ?? "",
               e => set(e, string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim()));
    }

    private void Flag(Panel form, string label, string[] sources, Func<ProgramEntry, bool> get, Action<ProgramEntry, bool> set)
    {
        var box = new CheckBox { Content = label, Margin = new Thickness(160, 0, 0, 10) };
        form.Children.Add(box);
        _fields.Add(new Field(box, sources, e => box.IsChecked = get(e), e => set(e, box.IsChecked == true)));
    }

    /// <summary>Remplit le formulaire avec l'entrée.</summary>
    private void Load(ProgramEntry entry)
    {
        _source = entry.Source;
        foreach (var field in _fields)
            field.Load(entry);
        ShowFields();
    }

    /// <summary>Seuls les champs de la source choisie sont affichés.</summary>
    private void ShowFields()
    {
        foreach (var field in _fields)
            field.Row.Visibility = field.Sources.Contains(_source) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Entrée lue du formulaire ; les champs masqués (autre source) ne sont pas gardés.</summary>
    private ProgramEntry Read()
    {
        var entry = new ProgramEntry();
        foreach (var field in _fields.Where(f => f.Sources.Contains(_source)))
            field.Store(entry);
        entry.Source = _source;
        if (entry.Detect is { DisplayName: null, File: null, WingetId: null, Command: null, Regex: null })
            entry.Detect = null;
        if (entry.Latest is { Url: null, Command: null, Regex: null })
            entry.Latest = null;
        return entry;
    }

    /// <summary>Contrôle du profil modifié, détection et dernière version ; enregistre si demandé et réussi.</summary>
    private async Task TestAsync(bool save)
    {
        var entry = Read();
        var changed = _profile.Clone();
        var index = changed.Programs.FindIndex(p => p.Id == (_originalId ?? entry.Id));
        if (_originalId is not null && index >= 0)
            changed.Programs[index] = entry;
        else
            changed.Programs.Add(entry);
        var errors = ProfileValidator.Validate(changed);
        if (errors.Count > 0)
        {
            _result.Text = string.Join("\n", errors);
            return;
        }
        _buttons.IsEnabled = false;
        _result.Text = L.T("Testing…");
        try
        {
            IReadOnlyList<WingetPackage> packages = entry.IsWinget || entry.Detect?.WingetId is not null
                ? await _services.Winget.ListAsync()
                : [];
            var installed = await _services.Detector.DetectAsync(entry, packages, _services.State);
            var latest = await _services.Latest.ResolveAsync(entry, packages, installed);
            _result.Text = L.T("Installed: {0}\nLatest version: {1}",
                               installed.Present ? installed.Version ?? L.T("unknown") : L.T("not installed"),
                               latest?.Version ?? L.T("unknown"));
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            _result.Text = L.T("Test failed, nothing was saved: {0}", exc.Message);
            _buttons.IsEnabled = true;
            return;
        }
        _buttons.IsEnabled = true;
        if (!save)
            return;
        Entry = entry;
        DialogResult = true;
    }
}
