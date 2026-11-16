using UtilitiesManager.Profiles;

namespace UtilitiesManager.Tests;

/// <summary>Lecture et contrôle du profil : un profil invalide n'est jamais appliqué.</summary>
public class ProfileTests
{
    private const string Json = """
        {
          "schema": 1,
          "defaults": { "mode": "confirm", "min_age_days": 3 },
          "programs": [
            { "id": "7zip", "name": "7-Zip", "source": "winget", "package": "7zip.7zip", "mode": "auto" },
            { "id": "outil", "name": "Outil", "source": "github", "package": "editeur/outil",
              "asset_pattern": "outil-.*\\.zip", "depends_on": "7zip",
              "detect": { "file": "%LOCALAPPDATA%\\Outil\\outil.exe" } }
          ],
          "tweaks": [
            { "id": "menu-classique", "name": "Menu contextuel classique", "restart_explorer": true,
              "values": [ { "key": "HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32",
                            "name": "", "type": "string", "value": "",
                            "restore_delete_key": "HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" } ] }
          ]
        }
        """;

    [Fact]
    public void ValidProfileIsReadAndSurvivesRoundTrip()
    {
        var (profile, errors) = ProfileValidator.Read(Json);
        Assert.Empty(errors);
        Assert.NotNull(profile);
        Assert.Equal(3, profile.Defaults.MinAgeDays);
        Assert.Equal("auto", profile.Find("7zip")!.ModeOr(profile.Defaults));
        Assert.Equal("confirm", profile.Find("outil")!.ModeOr(profile.Defaults));
        Assert.Equal(profile.ToJson(), profile.Clone().ToJson());
        Assert.Contains("\"asset_pattern\"", profile.ToJson());
    }

    [Fact]
    public void UnreadableJsonGivesAnErrorAndNoProfile()
    {
        var (profile, errors) = ProfileValidator.Read("{ \"programs\": [ ");
        Assert.Null(profile);
        Assert.StartsWith("Profil illisible", Assert.Single(errors));
    }

    [Fact]
    public void InvalidProfileListsEveryError()
    {
        var profile = Profile.Parse(Json);
        profile.Programs.Add(Fixtures.Winget("7zip"));                                          // identifiant en double
        profile.Programs.Add(new ProgramEntry { Id = "Majuscule", Name = "X", Source = "winget", Package = "a.b" });
        profile.Programs.Add(new ProgramEntry { Id = "sans-paquet", Name = "Y", Source = "winget" });
        profile.Programs.Add(new ProgramEntry { Id = "store", Name = "Z", Source = "msstore", Package = "9N" });
        profile.Programs.Add(new ProgramEntry { Id = "dep", Name = "D", Source = "winget", Package = "c.d", DependsOn = "absent" });
        profile.Programs.Add(new ProgramEntry { Id = "regex", Name = "R", Source = "winget", Package = "e.f", Mode = "jamais",
                                                Detect = new Detection { Regex = "(" } });
        profile.Tweaks[0].Values[0].Key = @"HKXX\Software";

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains("7zip : identifiant employé deux fois.", errors);
        Assert.Contains("Majuscule : identifiant non valide (minuscules, chiffres, point, tiret).", errors);
        Assert.Contains("sans-paquet : champ package obligatoire pour la source winget.", errors);
        Assert.Contains("store : champ detect.winget_id obligatoire pour la source msstore.", errors);
        Assert.Contains("dep : dépendance absent inconnue.", errors);
        Assert.Contains("regex : le mode doit être auto ou confirm.", errors);
        Assert.Contains(errors, e => e.StartsWith("regex : motif"));
        Assert.Contains(@"menu-classique : la clé de registre doit commencer par HKCU\ ou HKLM\.", errors);
    }

    [Fact]
    public async Task ProfileRepositoryBelongsToTheSignedInAccountAndLocalCopyServesWithoutIt()
    {
        var paths = Fixtures.NewPaths();
        var login = "";
        var store = new ProfileStore(paths, () => login, _ => throw new InvalidOperationException("aucune requête sans compte"));

        Assert.Null(store.Repository);
        Assert.Empty(store.Import(WriteJson(paths)));
        var load = await store.LoadAsync();
        Assert.True(load.FromCache);
        Assert.NotNull(load.Profile);
        Assert.NotNull(load.Error);

        login = "utilisateur";
        Assert.Equal("utilisateur/UtilitiesProfile", store.Repository);
    }

    private static string WriteJson(AppPaths paths)
    {
        var file = Path.Combine(paths.TempDir, "profile.json");
        Directory.CreateDirectory(paths.TempDir);
        File.WriteAllText(file, Json);
        return file;
    }
}
