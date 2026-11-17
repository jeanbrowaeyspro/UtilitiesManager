using System.Text;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;

namespace UtilitiesManager.Tests;

/// <summary>Lectures sans réseau ni programme lancé : winget, pages, empreintes, état d'un logiciel.</summary>
public class SoftwareTests
{
    // Sortie réelle de "winget list" : animation de progression avant un retour chariot, colonne "Disponible" vide ou remplie.
    private const string WingetList =
        "   - \r   \\ \r" +
        "Nom                                 ID                              Version        Disponible   Source\r\n" +
        "--------------------------------------------------------------------------------------------------------\r\n" +
        "7-Zip 24.09 (x64)                   7zip.7zip                       24.09          25.01        winget\r\n" +
        "Notepad++ (64-bit x64)              Notepad++.Notepad++             8.7.1                       winget\r\n" +
        "WhatsApp                            9NKSQGP7F2NH                    2.2450.6.0                  msstore\r\n" +
        "Pilote Intel                        ARP\\Machine\\X64\\{1234}           1.0\r\n";

    [Fact]
    public void WingetListIsReadByColumnPosition()
    {
        var packages = Winget.ParseList(WingetList);

        Assert.Equal(4, packages.Count);
        Assert.Equal(new WingetPackage("7-Zip 24.09 (x64)", "7zip.7zip", "24.09", "25.01", "winget"), packages[0]);
        Assert.Equal("", packages[1].Available);
        Assert.Equal("msstore", packages[2].Source);
        var store = new ProgramEntry { Id = "whatsapp", Name = "WhatsApp", Source = Sources.MsStore, Package = "9NKSQGP7F2NH" };
        Assert.Same(packages[2], Winget.Match(store, packages));
        var pattern = new ProgramEntry { Id = "intel", Name = "Intel", Package = "Intel.Pilote", Detect = new Detection { WingetId = @"^ARP\\Machine" } };
        Assert.Same(packages[3], Winget.Match(pattern, packages));
    }

    [Fact]
    public void WingetArgumentsFollowTheProgramOptions()
    {
        var program = new ProgramEntry { Id = "store", Source = Sources.MsStore, Package = "9N", SilentArgs = "--scope machine" };
        Assert.Equal("upgrade --id 9N --exact --source msstore --silent --scope machine "
                     + "--accept-source-agreements --accept-package-agreements --disable-interactivity",
                     Winget.InstallArguments(program, upgrade: true));
        program.Interactive = true;
        Assert.StartsWith("install --id 9N --exact --source msstore --version \"1.2\" --force --interactive",
                          Winget.InstallArguments(program, upgrade: true, version: "1.2"));
    }

    [Fact]
    public void PageGivesVersionAndAbsoluteLink()
    {
        const string page = "https://www.exemple.fr/telechargements/";
        const string html = """<a href="../files/Outil_Setup_4.12.3.exe?x=1&amp;y=2">Outil 4.12.3</a>""";

        var latest = LatestResolver.ParsePage(page, html, @"href=""(?<url>[^""]*Outil_Setup_(\d+(?:\.\d+)+)\.exe[^""]*)""");

        Assert.NotNull(latest);
        Assert.Equal("4.12.3", latest.Version);
        Assert.Equal("https://www.exemple.fr/files/Outil_Setup_4.12.3.exe?x=1&y=2", latest.DownloadUrl);
        Assert.Null(LatestResolver.ParsePage(page, html, "absent"));
    }

