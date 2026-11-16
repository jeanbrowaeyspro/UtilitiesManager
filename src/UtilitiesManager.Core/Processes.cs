using System.Diagnostics;

namespace UtilitiesManager;

/// <summary>Lancement de programmes externes, remplacé par un faux dans les tests.</summary>
public interface IProcessRunner
{
    /// <summary>Lance le programme, attend sa fin et renvoie son code de sortie.</summary>
    Task<int> RunAsync(ProcessStartInfo start, CancellationToken cancel = default);

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

    public void Start(ProcessStartInfo start) => Process.Start(start)?.Dispose();
}

/// <summary>FileZilla en cours d'exécution, remplacé par un faux dans les tests.</summary>
public interface IFileZillaProcess
{
    /// <summary>
    /// Ferme FileZilla s'il tourne et renvoie le chemin de son exécutable (pour le
    /// relancer) ; null s'il ne tournait pas.
    /// </summary>
    string? Close();

    bool IsRunning { get; }

    void Start(string exePath);
}

/// <summary>
/// FileZilla réel : fermeture propre (<see cref="Process.CloseMainWindow"/>), puis arrêt
/// forcé si le processus est toujours là après <see cref="GraceDelay"/>.
/// </summary>
public sealed class FileZillaProcess(IProcessRunner runner) : IFileZillaProcess
{
    public static readonly TimeSpan GraceDelay = TimeSpan.FromSeconds(10);

    public string? Close()
    {
        string? path = null;
        foreach (var process in Running())
        {
            using (process)
            {
                try
                {
                    path ??= process.MainModule?.FileName;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(GraceDelay))
                    {
                        Log.Info("FileZilla ne s'est pas fermé de lui-même : arrêt forcé");
                        process.Kill();
                        process.WaitForExit(GraceDelay);
                    }
                }
                catch (Exception exc) when (exc is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Processus terminé entre-temps ou inaccessible.
                    Log.Error($"Fermeture de FileZilla : {exc.Message}");
                }
            }
        }
        return path;
    }

    public bool IsRunning
    {
        get
        {
            var processes = Running();
            foreach (var process in processes)
                process.Dispose();
            return processes.Length > 0;
        }
    }

    private static Process[] Running() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(InstalledFileZilla.ExeName));

    public void Start(string exePath) =>
        runner.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        });
}
