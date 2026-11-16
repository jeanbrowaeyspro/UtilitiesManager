using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UtilitiesManager;

/// <summary>Fenêtre qui suit le thème : fond, texte et icône de l'application.</summary>
public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        Icon = Ui.AppIcon;
        FontSize = 14;
    }
}

/// <summary>Petits elements d'interface communs, construits en code : aucun texte en XAML.</summary>
public static class Ui
{
    private static ImageSource? _appIcon;

    /// <summary>Glyphes de Windows 11, ceux de Windows 10 à défaut.</summary>
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string IconUri = "pack://application:,,,/UtilitiesManager.App;component/assets/icon.ico";

    public static ImageSource AppIcon => _appIcon ??= LoadIcon();

    public static TextBlock Glyph(string glyph, double size = 16)
    {
        var text = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        text.FontFamily = IconFont;
        return text;
    }

    public static TextBlock Heading(string text, double size = 20) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };

    public static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        return block;
    }

    /// <summary>Glyphe suivi d'un texte, contenu d'un bouton de barre d'outils ou de barre laterale.</summary>
    public static StackPanel GlyphLabel(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Glyph(glyph);
        icon.Margin = new Thickness(0, 0, 10, 0);
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    /// <summary>Grille libelle / valeur a deux colonnes.</summary>
    public static Grid KeyValueGrid(IEnumerable<(string Key, string Value)> rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var (key, value) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = grid.RowDefinitions.Count - 1;
            var label = Muted(key);
            label.Margin = new Thickness(0, 2, 16, 2);
            Grid.SetRow(label, row);
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(label);
            grid.Children.Add(text);
        }

        return grid;
    }

    /// <summary>Ligne de Parametres : libelle a gauche, controle a droite.</summary>
    public static FrameworkElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>Case a cocher de Parametres, enregistree a chaque clic.</summary>
    public static CheckBox Check(string label, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 0, 10) };
        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>Liste deroulante liee a l'element choisi (SelectedItem), jamais a un index.</summary>
    public static ComboBox Choices<T>(IReadOnlyList<(T Value, string Text)> options, T current, Action<T> changed)
    {
        var items = options.Select(o => new ComboBoxItem { Content = o.Text, Tag = o.Value }).ToList();
        var box = new ComboBox { ItemsSource = items, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        box.SelectedItem = items.FirstOrDefault(i => Equals(i.Tag, current)) ?? items.FirstOrDefault();
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: T value })
            {
                changed(value);
            }
        };
        return box;
    }

    /// <summary>Bouton de texte simple (pages et dialogues).</summary>
    public static Button TextButton(string text, Action click, double minWidth = 110)
    {
        var button = new Button { Content = text, MinWidth = minWidth, Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Ouvre un dossier ou une page dans l'application associee de Windows.</summary>
    public static void OpenExternal(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    /// <summary>Icone multi-tailles : la barre de titre et la barre des taches choisissent leur image.</summary>
    private static ImageSource LoadIcon() =>
        BitmapFrame.Create(new Uri(IconUri));
}
