using System.Diagnostics;
using System.Text.RegularExpressions;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>Où un logiciel installé a été trouvé.</summary>
public enum FoundOrigin
{
    /// <summary>Paquet d'une source winget (catalogue winget ou Microsoft Store) : installable tel quel.</summary>
    Winget,
    /// <summary>Programme inscrit dans Windows (registre Uninstall, ou application MSIX hors catalogue).</summary>
    Registry,
    /// <summary>Outil portable du dossier des logiciels portables.</summary>
    Folder,
}

/// <summary>
/// Logiciel installé sur ce PC. <see cref="Key"/> l'identifie d'un relevé à l'autre (logiciels à ne plus proposer).
/// </summary>
public sealed record FoundSoftware(string Name, string? Version, string? Publisher, FoundOrigin Origin,
                                   string? WingetId = null, string? WingetSource = null, string? Exe = null, string? Folder = null)
{
    /// <summary>Dossier d'installation d'un outil portable : le sien, ou celui de son exe s'il est à la racine.</summary>
    public string? InstallDir => Folder ?? (Exe is null ? null : Path.GetDirectoryName(Exe));

    /// <summary>Identifiant winget ou Store exact : la source est connue sans recherche.</summary>
    public bool IsSure => Origin == FoundOrigin.Winget && WingetId is not null;

    public string Key => WingetId is { } id
        ? Inventory.StableWingetId(id)
        : Exe is { } exe ? "file:" + exe.ToLowerInvariant() : "name:" + Inventory.Normalize(Name);
}

/// <summary>
/// Relevé des logiciels installés sur ce PC et absents du profil : <c>winget list</c>, programmes inscrits dans
/// Windows et outils du dossier des logiciels portables.
/// </summary>
public sealed class Inventory(IRegistryStore registry)
{
    private static readonly RegistryRoot[] Roots = [RegistryRoot.CurrentUser, RegistryRoot.LocalMachine64, RegistryRoot.LocalMachine32];

    /// <summary>Éditeurs des applications MSIX livrées avec Windows (Microsoft, Windows) : jamais proposées.</summary>
    private static readonly string[] SystemPublishers = ["_8wekyb3d8bbwe", "_cw5n1h2txyewy"];

    /// <summary>Logiciels trouvés, triés par nom, sans ceux du profil ni UtilitiesManager lui-même.</summary>
    public IReadOnlyList<FoundSoftware> Scan(Profile profile, IReadOnlyList<WingetPackage> winget, string? folder)
    {
        var found = new Dictionary<string, FoundSoftware>();
        foreach (var package in winget)
        {
            if (FromWinget(package) is { } item)
                found.TryAdd(Normalize(item.Name), item);
        }
        foreach (var item in FromRegistry())
        {
            var key = Normalize(item.Name);
            if (found.TryGetValue(key, out var known))
                found[key] = known with { Publisher = known.Publisher ?? item.Publisher };
            else
                found[key] = item;
        }
        var all = found.Values.Concat(folder is null ? [] : FromFolder(folder));
        return all.Where(item => item.Name.Length > 0 && Normalize(item.Name) != Normalize(AppPaths.AppName)
                                 && !profile.Programs.Any(p => Matches(p, item)))
                  .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Paquet de <c>winget list</c> ; null pour une application MSIX livrée avec Windows.</summary>
    public static FoundSoftware? FromWinget(WingetPackage package)
    {
        var source = package.Source.ToLowerInvariant();
        if (source is Sources.Winget or Sources.MsStore)
            return new FoundSoftware(package.Name, Version(package.Version), null, FoundOrigin.Winget, package.Id, source);
        if (package.Id.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase)
            && SystemPublishers.Any(p => package.Id.EndsWith(p, StringComparison.OrdinalIgnoreCase)))
            return null;
        // Programme inscrit dans Windows (ARP\...) ou application MSIX hors catalogue : source à chercher.
        return new FoundSoftware(package.Name, Version(package.Version), null, FoundOrigin.Registry, package.Id);
    }

    /// <summary>
    /// Programmes de la liste "Applications installées" : ni composant système, ni mise à jour, ni sous-élément d'un
    /// autre programme, avec une commande de désinstallation.
    /// </summary>
    public IEnumerable<FoundSoftware> FromRegistry()
    {
        foreach (var root in Roots)
        {
            foreach (var name in registry.SubKeyNames(root, Detector.UninstallKey))
            {
                var key = $@"{Detector.UninstallKey}\{name}";
                if (registry.GetString(root, key, "DisplayName") is not { Length: > 0 } displayName
                    || registry.GetValue(root, key, "SystemComponent") is 1
                    || registry.GetString(root, key, "ParentKeyName") is not null
                    || registry.GetString(root, key, "ReleaseType") is { } release && Regex.IsMatch(release, "update|hotfix", RegexOptions.IgnoreCase)
                    || registry.GetString(root, key, "UninstallString") is null)
                    continue;
                yield return new FoundSoftware(displayName.Trim(), registry.GetString(root, key, "DisplayVersion"),
                                               registry.GetString(root, key, "Publisher"), FoundOrigin.Registry);
            }
        }
    }

