using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UtilitiesManager.Profiles;

namespace UtilitiesManager;

/// <summary>
/// Tableau modifiable d'une liste du profil (retouches .ini, valeurs du registre) : dernière ligne vide pour en
/// ajouter une, bouton pour retirer la ligne choisie.
/// </summary>
public sealed class EditableTable<T> where T : class, new()
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = true,
        CanUserDeleteRows = true,
        SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private ObservableCollection<T> _items = [];

    public EditableTable(double height = 150)
    {
        _grid.Height = height;
        var remove = Ui.TextButton(L.T("Remove the line"), () =>
        {
            if (_grid.SelectedItem is T item)
                _items.Remove(item);
        }, 140);
        remove.Margin = new Thickness(0, 6, 0, 0);
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(_grid);
        panel.Children.Add(remove);
        View = panel;
    }

    public FrameworkElement View { get; }

    public void Text(string header, string property, double width = double.NaN) =>
        _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = Bind(property), Width = Width(width), MinWidth = 70 });

    public void Choice(string header, string property, IReadOnlyList<string> values, double width) =>
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = header, ItemsSource = values, SelectedItemBinding = Bind(property), Width = Width(width) });

    /// <summary>Lignes affichées : copie de la liste, la liste d'origine n'est pas modifiée.</summary>
    public void Show(IEnumerable<T>? items)
    {
        _items = new ObservableCollection<T>((items ?? []).Select(i =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(i, Profile.JsonOptions), Profile.JsonOptions)!));
        _grid.ItemsSource = _items;
    }

    /// <summary>Lignes saisies, cellule en cours de saisie comprise.</summary>
    public List<T> Read()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        return [.. _items];
    }

    private static Binding Bind(string property) =>
        new(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

    private static DataGridLength Width(double width) =>
        double.IsNaN(width) ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width);
}
