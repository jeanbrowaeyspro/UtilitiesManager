using UtilitiesManager.Profiles;
using UtilitiesManager.Software;
using UtilitiesManager.ViewModels;

namespace UtilitiesManager;

/// <summary>Ligne de l'onglet Logiciels : un logiciel du profil (ou retiré du profil) et son état sur ce PC.</summary>
public sealed class ProgramRow(ProgramStatus status, ProfileDefaults defaults) : ObservableObject
{
    private bool _selected = status.NeedsAction;

    public ProgramStatus Status { get; } = status;

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string Name => Status.Program.Name;
    public string Source => Status.Program.Source;
    public string Installed => Status.Installed.Present ? Status.Installed.Version ?? L.T("unknown") : L.T("not installed");
    public string Available => Status.Latest?.Version ?? "";
    public string Mode => Status.State == ProgramState.Removed ? "" : Texts.Mode(Status.Program.ModeOr(defaults));

    public string State => Status.State switch
    {
        ProgramState.UpToDate => L.T("Up to date"),
        ProgramState.UpdateAvailable => L.T("Update available"),
        ProgramState.Waiting => L.T("Available on {0:d}", Status.AvailableOn?.LocalDateTime),
        ProgramState.Blocked => L.T("Version blocked ({0})", Status.Program.BlockedVersion),
        ProgramState.Missing => L.T("To install"),
        ProgramState.Removed => L.T("Removed from the profile: to uninstall"),
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

    public string Text => $"{Tweak.Name} ({(Applied ? L.T("applied") : L.T("not applied"))})";
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
