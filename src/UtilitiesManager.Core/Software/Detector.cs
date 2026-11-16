using System.Diagnostics;
using System.Text.RegularExpressions;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>Logiciel trouvé sur le PC : présence, version (si connue) et commande de désinstallation inscrite.</summary>
public sealed record InstalledInfo(bool Present, string? Version, string? UninstallString = null)
{
    public static readonly InstalledInfo Absent = new(false, null);
}

/// <summary>Version installée d'un logiciel du profil, selon sa source et sa règle <c>detect</c>.</summary>
public sealed class Detector(IRegistryStore registry, ShellCommands shell)
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly RegistryRoot[] Roots = [RegistryRoot.CurrentUser, RegistryRoot.LocalMachine64, RegistryRoot.LocalMachine32];

    public async Task<InstalledInfo> DetectAsync(ProgramEntry program, IReadOnlyList<WingetPackage> winget, MachineState state,
                                                 CancellationToken cancel = default)
    {
        var detect = program.Detect;
        InstalledInfo found;
        if (program.IsWinget && detect?.DisplayName is null && detect?.File is null && detect?.Command is null)
            found = Winget.Match(program, winget) is { } package ? new InstalledInfo(true, Clean(package.Version)) : InstalledInfo.Absent;
        else if (detect?.DisplayName is { } displayName)
            found = FromUninstallKeys(displayName);
        else if (detect?.File is { } file)
            found = FromFile(Environment.ExpandEnvironmentVariables(file));
        else if (detect?.Command is { } command)
            found = Match(await shell.ReadAsync(ShellCommands.Expand(command), cancel).ConfigureAwait(false), detect.Regex);
        else
            found = InstalledInfo.Absent;

        if (found.Present && found.Version is null && state.InstalledVersions.TryGetValue(program.Id, out var known))
            found = found with { Version = known };
        return found;
    }

    /// <summary>winget écrit "Unknown" ou "&lt; 1.2" quand il ne connaît pas la version exacte.</summary>
    private static string? Clean(string version) =>
        VersionText.Parse(version) is null ? null : version.TrimStart('<', '>', ' ');

    /// <summary>Premier programme installé dont le nom affiché correspond au motif.</summary>
    public InstalledInfo FromUninstallKeys(string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        foreach (var root in Roots)
        {
            foreach (var name in registry.SubKeyNames(root, UninstallKey))
            {
                var key = $@"{UninstallKey}\{name}";
                if (registry.GetString(root, key, "DisplayName") is not { } displayName || !regex.IsMatch(displayName))
                    continue;
                var uninstall = registry.GetString(root, key, "QuietUninstallString") ?? registry.GetString(root, key, "UninstallString");
                return new InstalledInfo(true, registry.GetString(root, key, "DisplayVersion"), uninstall);
            }
        }
        return InstalledInfo.Absent;
    }

    private static InstalledInfo FromFile(string path)
    {
        if (!File.Exists(path))
            return InstalledInfo.Absent;
        var info = FileVersionInfo.GetVersionInfo(path);
        var version = info.FileMajorPart + info.FileMinorPart + info.FileBuildPart + info.FilePrivatePart == 0
            ? null
            : $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        return new InstalledInfo(true, version);
    }

    /// <summary>
    /// Sortie d'une commande : présent si le motif correspond (version = premier groupe), ou, sans motif, si la
    /// commande réussit.
    /// </summary>
    public static InstalledInfo Match(ProcessOutput output, string? pattern)
    {
        if (pattern is null)
            return output.ExitCode == 0 ? new InstalledInfo(true, null) : InstalledInfo.Absent;
        var match = Regex.Match(output.Text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (!match.Success)
            return InstalledInfo.Absent;
        return new InstalledInfo(true, match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : null);
    }
}
