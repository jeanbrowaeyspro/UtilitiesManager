using System.Text.RegularExpressions;
using UtilitiesManager.GitHub;
using UtilitiesManager.Launcher;
using UtilitiesManager.Updates;
using UtilitiesManager.ViewModels;

namespace UtilitiesManager.Tests;

/// <summary>L'outil lui-même : traductions, installation sur le PC, mise à jour, lanceur.</summary>
public partial class ToolTests
{
    [GeneratedRegex("""L\.T\(\s*\$?"((?:[^"\\]|\\.)*)"|\{local:Tr '([^']*)'\}""")]
    private static partial Regex TextKey();

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void EveryTextHasAFrenchTranslationWithTheSamePlaceholders()
    {
        var src = Path.Combine(Fixtures.RepositoryRoot(), "src");
        var keys = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => TextKey().Matches(File.ReadAllText(f)))
            .Select(m => Regex.Unescape(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
            .Distinct().ToList();

        Assert.True(keys.Count > 200);
        Assert.Empty(keys.Where(k => !FrenchCatalog.Texts.ContainsKey(k)));
        Assert.Empty(FrenchCatalog.Texts.Keys.Except(keys));
        Assert.Empty(FrenchCatalog.Texts.Where(t =>
            !Placeholder().Matches(t.Key).Select(m => m.Value).Order().SequenceEqual(Placeholder().Matches(t.Value).Select(m => m.Value).Order())));
    }

    [Fact]
    public void SetupWritesStartupAndShortcutsThenUninstallRemovesThem()
    {
        var paths = Fixtures.NewPaths();
        var registry = new FakeRegistry();
        var setup = new Setup(paths, registry, new FakeShortcuts());
        var launcher = Path.Combine(Fixtures.NewFolder(), AppPaths.LauncherFileName);
        File.WriteAllText(launcher, "lanceur");

        setup.Install(launcher, new AppSettings { DesktopShortcut = true });

        Assert.True(setup.IsInstalled);
        Assert.Equal($"\"{paths.InstalledLauncher}\" {Switches.Startup}",
                     registry.GetValue(RegistryRoot.CurrentUser, Setup.RunPath, Setup.RunValue));
        Assert.True(File.Exists(paths.DesktopShortcut) && File.Exists(paths.StartMenuShortcut));

        setup.Apply(new AppSettings { RunAtStartup = false, StartMenuShortcut = false });
        Assert.Null(registry.GetValue(RegistryRoot.CurrentUser, Setup.RunPath, Setup.RunValue));
        Assert.False(File.Exists(paths.DesktopShortcut) || File.Exists(paths.StartMenuShortcut));
        Assert.False(AppSettings.Load(paths.SettingsFile).RunAtStartup);

        Assert.NotNull(setup.Uninstall());
        Assert.False(File.Exists(paths.SettingsFile));
    }

    private static GitHubRelease Release(string tag, bool prerelease = false, bool complete = true) =>
        new(tag, null, null, false, prerelease, null, null,
            complete
                ? [new GitHubAsset("UtilitiesManager.exe", 1, "u"), new GitHubAsset("UtilitiesManager.exe.sha256", 1, "s")]
                : [new GitHubAsset("UtilitiesManager.exe", 1, "u")]);

    [Fact]
    public void UpdateIsTheHighestCompleteReleaseAndTestVersionsAreOptIn()
    {
        var current = SemVersion.Parse("1.0.0");
        GitHubRelease[] releases = [Release("v1.1.0"), Release("v1.2.0", complete: false), Release("v1.1.1"), Release("v1.3.0-beta", prerelease: true)];

        Assert.Equal("1.1.0", UpdateService.SelectUpdate(releases, current, includePrereleases: false)!.Version.ToString());
        Assert.Equal("1.3.0-beta", UpdateService.SelectUpdate(releases, current, includePrereleases: true)!.Version.ToString());
        Assert.Null(UpdateService.SelectUpdate(releases, SemVersion.Parse("1.1.0"), includePrereleases: false));
        Assert.True(SemVersion.Parse("0.0.1").IsTestVersion);
    }

    [Fact]
    public void UpdateCheckAtMostOncePerHourAndChecksumIsRead()
    {
        var now = DateTimeOffset.Now;
        Assert.True(UpdateService.IsCheckDue(null, now));
        Assert.False(UpdateService.IsCheckDue(now.AddMinutes(-59), now));
        Assert.True(UpdateService.IsCheckDue(now.AddHours(-1), now));
        var hash = new string('A', 64);
        Assert.Equal(hash.ToLowerInvariant(), UpdateService.ParseChecksum($"{hash}  UtilitiesManager.exe\n"));
        Assert.Null(UpdateService.ParseChecksum("pas une empreinte"));
    }

    [Fact]
    public void ChangelogOfTheRepositoryIsReadable()
    {
        var entries = Changelog.Parse(File.ReadAllText(Path.Combine(Fixtures.RepositoryRoot(), "CHANGELOG.md")));
        var newest = entries[0];
        Assert.Equal(AppInfo.Version, newest.Version.ToString());
        Assert.NotEmpty(newest.Sections.SelectMany(s => s.Lines));
        Assert.Equal([newest], Changelog.Between(entries, null, newest.Version));
        Assert.Empty(Changelog.Between(entries, newest.Version, newest.Version));
    }

    [Fact]
    public void LauncherPassesItsPathAndArgumentsToTheApplication()
    {
        Assert.Equal("/demarrage", CommandLine.Arguments("\"C:\\Program Files\\UtilitiesManager.exe\"  /demarrage"));
        Assert.Equal($"{Switches.Launcher} \"C:\\U.exe\"", CommandLine.ForApp("C:\\U.exe", "U.exe"));
    }
}
