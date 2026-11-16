using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using UtilitiesManager.ViewModels;

namespace UtilitiesManager;

/// <summary>
/// Seule fenêtre de message de l'application (questions, informations, erreurs,
/// proposition de mise à jour, attente d'une opération et message de fin) :
/// thème Fluent de l'application, clair ou sombre, comme la fenêtre de réglages, y
/// compris quand le thème de Windows change.
/// </summary>
public partial class DialogWindow : Window
{
    private readonly DialogViewModel _viewModel;

    /// <summary>Premier plan forcé hors attente (fenêtre sans propriétaire visible).</summary>
    private bool _foreground;

    public DialogWindow(DialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        ShowButtons();
        ShowState();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DialogViewModel.Buttons))
                ShowButtons();
            else if (e.PropertyName is nameof(DialogViewModel.Kind) or nameof(DialogViewModel.IsWaiting))
                ShowState();
        };
        viewModel.RequestClose += Close;
    }

    /// <summary>Un bouton par <see cref="DialogButton"/> du modèle de vue.</summary>
    private void ShowButtons()
    {
        ButtonsPanel.Children.Clear();
        foreach (var model in _viewModel.Buttons)
        {
            var button = new Button
            {
                Content = model.Text,
                Command = model.Command,
                IsDefault = model.IsDefault,
                IsCancel = model.IsCancel,
                MinWidth = 100,
                Margin = new Thickness(8, 0, 0, 0),
            };
            if (model.IsDefault)
                button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            ButtonsPanel.Children.Add(button);
        }
    }

    /// <summary>
    /// Symbole de la nature du message ; en attente, barre de progression à la place des
    /// boutons et plus de premier plan forcé (l'UAC ne doit pas être masquée). En sortie
    /// d'attente (message de fin), la fenêtre est ramenée devant.
    /// </summary>
    private void ShowState()
    {
        Symbol.Text = Glyph(_viewModel.Kind);
        Symbol.SetResourceReference(TextBlock.ForegroundProperty,
                                    _viewModel.Kind == DialogKind.Error ? "SystemFillColorCriticalBrush" : "AccentTextFillColorPrimaryBrush");
        Progress.Visibility = _viewModel.IsWaiting ? Visibility.Visible : Visibility.Collapsed;
        ButtonsPanel.Visibility = _viewModel.IsWaiting ? Visibility.Collapsed : Visibility.Visible;
        Topmost = _foreground && !_viewModel.IsWaiting;
        if (!_viewModel.IsWaiting && IsVisible)
            Activate();
    }

    /// <summary>
    /// Fenêtre centrée sur <paramref name="owner"/> s'il est visible ; sinon centrée à
    /// l'écran, au premier plan (hors attente) et présente dans la barre des tâches.
    /// </summary>
    public static DialogWindow Create(DialogViewModel viewModel, Window? owner)
    {
        var window = new DialogWindow(viewModel);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowInTaskbar = false;
        }
        else
        {
            window._foreground = true;
            window.Topmost = !viewModel.IsWaiting;
        }
        return window;
    }

    /// <summary>Symbole Segoe Fluent Icons de chaque nature de message.</summary>
    public static string Glyph(DialogKind kind) => kind switch
    {
        DialogKind.Error => "",
        DialogKind.Info => "",
        _ => "",
    };

    /// <summary>Pas de fermeture pendant une opération (installation en cours).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_viewModel.CanClose;
        base.OnClosing(e);
    }
}
