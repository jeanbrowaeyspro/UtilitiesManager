using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace UtilitiesManager;

/// <summary>
/// A propos : icone, nom, versions et date de construction, auteur, chemins des reglages et des journaux,
/// licences des composants tiers.
/// </summary>
public sealed class AboutWindow : ThemedWindow
{
    /// <summary>Composants tiers livres ou utilises pour construire TwinCATPlus.</summary>
    private static readonly (string Name, string License)[] ThirdParty =
    [
        (".NET, WPF, Windows Forms", "MIT License, © .NET Foundation and Contributors"),
        ("Inno Setup", "Inno Setup License, © Jordan Russell, Martijn Laan"),
    ];

    public AboutWindow(ShellServices services)
    {
        Title = $"{AppInfo.Name} - {Tr.T("About")}";
        SizeToContent = SizeToContent.Height;
        Width = 560;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new Image { Source = Ui.AppIcon, Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0) });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Heading(AppInfo.Name, 24);
        name.Margin = new Thickness(0);
        title.Children.Add(name);
        title.Children.Add(Ui.Muted(Tr.T("Version {0}, built on {1}", AppInfo.ProductVersion, AppInfo.BuildDate)));
        header.Children.Add(title);

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        content.Children.Add(header);
        content.Children.Add(Ui.KeyValueGrid(HomePage.VersionRows(TwinCatInstallation.ReadFromMachine())));
        content.Children.Add(new TextBlock { Text = AppInfo.Author, Margin = new Thickness(0, 16, 0, 2) });
        content.Children.Add(Ui.Muted(AppInfo.PoweredBy));

        content.Children.Add(Section(Tr.T("Files")));
        content.Children.Add(PathRow(Tr.T("Settings"), services.Paths.SettingsFile, services.Paths.DataFolder));
        content.Children.Add(PathRow(Tr.T("Logs"), services.Paths.LogFolder, services.Paths.LogFolder));

        content.Children.Add(Section(Tr.T("Third-party components")));
        content.Children.Add(Ui.KeyValueGrid(ThirdParty.Select(t => (t.Name, t.License))));

        var close = new Button { Content = Tr.T("Close"), IsDefault = true, IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
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

    /// <summary>Chemin affiche avec un bouton Ouvrir (dossier cree au besoin).</summary>
    private static FrameworkElement PathRow(string label, string path, string folder)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var key = Ui.Muted(label);
        key.VerticalAlignment = VerticalAlignment.Center;
        var value = new TextBox { Text = path, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        var open = new Button { Content = Tr.T("Open"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
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
