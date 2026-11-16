using System.Text.RegularExpressions;

namespace UtilitiesManager.Updates;

public sealed record ChangelogSection(string Title, IReadOnlyList<string> Lines);

public sealed record ChangelogEntry(SemVersion Version, string Date, IReadOnlyList<ChangelogSection> Sections);

/// <summary>
/// CHANGELOG.md au format Keep a Changelog : "## [X.Y.Z] - AAAA-MM-JJ" puis des rubriques "### Application",
/// "### Outils dans TwinCAT", "### Extension". Les versions sans numero (Non publie) sont ignorees.
/// </summary>
public static partial class Changelog
{
    public const string SectionApplication = "Application";
    public const string SectionTools = "Outils dans TwinCAT";
    public const string SectionExtension = "Extension";

    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        SemVersion? version = null;
        var date = string.Empty;
        var sections = new List<ChangelogSection>();
        string? sectionTitle = null;
        var lines = new List<string>();

        void CloseSection()
        {
            if (sectionTitle is not null && lines.Count > 0)
            {
                sections.Add(new ChangelogSection(sectionTitle, lines.ToList()));
            }

            sectionTitle = null;
            lines.Clear();
        }

        void CloseEntry()
        {
            CloseSection();
            if (version is not null)
            {
                entries.Add(new ChangelogEntry(version, date, sections.ToList()));
            }

            version = null;
            sections.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseEntry();
                var match = VersionHeading().Match(line);
                if (match.Success && SemVersion.TryParse(match.Groups["version"].Value, out var parsed))
                {
                    version = parsed;
                    date = match.Groups["date"].Value;
                }
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                CloseSection();
                sectionTitle = line[4..].Trim();
            }
            else if (version is not null && sectionTitle is not null && line.Length > 0)
            {
                lines.Add(line);
            }
        }

        CloseEntry();
        entries.Sort((a, b) => b.Version.CompareTo(a.Version));
        return entries;
    }

    /// <summary>Entrees plus recentes que <paramref name="previous"/>, jusqu'a <paramref name="current"/> inclus.</summary>
    public static IReadOnlyList<ChangelogEntry> Between(IEnumerable<ChangelogEntry> entries, SemVersion? previous, SemVersion current) =>
        entries.Where(e => (previous is null || e.Version > previous) && e.Version <= current).ToList();

    /// <summary>Vrai si une des entrees a une rubrique Extension (la nouvelle version change l'extension).</summary>
    public static bool ChangesExtension(IEnumerable<ChangelogEntry> entries) =>
        entries.Any(e => e.Sections.Any(s => s.Title == SectionExtension));

    [GeneratedRegex(@"^##\s+\[?(?<version>v?\d+(\.\d+){1,3}(-[0-9A-Za-z.-]+)?)\]?(\s*-\s*(?<date>\S+))?")]
    private static partial Regex VersionHeading();
}
