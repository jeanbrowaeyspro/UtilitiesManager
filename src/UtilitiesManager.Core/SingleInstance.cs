using System.IO.Pipes;
using System.Text;

namespace UtilitiesManager;

/// <summary>
/// Une seule instance par session Windows : mutex Local\UtilitiesManager_SingleInstance (attendu aussi par
/// l'installeur) et canal nomme par lequel une seconde instance passe sa ligne de commande a la premiere.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "UtilitiesManager_SingleInstance";

    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private Task? _server;

    public SingleInstance(string name = DefaultName)
    {
        _name = name;
        _mutex = new Mutex(true, @"Local\" + name, out var createdNew);
        IsFirst = createdNew;
    }

    public bool IsFirst { get; }

    private string PipeName => _name + "_" + Environment.UserName;

    /// <summary>Premiere instance : ecoute les lignes de commande des instances suivantes.</summary>
    public void Listen(Action<string[]> onArguments)
    {
        if (!IsFirst || _server is not null)
        {
            return;
        }

        _server = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync(_stop.Token).ConfigureAwait(false);
                    onArguments(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // Client parti avant la fin : on attend le suivant.
                }
            }
        });
    }

    /// <summary>Instance suivante : transmet ses arguments a la premiere. Faux si elle ne repond pas.</summary>
    public bool Forward(IEnumerable<string> args, int timeoutMs = 3000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', args));
            pipe.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _server?.Wait(1000);
        }
        catch (AggregateException)
        {
            // Arret du serveur : rien a signaler.
        }

        if (IsFirst)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _stop.Dispose();
    }
}
