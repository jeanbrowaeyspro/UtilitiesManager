using UtilitiesManager.GitHub;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;

namespace UtilitiesManager.Tests;

/// <summary>Relevé des logiciels de ce PC et sources proposées, sans réseau ni programme lancé.</summary>
public class ScanTests
{
    [Fact]
    public void ScanMergesWingetRegistryAndFolderAndLeavesOutTheProfile()
    {
        var registry = new FakeRegistry();
        void Program(string key, string name, string? publisher = null)
        {
            var path = $@"{Detector.UninstallKey}\{key}";
            registry.SetString(RegistryRoot.LocalMachine64, path, "DisplayName", name);
            registry.SetString(RegistryRoot.LocalMachine64, path, "UninstallString", "uninstall.exe");
            if (publisher is not null)
                registry.SetString(RegistryRoot.LocalMachine64, path, "Publisher", publisher);
        }
        Program("Notepad++", "Notepad++ (64-bit x64)", "Don Ho");
        Program("Pilote", "Pilote Intel");
        Program("Composant", "Composant système");
        registry.SetDword(RegistryRoot.LocalMachine64, $@"{Detector.UninstallKey}\Composant", "SystemComponent", 1);
        var winget = Winget.ParseList(
            "Nom                       ID                                                       Version     Disponible  Source\r\n" +
            "-----------------------------------------------------------------------------------------------------------------\r\n" +
            "7-Zip 24.09 (x64)         7zip.7zip                                                24.09                   winget\r\n" +
            "Notepad++ (64-bit x64)    Notepad++.Notepad++                                      8.7.1                   winget\r\n" +
            "WhatsApp                  9NKSQGP7F2NH                                             2.2450.6.0              msstore\r\n" +
            @"Photos Microsoft          MSIX\Microsoft.Windows.Photos_2026.1.0_x64__8wekyb3d8bbwe 2026.1.0" + "\r\n");
        var folder = Path.Combine(Fixtures.Root, "portables");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(Path.Combine(folder, "Outil"));
        File.WriteAllText(Path.Combine(folder, "Outil", "aide.exe"), "");
        File.WriteAllText(Path.Combine(folder, "Outil", "Outil.exe"), "");
        File.WriteAllText(Path.Combine(folder, "UtilitiesManager.exe"), "");
        var profile = new Profile { Programs = [Fixtures.Winget("7zip", "7zip.7zip")] };

        var found = new Inventory(registry).Scan(profile, winget, folder);

        Assert.Equal(["Notepad++ (64-bit x64)", "Outil", "Pilote Intel", "WhatsApp"], found.Select(f => f.Name));
        var notepad = found[0];
        Assert.True(notepad.IsSure);
        Assert.Equal("Don Ho", notepad.Publisher);
        Assert.Equal(Path.Combine(folder, "Outil", "Outil.exe"), found[1].Exe);
        Assert.False(found[2].IsSure);
        var store = SourceFinder.SureCandidate(found[3], profile)!.Entry;
        Assert.Equal(Sources.MsStore, store.Source);
        Assert.Equal("^9NKSQGP7F2NH$", store.Detect?.WingetId);
        Assert.Equal("7zip-2", SourceFinder.UniqueId("7zip", profile));
        Assert.Equal("notepad", SourceFinder.UniqueId("Notepad++", profile));
    }

    [Fact]
    public void ChocolateySearchAndGitHubReleaseGiveTheSourceFields()
    {
        var packages = Chocolatey.ParseSearch("""
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                  xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
              <entry><title>notepadplusplus.install</title>
                <m:properties><d:Title>Notepad++ (Install)</d:Title><d:Version>8.7.1</d:Version><d:PackageSize>5200000</d:PackageSize></m:properties></entry>
              <entry><title>notepadplusplus</title>
                <m:properties><d:Title>Notepad++</d:Title><d:Version>8.7.1</d:Version><d:PackageSize>4000</d:PackageSize></m:properties></entry>
            </feed>
            """);
        Assert.Equal(["notepadplusplus.install", "notepadplusplus"], packages.Select(p => p.Id));
        Assert.True(packages[0].EmbedsInstaller);
        Assert.False(packages[1].EmbedsInstaller);

        GitHubAsset Asset(string name) => new(name, 1000, "");
        var release = new GitHubRelease("v1.2.3", null, null, false, false, null, null,
            [Asset("app-1.2.3-arm64.exe"), Asset("app-1.2.3-x64.exe.sha256"), Asset("app-1.2.3-x64.exe"), Asset("app-1.2.3.tar.gz")]);
        var pattern = SourceFinder.AssetPattern(release);
        Assert.Equal(@"^app-v?[\d.]+-x64\.exe$", pattern);
        Assert.Matches(pattern!, "app-1.3.0-x64.exe");
        Assert.Null(SourceFinder.AssetPattern(release with { Assets = [Asset("app-1.2.3.tar.gz")] }));
    }
}
