using System.Diagnostics;
using System.Text;

namespace UtilitiesManager;

/// <summary>Sortie d'un programme lancé caché : code de sortie et texte écrit.</summary>
public sealed record ProcessOutput(int ExitCode, string Text);

/// <summary>Lancement de programmes externes, remplacé par un faux dans les tests.</summary>
public interface IProcessRunner
{
    /// <summary>Lance le programme, attend sa fin et renvoie son code de sortie.</summary>
    Task<int> RunAsync(ProcessStartInfo start, CancellationToken cancel = default);

    /// <summary>Lance le programme caché, attend sa fin et renvoie sa sortie (standard et erreurs).</summary>
    Task<ProcessOutput> CaptureAsync(ProcessStartInfo start, CancellationToken cancel = default);

    /// <summary>Lance le programme sans attendre.</summary>
    void Start(ProcessStartInfo start);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(ProcessStartInfo start, CancellationToken cancel = default)
    {
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"Impossible de lancer {start.FileName}");
        await process.WaitForExitAsync(cancel);
        return process.ExitCode;
    }

    public async Task<ProcessOutput> CaptureAsync(ProcessStartInfo start, CancellationToken cancel = default)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding ??= Encoding.UTF8;
        start.StandardErrorEncoding ??= Encoding.UTF8;
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"Impossible de lancer {start.FileName}");
        var output = process.StandardOutput.ReadToEndAsync(cancel);
        var error = process.StandardError.ReadToEndAsync(cancel);
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Déjà terminé.
            }
            throw;
        }
        var text = await output;
        var errors = await error;
        return new ProcessOutput(process.ExitCode, errors.Length == 0 ? text : text + Environment.NewLine + errors);
    }

    public void Start(ProcessStartInfo start) => Process.Start(start)?.Dispose();
}

/// <summary>Programmes en cours d'exécution, remplacés par un faux dans les tests.</summary>
public interface IAppProcesses
{
    /// <summary>
    /// Ferme les processus nommés (sans <c>.exe</c>) et renvoie les chemins de leurs
    /// exécutables, pour les relancer après l'installation.
    /// </summary>
    IReadOnlyList<string> Close(IEnumerable<string> names);

    void Start(string exePath);
}

/// <summary>
/// Fermeture propre (<see cref="Process.CloseMainWindow"/>), puis arrêt forcé si le
/// processus est toujours là après <see cref="GraceDelay"/>.
/// </summary>
public sealed class AppProcesses(IProcessRunner runner) : IAppProcesses
{
    public static readonly TimeSpan GraceDelay = TimeSpan.FromSeconds(10);

    public IReadOnlyList<string> Close(IEnumerable<string> names)
    {
        var paths = new List<string>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is { } path
                            && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                            paths.Add(path);
                        process.CloseMainWindow();
                        if (!process.WaitForExit(GraceDelay))
                        {
                            Log.Info($"{name} ne s'est pas fermé de lui-même : arrêt forcé");
                            process.Kill();
                            process.WaitForExit(GraceDelay);
                        }
                    }
                    catch (Exception exc) when (exc is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Processus terminé entre-temps ou inaccessible.
                        Log.Error($"Fermeture de {name} : {exc.Message}");
                    }
                }
            }
        }
        return paths;
    }

    public void Start(string exePath) =>
        runner.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        });
}
