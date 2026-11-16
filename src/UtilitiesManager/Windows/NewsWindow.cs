using System.Windows;
using System.Windows.Controls;

namespace UtilitiesManager;

/// <summary>
/// Nouveautes : rubriques du CHANGELOG embarque (Application, Outils dans TwinCAT, Extension). Le contenu du
/// CHANGELOG reste en francais, seuls les titres des rubriques sont traduits.
/// </summary>
public sealed class NewsWindow : ThemedWindow
{
    private static readonly Dictionary<string, string> SectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        [Changelog.SectionApplication] = "Application",
        [Changelog.SectionTools] = "Tools in TwinCAT",
        [Changelog.SectionExtension] = "Extension",
    };

    public NewsWindow(IReadOnlyList<ChangelogEntry> entries)
    {
        Title = $"{AppInfo.Name} - {Tr.T("What's new")}";
        Width = 640;
        Height = 520;
        MinWidth = 420;
        MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        if (entries.Count == 0)
        {
            content.Children.Add(Ui.Muted(Tr.T("No change is listed.")));
        }

        foreach (var entry in entries)
        {
            var heading = Ui.Heading(string.IsNullOrEmpty(entry.Date)
                ? Tr.T("Version {0}", entry.Version)
                : Tr.T("Version {0} - {1}", entry.Version, entry.Date), 18);
            heading.Margin = new Thickness(0, content.Children.Count == 0 ? 0 : 16, 0, 6);
            content.Children.Add(heading);
            foreach (var section in entry.Sections)
            {
                var title = new TextBlock { Text = SectionTitle(section.Title), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
                title.SetResourceReference(TextBlock.ForegroundProperty, "TcpAccentBrush");
                content.Children.Add(title);
                foreach (var line in section.Lines)
                {
                    content.Children.Add(new TextBlock { Text = "•  " + line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 1, 0, 1) });
                }
            }
        }

        var close = new Button { Content = Tr.T("Close"), IsDefault = true, IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 20, 16) };
        close.Click += (_, _) => Close();
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    public static string SectionTitle(string title) =>
        SectionKeys.TryGetValue(title, out var key) ? Tr.T(key) : title;
}
