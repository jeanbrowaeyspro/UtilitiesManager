using System.Windows;
using System.Windows.Controls;
using UtilitiesManager.Profiles;

namespace UtilitiesManager;

/// <summary>
/// Définition d'un réglage Windows du profil : nom, valeurs du registre posées et rétablies, redémarrage de
/// l'Explorateur. Enregistrer contrôle le profil modifié : rien n'est enregistré s'il est invalide.
/// </summary>
public sealed class TweakEditorWindow : ThemedWindow
{
    private static readonly string[] Types = ["dword", "string"];

    private readonly Profile _profile;
    private readonly string? _originalId;
    private readonly TextBox _id;
    private readonly TextBox _name;
    private readonly CheckBox _restartExplorer;
    private readonly EditableTable<RegistrySetting> _values = new(260);
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public TweakEditorWindow(Profile profile, TweakEntry tweak, bool isNew)
    {
        _profile = profile;
        _originalId = isNew ? null : tweak.Id;
        Title = $"{AppPaths.AppName} - {(isNew ? L.T("Add a Windows setting") : tweak.Name)}";
        Width = 960;
        Height = 560;
        MinWidth = 620;
        MinHeight = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        _id = new TextBox { Text = tweak.Id, IsEnabled = isNew };
        form.Children.Add(Ui.Row(L.T("Identifier"), _id));
        _name = new TextBox { Text = tweak.Name };
        form.Children.Add(Ui.Row(L.T("Name"), _name));
        _restartExplorer = new CheckBox { Content = L.T("Restart Explorer afterwards"), IsChecked = tweak.RestartExplorer, Margin = new Thickness(160, 0, 0, 10) };
        form.Children.Add(_restartExplorer);

        var heading = Ui.Heading(L.T("Registry values"), 16);
        heading.Margin = new Thickness(0, 8, 0, 8);
        form.Children.Add(heading);
        _values.Text(L.T("Key"), nameof(RegistrySetting.Key), 280);
        _values.Text(L.T("Value name"), nameof(RegistrySetting.Name), 150);
        _values.Choice(L.T("Type"), nameof(RegistrySetting.Type), Types, 80);
        _values.Text(L.T("Value"), nameof(RegistrySetting.Value), 90);
        _values.Text(L.T("Restored value"), nameof(RegistrySetting.Restore), 110);
        _values.Text(L.T("Key deleted to restore"), nameof(RegistrySetting.RestoreDeleteKey));
        _values.Show(tweak.Values);
        form.Children.Add(_values.View);
        form.Children.Add(Ui.Muted(L.T("Empty restored value: the value is deleted (Windows default).")));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = Ui.TextButton(L.T("Save"), Save);
        save.IsDefault = true;
        buttons.Children.Add(save);
        buttons.Children.Add(Ui.TextButton(L.T("Cancel"), Close));
        var bottom = new StackPanel { Margin = new Thickness(20, 0, 20, 16) };
        bottom.Children.Add(_result);
        bottom.Children.Add(buttons);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Tweak = tweak;
    }

    /// <summary>Réglage enregistré (après Enregistrer réussi).</summary>
    public TweakEntry Tweak { get; private set; }

    private void Save()
    {
        var values = _values.Read();
        foreach (var value in values)
        {
            value.Name = value.Name?.Trim() ?? "";
            value.Restore = string.IsNullOrWhiteSpace(value.Restore) ? null : value.Restore.Trim();
            value.RestoreDeleteKey = string.IsNullOrWhiteSpace(value.RestoreDeleteKey) ? null : value.RestoreDeleteKey.Trim();
        }
        var tweak = new TweakEntry
        {
            Id = _id.Text.Trim(),
            Name = _name.Text.Trim(),
            RestartExplorer = _restartExplorer.IsChecked == true,
            Values = values,
        };
        var changed = _profile.Clone();
        var index = changed.Tweaks.FindIndex(t => t.Id == _originalId);
        if (index >= 0)
            changed.Tweaks[index] = tweak;
        else
            changed.Tweaks.Add(tweak);
        var errors = ProfileValidator.Validate(changed);
        if (errors.Count > 0)
        {
            _result.Text = string.Join("\n", errors);
            return;
        }
        Tweak = tweak;
        DialogResult = true;
    }
}
