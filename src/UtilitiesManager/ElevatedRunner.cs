using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using UtilitiesManager.Profiles;
using UtilitiesManager.Software;

namespace UtilitiesManager;

/// <summary>
/// Série d'installations menée par un second processus de l'application, lancé une fois en administrateur
/// (<c>/eleve canal</c>) : les installeurs qu'il lance héritent de ses droits et ne redemandent rien. La fenêtre
/// lui passe la série et ses ordres de pause et d'arrêt, et reçoit l'avancement, par un canal nommé
/// (<see cref="ElevatedProtocol"/>). L'invite UAC reste celle de Windows.
/// </summary>
public static class ElevatedRunner
{
    private const int UacRefused = 1223;

    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Côté fenêtre : lance le processus administrateur et lui confie <paramref name="items"/> ; null si les droits
    /// sont refusés ou si le processus ne répond pas (la série se fait alors dans ce processus).
    /// </summary>
    public static async Task<ElevatedReport?> RunAsync(IReadOnlyList<ElevatedItem> items, Profile profile,
                                                      Action<string, double?> report, BatchControl control)
    {
        var name = $"UtilitiesManager_{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        report(L.T("Asking once for the administrator rights of all the installations…"), null);
        Process? process;
        try
        {
            // ShellExecute attend la réponse à l'invite UAC : hors du thread de la fenêtre.
            process = await Task.Run(() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{Switches.Elevated} {name}")
            {
                UseShellExecute = true,
                Verb = "runas",
            }));
        }
        catch (Win32Exception exc)
        {
            Log.Info($"Processus administrateur : {exc.Message}");
            report(exc.NativeErrorCode == UacRefused
                ? L.T("Administrator rights refused: each installer will ask for them if it needs them.")
                : L.T("Administrator rights unavailable ({0}): each installer will ask for them if it needs them.", exc.Message), null);
            return null;
        }
        using (process)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    await pipe.WaitForConnectionAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    report(L.T("The administrator process did not answer: each installer will ask for the rights if it needs them."), null);
                    return null;
                }
            }

            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var gate = new SemaphoreSlim(1);
            async Task SendAsync(string line)
            {
                await gate.WaitAsync();
                try
                {
                    await writer.WriteLineAsync(line);
                }
                catch (IOException)
                {
                    // Processus administrateur déjà terminé.
                }
                finally
                {
                    gate.Release();
                }
            }

            await SendAsync(ElevatedProtocol.Write(new ElevatedRequest(profile.ToJson(), [.. items])));
            void OnChanged() => _ = SendAsync(ElevatedProtocol.Order(control));
            control.Changed += OnChanged;
            try
            {
                if (control.Paused || control.Stopped)
                    OnChanged();
                while (await reader.ReadLineAsync() is { } line)
                {
                    var message = ElevatedProtocol.Read<ElevatedReport>(line);
                    if (message.Done is not null)
                        return message with { Failed = message.Failed ?? [] };
                    report(message.Text ?? "", message.Fraction);
                }
            }
            finally
            {
                control.Changed -= OnChanged;
            }
        }
        // Interrompu sans bilan : la vérification qui suit dira ce qui a été installé.
        var interrupted = L.T("the administrator process stopped unexpectedly");
        report(L.T("Installations interrupted: {0}.", interrupted), null);
        return new ElevatedReport(Done: [], Failed: [.. items.Select(i => new ElevatedFailure(i.Program.Id, interrupted))]);
    }

    /// <summary>Côté processus administrateur : mène la série reçue et renvoie le code de sortie.</summary>
    public static async Task<int> ServeAsync(AppServices services, string pipeName)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(10_000);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            var gate = new Lock();
            void Send<T>(T message)
            {
                lock (gate)
                {
                    try
                    {
                        writer.WriteLine(ElevatedProtocol.Write(message));
                    }
                    catch (IOException)
                    {
                        // Fenêtre fermée : la série s'arrête après le logiciel en cours (lecture ci-dessous).
                    }
                }
            }

            var request = ElevatedProtocol.Read<ElevatedRequest>(await reader.ReadLineAsync() ?? throw new IOException("demande absente"));
            var control = new BatchControl();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await reader.ReadLineAsync() is { } order)
                        ElevatedProtocol.Apply(order, control);
                }
                catch (IOException)
                {
                    // Canal coupé.
                }
                control.Stop();
            });
            Log.Info($"Processus administrateur : {request.Items.Count} logiciel(s)");
            var (done, failed) = await new SoftwareActions(services).RunHereAsync(
                request.Items, Profile.Parse(request.Profile), (text, fraction) => Send(new ElevatedReport(text, fraction)), control);
            Send(new ElevatedReport(Done: [.. done.Select(p => p.Id)],
                                    Failed: [.. failed.Select(f => new ElevatedFailure(f.Program.Id, f.Message))]));
            return failed.Count > 0 ? CliCommand.ExitFailed : CliCommand.ExitOk;
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            Log.Error($"Processus administrateur : {exc}");
            return CliCommand.ExitFailed;
        }
    }
}
