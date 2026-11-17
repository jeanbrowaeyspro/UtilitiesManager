using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UtilitiesManager.Software;

namespace UtilitiesManager;

/// <summary>
/// Relevé des logiciels de ce PC absents du profil : cocher ceux à ajouter, ou ceux à ne plus proposer sur ce poste.
/// </summary>
public sealed class ScanWindow : ThemedWindow
{
    private readonly AppServices _services;
    private readonly TextBox _filter = new() { MinWidth = 260 };
    private readonly CheckBox _showIgnored = new() { Content = L.T("Show the software not proposed"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Extended,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 12, 0, 0),
    };
    private readonly TextBlock _status = Ui.Muted(L.T("Reading the software of this PC…"));
    private readonly List<Button> _actions = [];
    private List<FoundRow> _rows = [];

    public ScanWindow(AppServices services, Func<Task<IReadOnlyList<FoundSoftware>>> scan)
    {
        _services = services;
        Title = $"{AppPaths.AppName} - {L.T("Software of this PC")}";
        Width = 900;
        Height = 600;
        MinWidth = 600;
        MinHeight = 320;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _grid.Columns.Add(Ui.CheckColumn(nameof(FoundRow.Selected)));
        Column(L.T("Software"), nameof(FoundRow.Name), 260);
        Column(L.T("Version"), nameof(FoundRow.Version), 120);
        Column(L.T("Publisher"), nameof(FoundRow.Publisher), 170);
        Column(L.T("Found in"), nameof(FoundRow.Origin), 120);
        Column(L.T("Source"), nameof(FoundRow.Source), double.NaN);
        _filter.TextChanged += (_, _) => Refresh();
        _showIgnored.Click += (_, _) => Refresh();

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = L.T("Filter"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        top.Children.Add(_filter);
        top.Children.Add(_showIgnored);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Action(L.T("Add to profile"), Add, 140));
        buttons.Children.Add(Action(L.T("Do not propose again"), () => Ignore(true), 170));
        buttons.Children.Add(Action(L.T("Propose again"), () => Ignore(false), 140));
        buttons.Children.Add(Ui.TextButton(L.T("Cancel"), Close));

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        _status.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(top);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        root.Children.Add(_grid);
        Content = root;
        Loaded += async (_, _) => await LoadAsync(scan);
    }

    /// <summary>Logiciels cochés à ajouter au profil (après "Ajouter au profil").</summary>
    public IReadOnlyList<FoundSoftware> Chosen { get; private set; } = [];

    private void Column(string header, string property, double width) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(property),
            IsReadOnly = true,
            Width = double.IsNaN(width) ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            MinWidth = 80,
        });

    private Button Action(string text, Action click, double minWidth)
    {
        var button = Ui.TextButton(text, click, minWidth);
        button.IsEnabled = false;
        _actions.Add(button);
        return button;
    }

    private async Task LoadAsync(Func<Task<IReadOnlyList<FoundSoftware>>> scan)
    {
        try
        {
            var found = await scan();
            _rows = found.Select(f => new FoundRow(f, _services.Settings.IsIgnored(f))).ToList();
            _actions.ForEach(b => b.IsEnabled = true);
            Refresh();
            _filter.Focus();
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            Log.Error($"Relevé des logiciels : {exc}");
            _status.Text = L.T("The software of this PC could not be read: {0}", exc.Message);
        }
    }

    private void Refresh()
    {
        var filter = _filter.Text.Trim();
        var shown = _rows.Where(r => (_showIgnored.IsChecked == true || !r.Ignored)
                                     && (filter.Length == 0 || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                         || r.Publisher.Contains(filter, StringComparison.CurrentCultureIgnoreCase))).ToList();
        _grid.ItemsSource = shown;
        _status.Text = L.T("{0} software of this PC not in the profile.", shown.Count);
    }

    /// <summary>Lignes cochées, sinon lignes sélectionnées.</summary>
    private List<FoundRow> Marked()
    {
        var shown = _grid.ItemsSource as IEnumerable<FoundRow> ?? [];
        var ticked = shown.Where(r => r.Selected).ToList();
        return ticked.Count > 0 ? ticked : _grid.SelectedItems.OfType<FoundRow>().ToList();
    }

    private void Add()
    {
        Chosen = Marked().Select(r => r.Found).ToList();
        if (Chosen.Count == 0)
        {
            _status.Text = L.T("Tick the software to add.");
            return;
        }
        DialogResult = true;
    }

    /// <summary>Retire ou remet les logiciels marqués dans la liste des logiciels à ne plus proposer sur ce poste.</summary>
    private void Ignore(bool ignore)
    {
        var marked = Marked();
        if (marked.Count == 0)
            return;
        var ignored = _services.Settings.IgnoredSoftware;
        foreach (var row in marked)
        {
            ignored.Remove(row.Found.Key);
            if (ignore)
                ignored.Add(row.Found.Key);
        }
        _services.SaveSettings();
        _rows = _rows.Select(r => new FoundRow(r.Found, _services.Settings.IsIgnored(r.Found))).ToList();
        Refresh();
    }
}
