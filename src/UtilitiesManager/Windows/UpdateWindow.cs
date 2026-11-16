using System.Windows;
using System.Windows.Controls;
using UtilitiesManager.Updates;

namespace UtilitiesManager;

public enum UpdateChoice
{
    Later,
    Install,
    Skip,
}

/// <summary>
/// Proposition d'une nouvelle version de UtilitiesManager : version installée, nouveautés de la release, boutons
/// Installer maintenant / Plus tard / Ignorer cette version. Le téléchargement se fait dans la fenêtre (barre de
/// progression) ; une erreur y est affichée sans la fermer.
/// </summary>
public sealed class UpdateWindow : ThemedWindow
{
    private readonly Func<IProgress<double>, Task> _install;
    private readonly ProgressBar _progress = new() { Height = 4, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, Maximum = 1 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
    private bool _running;

    public UpdateWindow(AvailableUpdate update, bool showNotes, Func<IProgress<double>, Task> install)
    {
        _install = install;
        Title = L.T("UtilitiesManager {0} is available", update.Version);
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        content.Children.Add(Ui.Heading(Title));
        content.Children.Add(Ui.Muted(L.T("Installed version: {0}", AppInfo.Version)));
        if (showNotes && !string.IsNullOrWhiteSpace(update.Notes))
        {
            content.Children.Add(new TextBox
            {
                Text = update.Notes.Trim(),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 300,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }
        content.Children.Add(_progress);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        content.Children.Add(_error);

        var installButton = Ui.TextButton(L.T("Install now"), () => _ = InstallAsync());
        installButton.IsDefault = true;
        _buttons.Children.Add(installButton);
        _buttons.Children.Add(Ui.TextButton(L.T("Later"), () => Finish(UpdateChoice.Later)));
        _buttons.Children.Add(Ui.TextButton(L.T("Skip this version"), () => Finish(UpdateChoice.Skip)));
        content.Children.Add(_buttons);
        Content = content;
        Closing += (_, e) => e.Cancel = _running;
    }

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    private void Finish(UpdateChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }

    private async Task InstallAsync()
    {
        _running = true;
        _buttons.IsEnabled = false;
        _error.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        try
        {
            await _install(new Progress<double>(value =>
            {
                _progress.IsIndeterminate = false;
                _progress.Value = value;
            }));
            _running = false;
            Finish(UpdateChoice.Install);
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            Log.Error($"Mise à jour : {exc.Message}");
            _running = false;
            _buttons.IsEnabled = true;
            _progress.Visibility = Visibility.Collapsed;
            _error.Text = L.T("The update failed, nothing was changed:\n{0}", exc.Message);
            _error.Visibility = Visibility.Visible;
        }
    }
}
