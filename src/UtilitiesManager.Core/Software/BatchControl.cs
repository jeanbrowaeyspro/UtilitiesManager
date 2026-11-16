namespace UtilitiesManager.Software;

/// <summary>
/// Pause et arrêt d'une série d'installations, demandés par la fenêtre, l'icône de notification ou Ctrl+C. Le
/// logiciel en cours va toujours jusqu'au bout : un installeur coupé au milieu peut laisser un logiciel inutilisable.
/// </summary>
public sealed class BatchControl
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource? _resume;

    /// <summary>Pause, reprise ou arrêt demandés (appelé sur le thread de la demande).</summary>
    public event Action? Changed;

    public bool Paused
    {
        get
        {
            lock (_gate)
                return _resume is not null;
        }
    }

    public bool Stopped => _stop.IsCancellationRequested;

    /// <summary>Annule les téléchargements à l'avance quand l'arrêt est demandé.</summary>
    public CancellationToken StopToken => _stop.Token;

    public void Pause()
    {
        lock (_gate)
        {
            if (_resume is not null || Stopped)
                return;
            _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        Changed?.Invoke();
    }

    public void Resume()
    {
        TaskCompletionSource? resume;
        lock (_gate)
        {
            resume = _resume;
            _resume = null;
        }
        if (resume is null)
            return;
        resume.TrySetResult();
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (Stopped)
            return;
        _stop.Cancel();
        Resume();
        Changed?.Invoke();
    }

    /// <summary>Attend la reprise (ou l'arrêt) si la série est en pause.</summary>
    public Task WaitIfPausedAsync()
    {
        lock (_gate)
            return _resume?.Task ?? Task.CompletedTask;
    }
}
