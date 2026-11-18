using UtilitiesManager.Profiles;
using UtilitiesManager.Software;
using UtilitiesManager.ViewModels;

namespace UtilitiesManager;

/// <summary>Ligne de l'onglet Logiciels : un logiciel du profil (ou retiré du profil) et son état sur ce PC.</summary>
public sealed class ProgramRow(ProgramStatus status, ProfileDefaults defaults, BatchStep? step = null) : ObservableObject
{
    private bool _selected = status.NeedsAction;
    private BatchStep? _step = step;

    public ProgramStatus Status { get; } = status;

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string Name => Status.Program.Name;
    public string Source => Status.Program.Source;
    /// <summary>Étape pendant une série d'installations ; remplace l'état jusqu'à la vérification suivante.</summary>
    public BatchStep? Step
    {
        get => _step;
        set
        {
            if (!Set(ref _step, value))
                return;
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Installed));
        }
    }

    private bool Uninstall => Status.State == ProgramState.Removed;

    public string Installed => _step is { State: StepState.Done }
        ? Uninstall ? L.T("not installed") : _step.Text ?? Status.Latest?.Version ?? L.T("unknown")
        : Status.Installed.Present ? Status.Installed.Version ?? L.T("unknown") : L.T("not installed");
    public string Available => Status.Latest?.Version ?? "";
    public string Mode => Status.State == ProgramState.Removed ? "" : Texts.Mode(Status.Program.ModeOr(defaults));

    public string State => _step?.State switch
    {
        StepState.Waiting => L.T("Waiting"),
        StepState.Running => Uninstall ? L.T("Uninstalling") : L.T("In progress"),
        StepState.Paused => L.T("Paused"),
        StepState.Done => Uninstall ? L.T("Uninstalled") : L.T("Up to date"),
        StepState.Failed => L.T("Failed: {0}", _step.Text),
        StepState.Skipped => L.T("Not processed"),
        _ => CheckedState,
    };

    private string CheckedState => Status.State switch
    {
        ProgramState.UpToDate => L.T("Up to date"),
        ProgramState.UpdateAvailable => L.T("Update available"),
        ProgramState.Waiting => L.T("Available on {0:d}", Status.AvailableOn?.LocalDateTime),
        ProgramState.Blocked => L.T("Version blocked ({0})", Status.Program.BlockedVersion),
        ProgramState.Missing => L.T("To install"),
        ProgramState.Removed => L.T("Removed from the profile: to uninstall"),
        ProgramState.Dependency => L.T("Installed with {0} if needed", Status.Message),
        _ => L.T("Error: {0}", Status.Message),
    };
}

/// <summary>Ligne de l'onglet Réglages Windows.</summary>
public sealed class TweakRow(TweakEntry tweak, bool applied) : ObservableObject
{
    private bool _selected;

    public TweakEntry Tweak { get; } = tweak;
    public bool Applied { get; } = applied;

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string Name => Tweak.Name;
    public string State => Applied ? L.T("applied") : L.T("not applied");
    public string Text => $"{Name} ({State})";
}

/// <summary>Ligne du relevé des logiciels de ce PC (fenêtre et commande <c>scan</c>).</summary>
public sealed class FoundRow(FoundSoftware found, bool ignored = false) : ObservableObject
{
    private bool _selected;

    public FoundSoftware Found { get; } = found;
    public bool Ignored { get; } = ignored;

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string Name => Ignored ? L.T("{0} (not proposed)", Found.Name) : Found.Name;
    public string Version => Found.Version ?? "";
    public string Publisher => Found.Publisher ?? "";

    public string Origin => Found.Origin switch
    {
        FoundOrigin.Winget => Found.WingetSource ?? "winget",
        FoundOrigin.Registry => L.T("Windows"),
        _ => L.T("Portable folder"),
    };

    /// <summary>Paquet winget ou Store exact, sinon source à chercher à l'ajout.</summary>
    public string Source => Found.IsSure ? Found.WingetId! : L.T("to search");
}

/// <summary>Textes et listes de choix partagés par les fenêtres.</summary>
public static class Texts
{
    /// <summary>Âges minimum proposés, en jours (0 : immédiat).</summary>
    private static readonly int[] MinAges = [0, 1, 2, 3, 5, 7, 10, 14, 21, 30];

    public static string Mode(string mode) => mode == Modes.Auto ? L.T("Automatic") : L.T("With confirmation");

    public static string MinAge(int days) => days == 0 ? L.T("Immediate") : L.T("{0} days", days);

    /// <summary>Choix d'âge minimum (la valeur actuelle est ajoutée si elle n'est pas dans la liste).</summary>
    public static IReadOnlyList<(int Value, string Text)> MinAgeChoices(int current) =>
        MinAges.Append(current).Distinct().Order().Select(d => (d, MinAge(d))).ToList();

    public static IReadOnlyList<(string Value, string Text)> ModeChoices =>
        [(Modes.Confirm, Mode(Modes.Confirm)), (Modes.Auto, Mode(Modes.Auto))];
}
