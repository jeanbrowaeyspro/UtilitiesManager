using System.Text.RegularExpressions;

namespace UtilitiesManager;

/// <summary>Lecture et comparaison des numéros de version (<c>v1.3.323</c>, <c>1.3.315</c>…).</summary>
public static partial class VersionText
{
    [GeneratedRegex(@"\d+(\.\d+){1,3}")]
    private static partial Regex Number();

    /// <summary>
    /// Version contenue dans le texte, complétée à quatre composantes (1.3.315 devient
    /// 1.3.315.0) pour que deux écritures d'une même version soient égales.
    /// </summary>
    public static Version? Parse(string? text)
    {
        var match = Number().Match(text ?? "");
        if (!match.Success || !Version.TryParse(match.Value, out var v))
            return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    /// <summary>Affichage court : composantes nulles de fin retirées au-delà de trois.</summary>
    public static string Display(Version version) =>
        version.Revision > 0 ? version.ToString() : version.ToString(3);
}
