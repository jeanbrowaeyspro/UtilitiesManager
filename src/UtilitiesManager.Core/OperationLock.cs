namespace UtilitiesManager;

/// <summary>
/// Une seule opération à la fois sur ce PC (vérification, installation, retour arrière), entre la fenêtre en
/// arrière-plan et les commandes : sémaphore nommé de la session (non lié au thread, contrairement à un mutex, pour
/// être libéré après un <c>await</c>).
/// </summary>
public sealed class OperationLock : IDisposable
{
    public const string DefaultName = "UtilitiesManager_Operation";

    private readonly Semaphore _semaphore;

    private OperationLock(Semaphore semaphore) => _semaphore = semaphore;

    /// <summary>Prend le verrou, null s'il est déjà pris.</summary>
    public static OperationLock? TryAcquire(string name = DefaultName)
    {
        var semaphore = new Semaphore(1, 1, @"Local\" + name);
        if (semaphore.WaitOne(0))
            return new OperationLock(semaphore);
        semaphore.Dispose();
        return null;
    }

    public void Dispose()
    {
        _semaphore.Release();
        _semaphore.Dispose();
    }
}