    /// <summary>
    /// Outils portables : un sous-dossier par outil (son exe principal), et chaque exe posé à la racine du dossier.
    /// </summary>
    public static IEnumerable<FoundSoftware> FromFolder(string folder)
    {
        folder = Environment.ExpandEnvironmentVariables(folder);
        if (!Directory.Exists(folder))
            yield break;
        foreach (var exe in SafeFiles(folder))
            yield return FromExe(exe, null);
        foreach (var directory in SafeDirectories(folder))
        {
            // Dossiers spéciaux de l'Explorateur ("Nom.{GUID}") : pas des outils.
            if (Path.GetFileName(directory).Contains(".{"))
                continue;
            if (MainExe(directory, SafeFiles(directory).ToList()) is { } exe)
                yield return FromExe(exe, directory);
        }
    }

    /// <summary>Exe principal d'un dossier : celui dont le nom ressemble le plus au dossier, sinon le plus gros.</summary>
    public static string? MainExe(string directory, IReadOnlyList<string> exes)
    {
        if (exes.Count == 0)
            return null;
        var folderName = Normalize(Path.GetFileName(directory));
        return exes.FirstOrDefault(e => Normalize(Path.GetFileNameWithoutExtension(e)) == folderName)
               ?? exes.FirstOrDefault(e => folderName.StartsWith(Normalize(Path.GetFileNameWithoutExtension(e)), StringComparison.Ordinal)
                                           && Normalize(Path.GetFileNameWithoutExtension(e)).Length > 2)
               ?? exes.MaxBy(e => new FileInfo(e).Length);
    }

    /// <param name="folder">Dossier propre à l'outil ; null pour un exe posé à la racine (dossier partagé).</param>
    private static FoundSoftware FromExe(string exe, string? folder)
    {
        var info = FileVersionInfo.GetVersionInfo(exe);
        var name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName.Trim()
                 : !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription.Trim()
                 : Path.GetFileNameWithoutExtension(exe);
        var version = Version(info.ProductVersion ?? info.FileVersion ?? "");
        return new FoundSoftware(name, version, string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim(),
                                 FoundOrigin.Folder, Exe: exe, Folder: folder);
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try
        {
            return Directory.GetFiles(folder, "*.exe");
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Vrai si le logiciel trouvé est celui de l'entrée du profil (mêmes règles que la détection).</summary>
    public static bool Matches(ProgramEntry program, FoundSoftware found)
    {
        if (found.WingetId is { } id)
        {
            if (id.Equals(program.Package, StringComparison.OrdinalIgnoreCase))
                return true;
            if (program.Detect?.WingetId is { } pattern && Regex.IsMatch(id, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        if (program.Detect?.DisplayName is { } displayName && found.Origin != FoundOrigin.Folder
            && Regex.IsMatch(found.Name, displayName, RegexOptions.IgnoreCase))
            return true;
        if (found.Exe is { } exe)
        {
            if (program.Detect?.File is { } file && SamePath(Environment.ExpandEnvironmentVariables(file), exe))
                return true;
            if (program.InstallDir is { } dir && found.Folder is { } folder && SamePath(Environment.ExpandEnvironmentVariables(dir), folder))
                return true;
        }
        var name = Normalize(found.Name);
        return name.Length > 0 && (name == Normalize(program.Name) || name == Normalize(program.Id));
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Nom sans parenthèses, numéro de version ni architecture ("7-Zip 24.09 (x64)" → "7-Zip").</summary>
    public static string Clean(string name)
    {
        var text = Regex.Replace(name, @"\([^)]*\)", " ");
        text = Regex.Replace(text, @"\bv?\d+(?:[._]\d+)+[a-z]?\b", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(x64|x86|amd64|arm64|64-bit|32-bit|64 bits|32 bits|win64|win32)\b", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '_');
        return text.Length > 0 ? text : name.Trim();
    }

    /// <summary>Nom comparable : <see cref="Clean"/> en minuscules, sans ponctuation ("7-Zip 24.09 (x64)" → "7zip").</summary>
    public static string Normalize(string name) => Regex.Replace(Clean(name).ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");

    /// <summary>Identifiant winget sans le numéro de version des applications MSIX (famille du paquet).</summary>
    public static string StableWingetId(string id) =>
        id.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase) && id.IndexOf('_') is var cut and > 0 ? id[..cut] : id;

    /// <summary>winget écrit "Unknown" ou "&lt; 1.2" quand il ne connaît pas la version exacte.</summary>
    private static string? Version(string text) =>
        VersionText.Parse(text) is null ? null : text.Trim().TrimStart('<', '>', ' ');
}
