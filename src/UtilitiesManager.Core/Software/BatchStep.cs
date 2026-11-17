namespace UtilitiesManager.Software;

/// <summary>Étape d'un logiciel dans une série d'installations, affichée dans le tableau au fil de la série.</summary>
public enum StepState
{
    Waiting,
    Running,
    Paused,
    Done,
    Failed,
    /// <summary>Non traité : série arrêtée avant lui.</summary>
    Skipped,
}

/// <summary>
/// Étape du logiciel <paramref name="Id"/> ; <paramref name="Text"/> : version installée (<see cref="StepState.Done"/>)
/// ou message d'erreur (<see cref="StepState.Failed"/>).
/// </summary>
public sealed record BatchStep(string Id, StepState State, string? Text = null);
