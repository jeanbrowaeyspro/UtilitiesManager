using System.Text;
using System.Text.RegularExpressions;

namespace UtilitiesManager.Software;

/// <summary>
/// Clé d'un fichier <c>.ini</c> posée après l'installation d'un logiciel (ex. vérification intégrée des mises à
/// jour coupée, puisque UtilitiesManager s'en charge).
/// </summary>
/// <remarks>
/// Seule la valeur est modifiée : le reste du fichier est réécrit octet pour octet. Un fichier UTF-16 (avec BOM)
/// est traité en UTF-16 ; tout autre fichier est lu en Latin-1, qui fait correspondre un caractère à chaque octet :
/// UTF-8 (avec ou sans BOM) comme ANSI ressortent inchangés, fins de ligne comprises.
/// </remarks>
public static partial class IniFile
{
    [GeneratedRegex(@"^[ \t]*\[(?<name>[^\]\r\n]*)\][ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex SectionHeader();

    private static Regex KeyLine(string key) =>
        new(@"^(?<prefix>[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)(?<value>[^\r\n]*)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>Valeur lue dans le texte ; null si la section ou la clé manque.</summary>
    public static string? Read(string text, string section, string key)
    {
        var (start, end) = Section(text, section);
        if (start < 0)
            return null;
        var match = KeyLine(key).Match(text[..end], start);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>
    /// Texte avec la nouvelle valeur. La clé est ajoutée juste après l'en-tête de la section si elle manque, et la
    /// section en fin de fichier si elle manque.
    /// </summary>
    public static string With(string text, string section, string key, string value)
    {
        var (start, end) = Section(text, section);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        if (start < 0)
        {
            var separator = text.Length == 0 || text.EndsWith('\n') ? "" : newline;
            return $"{text}{separator}[{section}]{newline}{key}={value}{newline}";
        }
        var match = KeyLine(key).Match(text[..end], start);
        if (match.Success)
        {
            var group = match.Groups["value"];
            return string.Concat(text.AsSpan(0, group.Index), value, text.AsSpan(group.Index + group.Length));
        }
        var lineEnd = text.IndexOf('\n', start - 1);
        var insertAt = lineEnd < 0 ? text.Length : lineEnd + 1;
        var prefix = lineEnd < 0 ? newline : "";
        return text.Insert(insertAt, $"{prefix}{key}={value}{newline}");
    }

    /// <summary>Début (juste après le nom entre crochets) et fin de la section ; (-1, -1) si absente.</summary>
    private static (int Start, int End) Section(string text, string section)
    {
        var headers = SectionHeader().Matches(text);
        for (var i = 0; i < headers.Count; i++)
        {
            if (!headers[i].Groups["name"].Value.Trim().Equals(section, StringComparison.OrdinalIgnoreCase))
                continue;
            var start = headers[i].Index + headers[i].Length;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            return (start, end);
        }
        return (-1, -1);
    }

    private static Encoding EncodingOf(byte[] bytes) => bytes switch
    {
        [0xFF, 0xFE, ..] => Encoding.Unicode,
        [0xFE, 0xFF, ..] => Encoding.BigEndianUnicode,
        _ => Encoding.Latin1,
    };

    /// <summary>
    /// Pose la valeur dans le fichier (chemin avec variables d'environnement) en conservant encodage et fins de
    /// ligne ; fichier créé s'il manque. Renvoie faux si la valeur était déjà la bonne.
    /// </summary>
    public static bool Apply(string path, string section, string key, string value)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
        var encoding = EncodingOf(bytes);
        var text = encoding.GetString(bytes);
        if (Read(text, section, key) == value)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // GetString garde le BOM UTF-16 comme caractère U+FEFF : il est réécrit tel quel.
        File.WriteAllBytes(path, encoding.GetBytes(With(text, section, key, value)));
        return true;
    }
}
