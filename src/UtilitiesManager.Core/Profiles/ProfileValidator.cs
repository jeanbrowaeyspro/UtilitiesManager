using System.Text.Json;
using System.Text.RegularExpressions;

namespace UtilitiesManager.Profiles;

/// <summary>
/// Contrôle d'un profil avant de l'appliquer : un profil invalide n'est jamais appliqué, la copie locale reste en
/// service et les erreurs sont signalées.
/// </summary>
public static partial class ProfileValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex IdPattern();

    /// <summary>Profil lu et contrôlé ; null et les erreurs s'il est illisible ou invalide.</summary>
    public static (Profile? Profile, IReadOnlyList<string> Errors) Read(string json)
    {
        Profile profile;
        try
        {
            profile = Profile.Parse(json);
        }
        catch (JsonException exc)
        {
            return (null, [L.T("Unreadable profile: {0}", exc.Message)]);
        }
        var errors = Validate(profile);
        return (errors.Count == 0 ? profile : null, errors);
    }

    public static IReadOnlyList<string> Validate(Profile profile)
    {
        var errors = new List<string>();
        if (profile.Schema != Profile.CurrentSchema)
            errors.Add(L.T("Profile format {0} unknown to this version of UtilitiesManager.", profile.Schema));
        CheckMode(profile.Defaults.Mode, L.T("defaults"), errors);
        if (profile.Defaults.MinAgeDays < 0)
            errors.Add(L.T("{0}: negative minimum age.", L.T("defaults")));

        var ids = new HashSet<string>();
        foreach (var program in profile.Programs)
        {
            var name = string.IsNullOrWhiteSpace(program.Id) ? "?" : program.Id;
            if (!IdPattern().IsMatch(program.Id))
                errors.Add(L.T("{0}: invalid identifier (lowercase letters, digits, dot, dash).", name));
            if (!ids.Add(program.Id))
                errors.Add(L.T("{0}: identifier used twice.", name));
            if (string.IsNullOrWhiteSpace(program.Name))
                errors.Add(L.T("{0}: name missing.", name));
            if (program.Mode is not null)
                CheckMode(program.Mode, name, errors);
            if (program.MinAgeDays < 0)
                errors.Add(L.T("{0}: negative minimum age.", name));
            CheckSource(program, name, errors);
            CheckPatterns(program, name, errors);
            if (program.Ini?.Any(i => string.IsNullOrWhiteSpace(i.File) || string.IsNullOrWhiteSpace(i.Section) || string.IsNullOrWhiteSpace(i.Key)) == true)
                errors.Add(L.T("{0}: .ini setting without file, section or key.", name));
        }
        foreach (var program in profile.Programs.Where(p => p.DependsOn is not null))
        {
            if (program.DependsOn == program.Id || !ids.Contains(program.DependsOn!))
                errors.Add(L.T("{0}: unknown dependency {1}.", program.Id, program.DependsOn!));
        }

        var tweakIds = new HashSet<string>();
        foreach (var tweak in profile.Tweaks)
        {
            var name = string.IsNullOrWhiteSpace(tweak.Id) ? "?" : tweak.Id;
            if (!IdPattern().IsMatch(tweak.Id) || !tweakIds.Add(tweak.Id))
                errors.Add(L.T("{0}: invalid or duplicate identifier.", name));
            if (string.IsNullOrWhiteSpace(tweak.Name))
                errors.Add(L.T("{0}: name missing.", name));
            if (tweak.Values.Count == 0)
                errors.Add(L.T("{0}: no registry value.", name));
            foreach (var value in tweak.Values)
            {
                if (RegistryPath.TryParse(value.Key, out _, out _) is false
                    || (value.RestoreDeleteKey is { } key && !RegistryPath.TryParse(key, out _, out _)))
                    errors.Add(L.T("{0}: registry key must start with HKCU\\ or HKLM\\.", name));
                if (value.Type is not ("dword" or "string"))
                    errors.Add(L.T("{0}: registry type must be dword or string.", name));
                else if (value.Type == "dword" && (!int.TryParse(value.Value, out _)
                                                    || (value.Restore is { } restore && !int.TryParse(restore, out _))))
                    errors.Add(L.T("{0}: dword value must be an integer.", name));
            }
        }
        return errors;
    }

    private static void CheckMode(string mode, string name, List<string> errors)
    {
        if (mode is not (Modes.Auto or Modes.Confirm))
            errors.Add(L.T("{0}: mode must be auto or confirm.", name));
    }

    private static void CheckSource(ProgramEntry program, string name, List<string> errors)
    {
        if (!Sources.All.Contains(program.Source))
        {
            errors.Add(L.T("{0}: unknown source {1}.", name, program.Source));
            return;
        }
        void Require(bool present, string field)
        {
            if (!present)
                errors.Add(L.T("{0}: field {1} required for source {2}.", name, field, program.Source));
        }
        var detect = program.Detect;
        var hasDetection = detect is not null && (detect.DisplayName ?? detect.File ?? detect.Command ?? detect.WingetId) is not null;
        switch (program.Source)
        {
            case Sources.Winget:
            case Sources.Chocolatey:
                Require(!string.IsNullOrWhiteSpace(program.Package), "package");
                Require(program.Source == Sources.Winget || hasDetection, "detect");
                break;
            case Sources.MsStore:
                Require(!string.IsNullOrWhiteSpace(program.Package), "package");
                Require(detect?.WingetId is not null, "detect.winget_id");
                break;
            case Sources.GitHub:
                Require(program.Package?.Count(c => c == '/') == 1, "package");
                Require(program.AssetPattern is not null, "asset_pattern");
                Require(hasDetection, "detect");
                break;
            case Sources.Zip:
                Require(program.Url is not null, "url");
                Require(program.InstallDir is not null, "install_dir");
                Require(hasDetection, "detect");
                break;
            case Sources.Download:
                Require(program.Url is not null || program.Latest?.Url is not null, "url");
                Require(hasDetection, "detect");
                break;
            case Sources.Command:
                Require(program.InstallCommand is not null, "install_command");
                Require(hasDetection, "detect");
                break;
        }
        if (program.Verify is { } verify && !Verifications.All.Contains(verify))
            errors.Add(L.T("{0}: unknown verification {1}.", name, verify));
        if (program.VerifyOrDefault == Verifications.Sha256 && program.Sha256 is null && program.ChecksumUrl is null)
            errors.Add(L.T("{0}: field {1} required for source {2}.", name, "sha256", program.Source));
        if (program.VerifyOrDefault == Verifications.Origin && program.Source != Sources.GitHub
            && program.Url?.StartsWith(ProfileStore.ProfileScheme, StringComparison.Ordinal) != true)
            errors.Add(L.T("{0}: origin verification is only for the profile repository or a GitHub release.", name));
    }

    private static void CheckPatterns(ProgramEntry program, string name, List<string> errors)
    {
        foreach (var pattern in new[]
                 {
                     program.AssetPattern, program.InstallerPattern, program.Detect?.DisplayName, program.Detect?.WingetId,
                     program.Detect?.Regex, program.Latest?.Regex,
                 })
        {
            if (pattern is null)
                continue;
            try
            {
                _ = new Regex(pattern);
            }
            catch (ArgumentException)
            {
                errors.Add(L.T("{0}: invalid pattern {1}.", name, pattern));
            }
        }
    }
}
