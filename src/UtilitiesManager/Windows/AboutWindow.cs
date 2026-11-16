using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace UtilitiesManager;

/// <summary>À propos : icône, nom, version, auteur, chemins des réglages et du journal, composants tiers.</summary>
public sealed class AboutWindow : ThemedWindow
{
    private const string Author = "Jean Browaeys — Automaticien Indépendant";

    /// <summary>Composants tiers utilisés par UtilitiesManager.</summary>
    private static readonly (string Name, string License)[] ThirdParty =
    [
        (".NET, WPF, Windows Forms", "MIT License, © .NET Foundation and Contributors"),
        ("winget (Windows Package Manager)", "MIT License, © Microsoft Corporation"),
    ];

    public AboutWindow(AppPaths paths)
    {
        Title = $"{AppPaths.AppName} - {L.T("About")}";
        SizeToContent = SizeToContent.Height;
        Width = 560;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new Image { Source = Ui.AppIcon, Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0) });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Heading(AppPaths.AppName, 24);
        name.Margin = new Thickness(0);
        title.Children.Add(name);
        title.Children.Add(Ui.Muted(L.T("Version {0}", AppInfo.Version)));
        header.Children.Add(title);

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        content.Children.Add(header);
        content.Children.Add(new TextBlock { Text = Author, Margin = new Thickness(0, 0, 0, 2) });

        content.Children.Add(Section(L.T("Files")));
        content.Children.Add(PathRow(L.T("Settings"), paths.SettingsFile));
        content.Children.Add(PathRow(L.T("Log"), paths.LogFile));

        content.Children.Add(Section(L.T("Third-party components")));
        content.Children.Add(Ui.KeyValueGrid(ThirdParty.Select(t => (t.Name, t.License))));

        var close = new Button { Content = L.T("Close"), IsDefault = true, IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        close.Click += (_, _) => Close();
        content.Children.Add(close);
        Content = content;
    }

    private static TextBlock Section(string text)
    {
        var heading = Ui.Heading(text, 16);
        heading.Margin = new Thickness(0, 20, 0, 6);
        return heading;
    }

    /// <summary>Chemin affiché avec un bouton Ouvrir (dossier du fichier, créé au besoin).</summary>
    private static FrameworkElement PathRow(string label, string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var key = Ui.Muted(label);
        key.VerticalAlignment = VerticalAlignment.Center;
        var value = new TextBox { Text = path, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        var open = new Button { Content = L.T("Open"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(folder);
            Ui.OpenExternal(folder);
        };
        Grid.SetColumn(value, 1);
        Grid.SetColumn(open, 2);
        grid.Children.Add(key);
        grid.Children.Add(value);
        grid.Children.Add(open);
        return grid;
    }
}
