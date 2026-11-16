using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace UtilitiesManager.Theme;

/// <summary>
/// Thème Fluent clair ou sombre : choisi dans les réglages, ou celui du réglage "Mode
/// Application" de Windows (<c>system</c>), suivi quand il change pendant l'exécution.
/// </summary>
/// <remarks>
/// Les pinceaux propres à l'application (texte atténué, bordure des cadres) sont
/// renseignés ici pour chaque thème : aucune couleur n'est écrite dans les XAML.
/// </remarks>
public static class ThemeManager
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    private static bool _listening;
    private static string _choice = System;

    /// <summary>Vrai si Windows est réglé sur le thème sombre pour les applications.</summary>
    public static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception exc) when (exc is global::System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Thème sombre pour le choix donné (<c>system</c> : celui de Windows).</summary>
    public static bool IsDark(string? choice) => choice switch
    {
        Dark => true,
        Light => false,
        _ => SystemIsDark(),
    };

    /// <summary>Applique le thème choisi à l'application ; avec <c>system</c>, suit celui de Windows.</summary>
    public static void Apply(Application app, string? choice)
    {
        _choice = choice ?? System;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            app.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _listening = true;
        }
        var dark = IsDark(_choice);
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        ApplyBrushes(app.Resources, dark);
    }

    /// <summary>Pinceaux propres à l'application pour le thème donné.</summary>
    public static void ApplyBrushes(ResourceDictionary resources, bool dark)
    {
        resources["MutedTextBrush"] = Frozen(dark ? Color.FromRgb(0x98, 0xA2, 0xB0) : Color.FromRgb(0x6B, 0x74, 0x80));
        resources["PanelBorderBrush"] = Frozen(dark ? Color.FromRgb(0x33, 0x39, 0x44) : Color.FromRgb(0xDC, 0xE1, 0xE8));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || _choice != System)
            return;
        var app = Application.Current;
        app?.Dispatcher.BeginInvoke(() =>
        {
            if (SystemIsDark() != (app.ThemeMode == ThemeMode.Dark))
                Apply(app, _choice);
        });
    }
}
