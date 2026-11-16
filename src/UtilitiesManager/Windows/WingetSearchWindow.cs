using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UtilitiesManager.Software;

namespace UtilitiesManager;

/// <summary>
/// Ajout d'un logiciel : recherche winget par nom ou identifiant. "Autre source" ouvre le formulaire vide.
/// </summary>
public sealed class WingetSearchWindow : ThemedWindow
{
    private readonly Winget _winget;
    private readonly TextBox _query = new() { MinWidth = 300 };
    private readonly DataGrid _results = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        Margin = new Thickness(0, 12, 0, 0),
    };
    private readonly TextBlock _status = Ui.Muted("");
    private readonly Button _add;
    private CancellationTokenSource? _search;

    public WingetSearchWindow(Winget winget)
    {
        _winget = winget;
        Title = $"{AppPaths.AppName} - {L.T("Add software")}";
        Width = 760;
        Height = 520;
        MinWidth = 500;
        MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _results.Columns.Add(new DataGridTextColumn { Header = L.T("Name"), Binding = new Binding(nameof(WingetPackage.Name)), Width = 260 });
        _results.Columns.Add(new DataGridTextColumn { Header = L.T("Identifier"), Binding = new Binding(nameof(WingetPackage.Id)), Width = 240 });
        _results.Columns.Add(new DataGridTextColumn { Header = L.T("Version"), Binding = new Binding(nameof(WingetPackage.Version)), Width = 110 });
        _results.Columns.Add(new DataGridTextColumn { Header = L.T("Source"), Binding = new Binding(nameof(WingetPackage.Source)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _results.MouseDoubleClick += (_, _) => Choose();
        _results.SelectionChanged += (_, _) => _add!.IsEnabled = _results.SelectedItem is not null;

        var search = Ui.TextButton(L.T("Search"), () => _ = SearchAsync());
        search.IsDefault = true;
        var top = new DockPanel();
        DockPanel.SetDock(search, Dock.Right);
        search.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(search);
        top.Children.Add(_query);

        _add = Ui.TextButton(L.T("Add"), Choose);
        _add.IsEnabled = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_add);
        buttons.Children.Add(Ui.TextButton(L.T("Other source…"), () =>
        {
            OtherSource = true;
            DialogResult = true;
        }, 140));
        buttons.Children.Add(Ui.TextButton(L.T("Cancel"), Close));

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        _status.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(top);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        root.Children.Add(_results);
        Content = root;
        Loaded += (_, _) => _query.Focus();
        Closed += (_, _) => _search?.Cancel();
    }

    /// <summary>Paquet choisi (null si "Autre source" ou annulé).</summary>
    public WingetPackage? Chosen { get; private set; }

    public bool OtherSource { get; private set; }

    private void Choose()
    {
        if (_results.SelectedItem is not WingetPackage package)
            return;
        Chosen = package;
        DialogResult = true;
    }

    private async Task SearchAsync()
    {
        var query = _query.Text.Trim();
        if (query.Length == 0)
            return;
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var cancel = _search.Token;
        _status.Text = L.T("Searching…");
        try
        {
            var found = await _winget.SearchAsync(query, cancel);
            _results.ItemsSource = found;
            _status.Text = found.Count == 0 ? L.T("Nothing found.") : L.T("{0} result(s).", found.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            _status.Text = L.T("Search failed: {0}", exc.Message);
        }
    }
}
