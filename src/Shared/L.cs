using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace UtilitiesManager;

/// <summary>
/// Textes de l'interface : le texte anglais sert de clé, sa traduction française est
/// dans <see cref="FrenchCatalog"/>. Langue de Windows par défaut (français si
/// <c>fr-*</c>, anglais sinon), forçable dans les réglages.
/// Fichier lié au lanceur et à l'application : interne à chacun.
/// </summary>
internal static class L
{
    public const string System = "system";
    public const string French = "fr";
    public const string English = "en";

    /// <summary>Vrai si les textes sont affichés en français.</summary>
    public static bool IsFrench { get; private set; } = WindowsIsFrench;

    private static bool WindowsIsFrench =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(French, StringComparison.OrdinalIgnoreCase);

    /// <summary>Choisit la langue : <see cref="French"/>, <see cref="English"/> ou celle de Windows.</summary>
    public static void Use(string? language) =>
        IsFrench = language == French || (language != English && WindowsIsFrench);

    /// <summary>Langue mémorisée dans le fichier de réglages, lue sans désérialiser tout le fichier.</summary>
    public static string? LanguageIn(string settingsFile)
    {
        try
        {
            if (!File.Exists(settingsFile))
                return null;
            var match = Regex.Match(File.ReadAllText(settingsFile), "\"language\"\\s*:\\s*\"(\\w+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Texte traduit dans la langue choisie.</summary>
    public static string T(string english) =>
        IsFrench && FrenchCatalog.Texts.TryGetValue(english, out var french) ? french : english;

    /// <summary>Texte traduit, puis complété par <see cref="string.Format(string, object[])"/>.</summary>
    public static string T(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>Toutes les clés du catalogue (contrôle des traductions dans les tests).</summary>
    public static IEnumerable<string> Keys => FrenchCatalog.Texts.Keys;
}
