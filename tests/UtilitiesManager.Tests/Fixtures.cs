using System.Diagnostics;
using System.Runtime.CompilerServices;
using UtilitiesManager.Profiles;
using UtilitiesManager.ViewModels;

// Les tests partagent des variables d'environnement : un par un.
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
        foreach (var variable in new[] { "APPDATA", "LOCALAPPDATA" })
        {
            var folder = Path.Combine(Root, "env", variable.ToLowerInvariant());
            Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable(variable, folder);
        }
        L.Use(L.French);
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

    /// <summary>Dossier racine du dépôt (celui qui contient CHANGELOG.md).</summary>
    public static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CHANGELOG.md")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("CHANGELOG.md");
    }

    /// <summary>Logiciel winget minimal, valide.</summary>
    public static ProgramEntry Winget(string id, string package = "Editeur.Logiciel") =>
        new() { Id = id, Name = id, Source = Sources.Winget, Package = package };
}

/// <summary>Registre en mémoire.</summary>
public sealed class FakeRegistry : IRegistryStore
{
    private readonly Dictionary<(RegistryRoot, string), Dictionary<string, object>> _keys = [];

    private static (RegistryRoot, string) Id(RegistryRoot root, string path) => (root, path.ToLowerInvariant());

    public IReadOnlyList<string> SubKeyNames(RegistryRoot root, string path)
    {
        var prefix = path.ToLowerInvariant() + @"\";
        return _keys.Keys.Where(k => k.Item1 == root && k.Item2.StartsWith(prefix))
                    .Select(k => k.Item2[prefix.Length..].Split('\\')[0]).Distinct().ToList();
    }

    public object? GetValue(RegistryRoot root, string path, string name) =>
        _keys.TryGetValue(Id(root, path), out var values) && values.TryGetValue(name, out var value) ? value : null;

    public bool HasKey(RegistryRoot root, string path) => _keys.ContainsKey(Id(root, path));

    /// <summary>Écriture dans HKLM refusée, comme sans les droits administrateur.</summary>
    public bool LocalMachineReadOnly { get; init; }

    private void Set(RegistryRoot root, string path, string name, object value)
    {
        if (LocalMachineReadOnly && root != RegistryRoot.CurrentUser)
            throw new UnauthorizedAccessException("HKLM");
        if (!_keys.TryGetValue(Id(root, path), out var values))
            _keys[Id(root, path)] = values = [];
        values[name] = value;
    }

    public void SetString(RegistryRoot root, string path, string name, string value) => Set(root, path, name, value);

    public void SetDword(RegistryRoot root, string path, string name, int value) => Set(root, path, name, value);

    public void DeleteValue(RegistryRoot root, string path, string name)
    {
        if (_keys.TryGetValue(Id(root, path), out var values))
            values.Remove(name);
    }

    public void DeleteKey(RegistryRoot root, string path)
    {
        var prefix = path.ToLowerInvariant();
        foreach (var key in _keys.Keys.Where(k => k.Item1 == root && (k.Item2 == prefix || k.Item2.StartsWith(prefix + @"\"))).ToList())
            _keys.Remove(key);
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

/// <summary>Programmes externes simulés : mémorise les lancements, renvoie une sortie donnée. Rien n'est exécuté.</summary>
public sealed class FakeRunner(int exitCode = 0, string output = "") : IProcessRunner
{
    public List<ProcessStartInfo> Runs { get; } = [];

    public Task<int> RunAsync(ProcessStartInfo start, CancellationToken cancel = default)
    {
        Runs.Add(start);
        return Task.FromResult(exitCode);
    }

    public Task<ProcessOutput> CaptureAsync(ProcessStartInfo start, CancellationToken cancel = default)
    {
        Runs.Add(start);
        return Task.FromResult(new ProcessOutput(exitCode, output));
    }

    public void Start(ProcessStartInfo start) => Runs.Add(start);
}

/// <summary>Messages simulés : réponse fixée aux confirmations, textes mémorisés.</summary>
public sealed class FakeDialogs(bool answer = true) : IDialogs
{
    public List<string> Shown { get; } = [];

    public bool Confirm(string text, string? yes = null, string? no = null)
    {
        Shown.Add(text);
        return answer;
    }

    public void Info(string text) => Shown.Add(text);

    public void Error(string text) => Shown.Add(text);
}
