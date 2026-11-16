using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Win32;

namespace UtilitiesManager.Software;

/// <summary>
/// Commandes du profil (<c>detect.command</c>, <c>install_command</c>…) lancées par <c>cmd.exe</c>, cachées. Le
/// PATH est relu dans le registre à chaque appel : un outil installé pendant la session (Python, VS Code) est
/// trouvé sans relancer UtilitiesManager. Une même commande de lecture n'est lancée qu'une fois par vérification.
/// </summary>
public sealed class ShellCommands(IProcessRunner runner)
{
    private readonly ConcurrentDictionary<string, Task<ProcessOutput>> _reads = new(StringComparer.Ordinal);

    /// <summary>Oublie les sorties mémorisées (début d'une vérification, après une installation).</summary>
    public void Reset() => _reads.Clear();

    /// <summary>Sortie d'une commande de lecture, lancée une seule fois tant que <see cref="Reset"/> n'est pas appelé.</summary>
    public Task<ProcessOutput> ReadAsync(string command, CancellationToken cancel = default) =>
        _reads.GetOrAdd(command, c => RunHiddenAsync(c, cancel));

    /// <summary>Commande cachée, sortie capturée. Commande introuvable : code non nul, sans exception.</summary>
    public async Task<ProcessOutput> RunHiddenAsync(string command, CancellationToken cancel = default)
    {
        try
        {
            return await runner.CaptureAsync(Start(command), cancel).ConfigureAwait(false);
        }
        catch (Exception exc) when (exc is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ProcessOutput(-1, exc.Message);
        }
    }

    /// <summary>
    /// Commande avec les droits administrateur : l'UAC s'affiche, la console reste cachée et sa sortie n'est pas
    /// lue. Renvoie le code de sortie (1223 si l'UAC est refusée).
    /// </summary>
    public async Task<int> RunElevatedAsync(string command, CancellationToken cancel = default)
    {
        // ShellExecute refuse un environnement modifié : le PATH n'est pas rafraîchi ici.
        var start = new ProcessStartInfo(Environment.ExpandEnvironmentVariables("%ComSpec%"), Arguments(command))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        };
        try
        {
            return await runner.RunAsync(start, cancel).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception exc) when (exc.NativeErrorCode == 1223)
        {
            return 1223;
        }
    }

    private static ProcessStartInfo Start(string command)
    {
        var start = new ProcessStartInfo(Environment.ExpandEnvironmentVariables("%ComSpec%"), Arguments(command))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        start.Environment["PATH"] = FreshPath();
        return start;
    }

    private static string Arguments(string command) => $"/d /s /c \"{command}\"";

    /// <summary>PATH de la machine puis de l'utilisateur, tels qu'enregistrés à cet instant.</summary>
    public static string FreshPath()
    {
        static string Read(RegistryKey hive, string key)
        {
            using var k = hive.OpenSubKey(key);
            return k?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        }
        var machine = Read(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
        var user = Read(Registry.CurrentUser, "Environment");
        var path = Environment.ExpandEnvironmentVariables(machine + ";" + user);
        return path.Trim(';').Length > 0 ? path : Environment.GetEnvironmentVariable("PATH") ?? "";
    }

    /// <summary>Remplace <c>{file}</c>, <c>{version}</c>, <c>{dep_version}</c> et les variables d'environnement.</summary>
    public static string Expand(string text, string? file = null, string? version = null, string? depVersion = null) =>
        Environment.ExpandEnvironmentVariables(text
            .Replace("{file}", file ?? "", StringComparison.Ordinal)
            .Replace("{version}", version ?? "", StringComparison.Ordinal)
            .Replace("{dep_version}", depVersion ?? "", StringComparison.Ordinal));
}