    [Fact]
    public void ChecksumFileGivesTheHashOfTheRightFile()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = $"{a}  autre.zip\n{b} *Outil-1.2.zip\r\n";
        Assert.Equal(b, Integrity.FindHash(sums, "outil-1.2.zip"));
        Assert.Null(Integrity.FindHash(sums, "absent.zip"));
    }

    [Fact]
    public void MsiRepairCommandBecomesSilentUninstall()
    {
        Assert.Equal("msiexec.exe /X{AB-12} /qn /norestart", InstallService.QuietUninstall("MsiExec.exe /I{AB-12}"));
        Assert.Equal("\"C:\\Outil\\uninst.exe\"", InstallService.QuietUninstall("\"C:\\Outil\\uninst.exe\""));
    }

    [Fact]
    public void DependencyIsInstalledFirst()
    {
        var java = Fixtures.Winget("java");
        var outil = Fixtures.Winget("outil");
        outil.DependsOn = "java";
        Assert.Equal(["java", "outil"], InstallService.Order([outil, java]).Select(p => p.Id));
    }

    [Fact]
    public async Task BatchPausesResumesAndStopsThroughTheElevatedProtocol()
    {
        var control = new BatchControl();
        ElevatedProtocol.Apply(ElevatedProtocol.Pause, control);
        var waiting = control.WaitIfPausedAsync();
        Assert.False(waiting.IsCompleted);
        ElevatedProtocol.Apply(ElevatedProtocol.Order(control) == ElevatedProtocol.Pause ? ElevatedProtocol.Resume : "", control);
        await waiting.WaitAsync(TimeSpan.FromSeconds(1));
        control.Pause();
        ElevatedProtocol.Apply(ElevatedProtocol.Stop, control);
        Assert.True(control.WaitIfPausedAsync().IsCompleted);
        Assert.True(control.StopToken.IsCancellationRequested);
        Assert.Equal(ElevatedProtocol.Stop, ElevatedProtocol.Order(control));

        var removed = new InstalledInfo(true, "1.2", "\"C:\\Outil\\uninst.exe\"");
        var line = ElevatedProtocol.Write(new ElevatedRequest(new Profile().ToJson(), [new(Fixtures.Winget("outil"), removed)]));
        Assert.DoesNotContain('\n', line);
        var request = ElevatedProtocol.Read<ElevatedRequest>(line);
        Assert.Equal("outil", request.Items[0].Program.Id);
        Assert.Equal(removed, request.Items[0].Removed);
    }

    [Fact]
    public void OnlyDesktopIconsAddedSinceTheSnapshotAreRemoved()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "Ancien.lnk"), "");
            var desktop = new DesktopIcons([folder]);
            var before = desktop.Snapshot();
            File.WriteAllText(Path.Combine(folder, "Nouveau.lnk"), "");
            File.WriteAllText(Path.Combine(folder, "Site.url"), "");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
            Assert.Equal(["Nouveau", "Site"], desktop.RemoveAdded(before).Select(r => r.Name).Order());
            Assert.Equal(["Ancien.lnk", "notes.txt"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void StateFollowsInstallationAgeAndBlockedVersion()
    {
        var defaults = new ProfileDefaults { Mode = Modes.Confirm, MinAgeDays = 5 };
        var state = new MachineState();
        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        var program = Fixtures.Winget("outil");
        var installed = new InstalledInfo(true, "1.0");
        var latest = new LatestInfo("1.1");

        ProgramState Evaluate(InstalledInfo info, LatestInfo? newest, bool newer, DateTimeOffset at) =>
            CheckEngine.Evaluate(program, info, newest, newer, null, defaults, state, at).State;

        Assert.Equal(ProgramState.Missing, Evaluate(InstalledInfo.Absent, latest, true, now));
        // Absent mais dépendance d'un autre logiciel : installé avec lui, pas proposé seul.
        var dependency = CheckEngine.Evaluate(program, InstalledInfo.Absent, latest, true, null, defaults, state, now, ["Microsoft 365"]);
        Assert.Equal(ProgramState.Dependency, dependency.State);
        Assert.Equal("Microsoft 365", dependency.Message);
        Assert.False(dependency.NeedsAction);
        Assert.Equal(ProgramState.UpToDate, Evaluate(installed, latest, false, now));
        // Version vue pour la première fois : attendue cinq jours, puis proposée.
        Assert.Equal(ProgramState.Waiting, Evaluate(installed, latest, true, now));
        Assert.Equal(ProgramState.Waiting, Evaluate(installed, latest, true, now.AddDays(4)));
        Assert.Equal(ProgramState.UpdateAvailable, Evaluate(installed, latest, true, now.AddDays(5)));
        // Date de publication connue (GitHub) : elle prime sur la première vue.
        Assert.Equal(ProgramState.UpdateAvailable, Evaluate(installed, latest with { Published = now.AddDays(-6) }, true, now));
        program.BlockedVersion = "1.0";
        Assert.Equal(ProgramState.Blocked, Evaluate(installed, latest, true, now.AddDays(10)));
    }

    [Fact]
    public void AutoModeUpdatesAloneOtherActionsArePending()
    {
        var defaults = new ProfileDefaults { Mode = Modes.Confirm };
        var auto = Fixtures.Winget("auto");
        auto.Mode = Modes.Auto;
        var present = new InstalledInfo(true, "1");
        var result = new CheckResult(
        [
            new ProgramStatus(auto, present, null, ProgramState.UpdateAvailable),
            new ProgramStatus(Fixtures.Winget("confirm"), present, null, ProgramState.UpdateAvailable),
            new ProgramStatus(Fixtures.Winget("absent"), InstalledInfo.Absent, null, ProgramState.Missing),
            new ProgramStatus(Fixtures.Winget("retire"), present, null, ProgramState.Removed),
            new ProgramStatus(Fixtures.Winget("attente"), present, null, ProgramState.Waiting),
        ], null);

        Assert.Equal(["auto"], result.AutoUpdates(defaults).Select(p => p.Program.Id));
        Assert.Equal(["confirm", "absent", "retire"], result.Pending(defaults).Select(p => p.Program.Id));
    }

    [Fact]
    public void OnlyOnePreviousVersionIsKept()
    {
        var cache = new VersionCache(Fixtures.NewFolder());
        var folder = Fixtures.NewFolder();
        string Installer(string version)
        {
            var file = Path.Combine(folder, $"setup-{version}.exe");
            File.WriteAllText(file, version);
            return file;
        }

        cache.Push("outil", "1", Installer("1"));
        cache.Push("outil", "2", Installer("2"));
        cache.Push("outil", "2", Installer("2"));   // réinstallation : la précédente reste la 1
        Assert.Equal("1", cache.PreviousOf("outil")!.Version);
        cache.Push("outil", "3", Installer("3"));
        var previous = cache.PreviousOf("outil")!;
        Assert.Equal("2", previous.Version);
        Assert.Equal("2", File.ReadAllText(previous.File!));

        cache.Reverted("outil");
        Assert.Null(cache.PreviousOf("outil"));
        Assert.Equal("2", File.ReadAllText(cache.CurrentOf("outil")!.File!));
    }

    [Fact]
    public void IniValueIsChangedWithoutTouchingTheRest()
    {
        const string text = "; réglages\r\n[General]\r\nLangue = fr\r\nCheckUpdates=1\r\n[Autre]\r\nCheckUpdates=1\r\n";

        Assert.Equal(text.Replace("CheckUpdates=1\r\n[Autre]", "CheckUpdates=0\r\n[Autre]"),
                     IniFile.With(text, "general", "checkupdates", "0"));
        Assert.Equal(text.Replace("[Autre]\r\n", "[Autre]\r\nNouvelle=oui\r\n"), IniFile.With(text, "Autre", "Nouvelle", "oui"));
        Assert.Equal("a=1\n[Updates]\nauto=0\n", IniFile.With("a=1", "Updates", "auto", "0"));
        Assert.Equal("fr", IniFile.Read(text, "General", "Langue"));
    }

    [Fact]
    public void Utf16IniFileKeepsItsEncoding()
    {
        var file = Path.Combine(Fixtures.NewFolder(), "outil.ini");
        File.WriteAllText(file, "[Options]\r\nMaj=1\r\nÉtat=été\r\n", Encoding.Unicode);

        Assert.True(IniFile.Apply(file, "Options", "Maj", "0"));
        Assert.False(IniFile.Apply(file, "Options", "Maj", "0"));

        var bytes = File.ReadAllBytes(file);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);
        Assert.Equal("[Options]\r\nMaj=0\r\nÉtat=été\r\n", Encoding.Unicode.GetString(bytes[2..]));
    }

    [Fact]
    public void WindowsSettingIsAppliedRestoredAndCapturedFromTheCatalog()
    {
        var registry = new FakeRegistry();
        var tweaks = new WindowsTweaks(registry);
        const string clsid = @"Software\Classes\CLSID\{86ca1aa0}";
        var tweak = new TweakEntry
        {
            Id = "menu", Name = "Menu",
            Values =
            [
                new RegistrySetting { Key = $@"HKCU\{clsid}\InprocServer32", Name = "", Type = "string", Value = "",
                                      RestoreDeleteKey = $@"HKCU\{clsid}" },
                new RegistrySetting { Key = @"HKCU\Software\Outil", Name = "Recherche", Type = "dword", Value = "0", Restore = "1" },
            ],
        };

        Assert.False(tweaks.IsApplied(tweak));
        tweaks.Apply(tweak);
        Assert.True(tweaks.IsApplied(tweak));
        Assert.Equal(0, registry.GetValue(RegistryRoot.CurrentUser, @"Software\Outil", "Recherche"));

        tweaks.Restore(tweak);
        Assert.False(tweaks.IsApplied(tweak));
        Assert.False(registry.HasKey(RegistryRoot.CurrentUser, $@"{clsid}\InprocServer32"));
        Assert.Equal(1, registry.GetValue(RegistryRoot.CurrentUser, @"Software\Outil", "Recherche"));

        // Catalogue (traductions contrôlées avec les autres textes) : valide, seuls les réglages posés sont relevés.
        var catalog = TweakCatalog.Load();
        Assert.Empty(ProfileValidator.Validate(new Profile { Tweaks = catalog }));
        tweaks.Apply(catalog.Single(c => c.Id == "dark-theme"));
        Assert.Equal(["dark-theme"], catalog.Where(tweaks.IsApplied).Select(c => c.Id));
    }
}
