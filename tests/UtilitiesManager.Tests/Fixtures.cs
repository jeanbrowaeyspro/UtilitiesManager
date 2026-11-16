using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using UtilitiesManager.ViewModels;

// Les tests partagent des variables d'environnement et le fil STA de WPF : un par un.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UtilitiesManager.Tests;

/// <summary>Dossiers temporaires et faux services communs aux tests.</summary>
public static class Fixtures
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "UtilitiesManagerTests");

    /// <summary>
    /// Les vrais dossiers de Jean ne sont jamais lus ni écrits par les tests. Textes
    /// attendus en français, quelle que soit la langue de Windows.
    /// </summary>
    [ModuleInitializer]
    internal static void IsolateEnvironment()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;   // schéma pack:// connu sans Application
        IsolateFolders();
        L.Use(L.French);
    }

    private static void IsolateFolders()
    {
        foreach (var variable in new[] { "APPDATA", "LOCALAPPDATA" })
        {
            var folder = Path.Combine(Root, "env", variable.ToLowerInvariant());
            Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable(variable, folder);
        }
    }

    /// <summary>Nouveau dossier vide propre à un test.</summary>
    public static string NewFolder([CallerMemberName] string name = "")
    {
        var folder = Path.Combine(Root, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Jeu complet d'emplacements dans un dossier temporaire.</summary>
    public static AppPaths NewPaths([CallerMemberName] string name = "")
    {
        var root = NewFolder(name);
        return new AppPaths(
            InstallDir: Path.Combine(root, "local", "Programs", AppPaths.AppName),
            SettingsFile: Path.Combine(root, "roaming", AppPaths.AppName, "settings.json"),
            DataDir: Path.Combine(root, "local", AppPaths.AppName),
            DesktopDir: Path.Combine(root, "Bureau"),
            StartMenuDir: Path.Combine(root, "Programmes"),
            TempDir: Path.Combine(root, "temp"));
    }

    /// <summary>Réponse de l'API GitHub pour une release sans UtilitiesManager.exe.</summary>
    public const string ReleaseJson = """
        {
          "tag_name": "v1.3.0",
          "name": "Autre outil 1.3.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "Autre-1.3.0.zip",
              "browser_download_url": "https://github.com/exemple/autre/releases/download/v1.3.0/Autre-1.3.0.zip" }
          ]
        }
        """;

    /// <summary>Exécute une action sur un fil STA (exigé par WPF) et propage ses exceptions.</summary>
    public static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exc) { error = exc; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new AggregateException(error);
    }
}

/// <summary>Registre en mémoire.</summary>
public sealed class FakeRegistry : IRegistryStore
{
    private readonly Dictionary<(RegistryRoot, string), Dictionary<string, string>> _keys = [];

    private static (RegistryRoot, string) Id(RegistryRoot root, string path) => (root, path.ToLowerInvariant());

    public IReadOnlyList<string> SubKeyNames(RegistryRoot root, string path)
    {
        var prefix = path.ToLowerInvariant() + @"\";
        return _keys.Keys.Where(k => k.Item1 == root && k.Item2.StartsWith(prefix))
                    .Select(k => k.Item2[prefix.Length..].Split('\\')[0]).Distinct().ToList();
    }

    public string? GetString(RegistryRoot root, string path, string name) =>
        _keys.TryGetValue(Id(root, path), out var values) && values.TryGetValue(name, out var value) ? value : null;

    public void SetString(RegistryRoot root, string path, string name, string value)
    {
        if (!_keys.TryGetValue(Id(root, path), out var values))
            _keys[Id(root, path)] = values = [];
        values[name] = value;
    }

    public void DeleteValue(RegistryRoot root, string path, string name)
    {
        if (_keys.TryGetValue(Id(root, path), out var values))
            values.Remove(name);
    }

    /// <summary>Entrée de désinstallation de FileZilla, telle que l'écrit son installeur NSIS.</summary>
    public FakeRegistry WithFileZilla(string version, string? location, RegistryRoot root = RegistryRoot.LocalMachine32)
    {
        var path = $@"{InstalledFileZilla.UninstallPath}\{InstalledFileZilla.KeyName}";
        SetString(root, path, "DisplayName", $"FileZilla {version}");
        SetString(root, path, "DisplayVersion", version);
        if (location is not null)
            SetString(root, path, "InstallLocation", location);
        return this;
    }
}

/// <summary>Raccourcis : un fichier texte contenant la cible.</summary>
public sealed class FakeShortcuts : IShortcutWriter
{
    public void Create(string shortcutPath, string target, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        File.WriteAllText(shortcutPath, target);
    }
}

/// <summary>FileZilla simulé : mémorise les fermetures et relances.</summary>
public sealed class FakeFileZilla(string? runningPath) : IFileZillaProcess
{
    public string? RunningPath { get; private set; } = runningPath;
    public List<string> Events { get; } = [];

    public string? Close()
    {
        Events.Add("close");
        var path = RunningPath;
        RunningPath = null;
        return path;
    }

    public bool IsRunning => RunningPath is not null;

    public void Start(string exePath)
    {
        Events.Add($"start {exePath}");
        RunningPath = exePath;
    }

    /// <summary>FileZilla relancé hors de UtilitiesManager (par son installeur).</summary>
    public void StartedElsewhere(string exePath) => RunningPath = exePath;
}

