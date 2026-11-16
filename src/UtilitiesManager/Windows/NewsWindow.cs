using System.IO;
using System.Windows;
using System.Windows.Controls;
using UtilitiesManager.Updates;

namespace UtilitiesManager;

/// <summary>
/// Nouveautés : versions du CHANGELOG embarqué, la plus récente en haut. Le contenu reste en français (langue du
/// CHANGELOG).
/// </summary>
public sealed class NewsWindow : ThemedWindow
{
    public NewsWindow(IReadOnlyList<ChangelogEntry> entries)
    {
        Title = $"{AppPaths.AppName} - {L.T("What's new")}";
        Width = 640;
        Height = 520;
        MinWidth = 420;
        MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        if (entries.Count == 0)
            content.Children.Add(Ui.Muted(L.T("No change is listed.")));
        foreach (var entry in entries)
        {
            var heading = Ui.Heading(string.IsNullOrEmpty(entry.Date)
                ? L.T("Version {0}", entry.Version)
                : L.T("Version {0} - {1}", entry.Version, entry.Date), 18);
            heading.Margin = new Thickness(0, content.Children.Count == 0 ? 0 : 16, 0, 6);
            content.Children.Add(heading);
            foreach (var section in entry.Sections)
            {
                content.Children.Add(new TextBlock { Text = section.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
                foreach (var line in section.Lines)
                    content.Children.Add(new TextBlock { Text = "•  " + line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 1, 0, 1) });
            }
        }

        var close = new Button { Content = L.T("Close"), IsDefault = true, IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 20, 16) };
        close.Click += (_, _) => Close();
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    /// <summary>Entrées du CHANGELOG embarqué dans l'application.</summary>
    public static IReadOnlyList<ChangelogEntry> Embedded()
    {
        using var stream = typeof(NewsWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
            return [];
        using var reader = new StreamReader(stream);
        return Changelog.Parse(reader.ReadToEnd());
    }
}
