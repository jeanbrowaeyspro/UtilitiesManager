using System.Diagnostics;
using System.Text;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>Ligne de <c>winget list</c>.</summary>
public sealed record WingetPackage(string Name, string Id, string Version, string Available, string Source);

/// <summary>
/// Moteur principal : <c>winget list</c> donne les versions installées et disponibles de tout le PC en un appel,
/// <c>winget install</c> / <c>upgrade</c> / <c>uninstall</c> agissent sur un paquet. winget est une console : elle
/// reste cachée, seule l'UAC des installeurs apparaît.
/// </summary>
public sealed class Winget(IProcessRunner runner)
{
    public const string ExeName = "winget.exe";
    private const string Agreements = "--accept-source-agreements --disable-interactivity";

    /// <summary>Codes de sortie d'une installation annulée (UAC refusée, installeur fermé).</summary>
    public static readonly int[] CancelledCodes = [unchecked((int)0x8A15010C), 1223, unchecked((int)0x800704C7)];

    private string? _path;

    /// <summary>
    /// Premier winget.exe trouvé : alias d'exécution de l'utilisateur (<c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>),
    /// puis le PATH. Null si absent.
    /// </summary>
    public string? Path => _path ??= Find(CandidateDirs(), File.Exists);

    public static IEnumerable<string> CandidateDirs()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
            yield return System.IO.Path.Combine(local, "Microsoft", "WindowsApps");
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            yield return dir.Trim();
    }

    public static string? Find(IEnumerable<string> dirs, Func<string, bool> exists) =>
        dirs.Select(dir => System.IO.Path.Combine(dir, ExeName)).FirstOrDefault(exists);

    /// <summary>Code de sortie lisible : les codes de winget sont des HRESULT.</summary>
    public static string ExitCodeText(int code) => code < 0 || code > 0xFFFF ? $"0x{code:X8}" : code.ToString();

    private string RequirePath() => Path ?? throw new InvalidOperationException(L.T("winget is not installed on this PC."));

    private ProcessStartInfo Start(string arguments) => new(RequirePath(), arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };

    /// <summary>Tous les logiciels installés connus de winget.</summary>
    public async Task<IReadOnlyList<WingetPackage>> ListAsync(CancellationToken cancel = default)
    {
        var output = await runner.CaptureAsync(Start($"list {Agreements}"), cancel).ConfigureAwait(false);
        var packages = ParseList(output.Text);
        if (packages.Count == 0)
            throw new InvalidOperationException(L.T("winget list failed (code {0}).", ExitCodeText(output.ExitCode)));
        return packages;
    }

    /// <summary>Recherche dans les sources winget (formulaire Ajouter).</summary>
    public async Task<IReadOnlyList<WingetPackage>> SearchAsync(string query, CancellationToken cancel = default)
    {
        var output = await runner.CaptureAsync(Start($"search \"{query.Replace("\"", "")}\" {Agreements}"), cancel).ConfigureAwait(false);
        return ParseList(output.Text);
    }

    /// <summary>
    /// Lecture du tableau par la position des colonnes de l'en-tête (ligne suivie de tirets). Les animations de
    /// progression écrites avant un retour chariot sont ignorées : seul le texte après le dernier <c>\r</c> compte.
    /// </summary>
    public static IReadOnlyList<WingetPackage> ParseList(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).Select(line => line[(line.LastIndexOf('\r') + 1)..]).ToList();
        var packages = new List<WingetPackage>();
        for (var i = 0; i + 1 < lines.Count; i++)
        {
            if (!lines[i + 1].StartsWith("---", StringComparison.Ordinal))
                continue;
            var columns = Columns(lines[i]);
            if (columns.Count < 3)
                continue;
            var sourceColumn = columns.FindIndex(c => IsSource(c.Name));
            var availableColumn = columns.Count > 3 && sourceColumn != 3 ? 3 : -1;
            for (var j = i + 2; j < lines.Count; j++)
            {
                if (j + 1 < lines.Count && lines[j + 1].StartsWith("---", StringComparison.Ordinal))
                    break;
                var line = lines[j];
                if (line.Length <= columns[1].Start)
                    continue;
                string Cell(int index)
                {
                    if (index < 0 || index >= columns.Count || columns[index].Start >= line.Length)
                        return "";
                    var end = index + 1 < columns.Count ? Math.Min(columns[index + 1].Start, line.Length) : line.Length;
                    return line[columns[index].Start..end].Trim();
                }
                var id = Cell(1);
                if (id.Length == 0 || id.Contains(' '))
                    continue;
                packages.Add(new WingetPackage(Cell(0), id, Cell(2), Cell(availableColumn), Cell(sourceColumn)));
            }
        }
        return packages;
    }

    private static bool IsSource(string name) => name.Equals("Source", StringComparison.OrdinalIgnoreCase);

    private static List<(string Name, int Start)> Columns(string header)
    {
        var columns = new List<(string, int)>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] == ' ' || (i > 0 && header[i - 1] != ' '))
                continue;
            var end = header.IndexOf(' ', i);
            columns.Add((header[i..(end < 0 ? header.Length : end)], i));
        }
        return columns;
    }

    /// <summary>Paquet du profil dans la liste : identifiant exact, ou motif <c>detect.winget_id</c> (Store).</summary>
    public static WingetPackage? Match(ProgramEntry program, IReadOnlyList<WingetPackage> installed)
    {
        var exact = installed.FirstOrDefault(p => p.Id.Equals(program.Package, StringComparison.OrdinalIgnoreCase));
        if (exact is not null || program.Detect?.WingetId is not { } pattern)
            return exact;
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return installed.FirstOrDefault(p => regex.IsMatch(p.Id));
    }

    /// <summary>
    /// Arguments d'installation (<paramref name="upgrade"/> faux) ou de mise à jour ; <paramref name="version"/>
    /// impose une version (version bloquée, retour à la version précédente).
    /// </summary>
    public static string InstallArguments(ProgramEntry program, bool upgrade, string? version = null)
    {
        var arguments = new StringBuilder(upgrade && version is null ? "upgrade" : "install");
        arguments.Append($" --id {program.Package} --exact");
        if (program.Source == Sources.MsStore)
            arguments.Append(" --source msstore");
        if (version is not null)
            arguments.Append($" --version \"{version}\"").Append(upgrade ? " --force" : "");
        arguments.Append(program.Interactive ? " --interactive" : " --silent");
        if (!string.IsNullOrWhiteSpace(program.SilentArgs))
            arguments.Append(' ').Append(program.SilentArgs);
        arguments.Append(" --accept-source-agreements --accept-package-agreements --disable-interactivity");
        return arguments.ToString();
    }

    public static string UninstallArguments(ProgramEntry program) =>
        $"uninstall --id {program.Package} --exact --silent{(program.Source == Sources.MsStore ? " --source msstore" : "")} {Agreements}";

    public Task<ProcessOutput> RunAsync(string arguments, CancellationToken cancel = default) =>
        runner.CaptureAsync(Start(arguments), cancel);
}