/// <summary>Programmes externes simulés : mémorise les lancements, renvoie un code donné. Rien n'est exécuté.</summary>
public sealed class FakeRunner(int exitCode = 0) : IProcessRunner
{
    public List<ProcessStartInfo> Runs { get; } = [];
    /// <summary>Contenu de l'installeur au moment de son "lancement".</summary>
    public List<string> Installers { get; } = [];
    /// <summary>Action exécutée pendant le "programme" (mise à jour du registre…).</summary>
    public Action? During { get; init; }

    /// <summary>Fin du "programme" : immédiate si null, sinon quand la tâche se termine.</summary>
    public Task? Until { get; init; }

    public async Task<int> RunAsync(ProcessStartInfo start, CancellationToken cancel = default)
    {
        Runs.Add(start);
        if (File.Exists(start.FileName))
            Installers.Add(File.ReadAllText(start.FileName));
        During?.Invoke();
        if (Until is not null)
            await Until;
        return exitCode;
    }

    public void Start(ProcessStartInfo start) => Runs.Add(start);
}

/// <summary>
/// Chocolatey simulé : version donnée par la "redirection", paquet nupkg écrit comme un
/// vrai zip contenant un faux installeur (texte, jamais exécuté).
/// </summary>
public sealed class FakeSource : IFileZillaSource
{
    public const string FakeInstaller = "faux installeur FileZilla";

    private readonly Func<FileZillaRelease>[] _answers;

    public FakeSource(params Func<FileZillaRelease>[] answers) => _answers = answers;

    public int Calls { get; private set; }
    public List<(string Url, string Destination)> Downloads { get; } = [];
    /// <summary>Nom de l'installeur placé dans le paquet (null : version annoncée).</summary>
    public string? InstallerEntry { get; init; }
    /// <summary>Erreur levée par le téléchargement.</summary>
    public Exception? DownloadError { get; init; }

    public Task<FileZillaRelease> GetLatestAsync(CancellationToken cancel = default) =>
        Task.FromResult(_answers[Math.Min(Calls++, _answers.Length - 1)]());

    public Task DownloadAsync(FileZillaRelease release, string destination, CancellationToken cancel = default)
    {
        Downloads.Add((release.PackageUrl, destination));
        if (DownloadError is not null)
            throw DownloadError;
        WritePackage(destination, InstallerEntry ?? $"tools/FileZilla_{VersionText.Display(release.Version)}_win64-setup.exe");
        return Task.CompletedTask;
    }

    /// <summary>Paquet nupkg minimal : spécification, script Chocolatey et installeur.</summary>
    public static void WritePackage(string destination, string installerEntry)
    {
        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        Write(zip, "filezilla.nuspec", "<package />");
        Write(zip, "tools/chocolateyInstall.ps1", "# installation");
        Write(zip, installerEntry, FakeInstaller);
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    public static FileZillaRelease Latest() =>
        ChocolateySource.ParseRedirect(new Uri("https://packages.chocolatey.org/filezilla.3.72.0.nupkg"));

    public static FileZillaRelease Offline() => throw new HttpRequestException("réseau absent");

    public static FileZillaRelease TooManyRequests() =>
        throw new HttpRequestException("Chocolatey limite le nombre de requêtes, réessayer plus tard.", null,
                                       System.Net.HttpStatusCode.TooManyRequests);
}

/// <summary>Contrôle de signature simulé, avec le résultat donné.</summary>
public sealed class FakeSignature(bool trusted = true, string? signer = SignatureCheck.ExpectedSigner) : ISignatureVerifier
{
    public List<string> Checked { get; } = [];

    public SignatureCheck Verify(string path)
    {
        Checked.Add(path);
        return new SignatureCheck(trusted, signer, signer is null ? null : "1FF3B857AB5501E21CFEC7AEE8685172AC56DAD2",
                                  trusted ? null : "0x800B0100");
    }
}

/// <summary>GitHub simulé : réponses successives (une exception = réseau absent).</summary>
public sealed class FakeReleases(params Func<ReleaseInfo?>[] answers) : IReleaseSource
{
    public int Calls { get; private set; }
    public List<(string Url, string Destination)> Downloads { get; } = [];
    /// <summary>Contenu écrit par le "téléchargement".</summary>
    public string Content { get; init; } = "lanceur";

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancel = default) =>
        Task.FromResult(answers[Math.Min(Calls++, answers.Length - 1)]());

    public Task DownloadAsync(string url, string destination, CancellationToken cancel = default)
    {
        Downloads.Add((url, destination));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, Content);
        return Task.CompletedTask;
    }

    public static ReleaseInfo? Offline() => throw new HttpRequestException("réseau absent");
}

/// <summary>Messages simulés : réponse fixée aux confirmations, textes et boutons mémorisés.</summary>
public sealed class FakeDialogs(bool answer = true) : IDialogs
{
    public List<string> Shown { get; } = [];
    public List<(string? Yes, string? No)> Buttons { get; } = [];

    public bool Confirm(string text, string? yes = null, string? no = null)
    {
        Shown.Add(text);
        Buttons.Add((yes, no));
        return answer;
    }

    public void Info(string text) => Shown.Add(text);

    public void Error(string text) => Shown.Add(text);
}
