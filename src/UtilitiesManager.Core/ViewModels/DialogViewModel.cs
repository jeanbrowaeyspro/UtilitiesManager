namespace UtilitiesManager.ViewModels;

/// <summary>
/// Messages à l'utilisateur, affichés dans une boîte de dialogue de l'application ;
/// remplacés par un faux dans les tests (une fenêtre modale bloquerait).
/// </summary>
public interface IDialogs
{
    /// <summary>Question : vrai si l'utilisateur choisit <paramref name="yes"/> ("Oui" par défaut).</summary>
    bool Confirm(string text, string? yes = null, string? no = null);

    void Info(string text);

    void Error(string text);
}

/// <summary>Nature d'une boîte de dialogue : choisit son symbole.</summary>
public enum DialogKind
{
    Question,
    Info,
    Error,
}

/// <summary>Bouton d'une boîte de dialogue.</summary>
/// <param name="IsDefault">Bouton principal (Entrée), mis en avant.</param>
/// <param name="IsCancel">Bouton déclenché par Échap.</param>
public sealed record DialogButton(string Text, RelayCommand Command, bool IsDefault, bool IsCancel);

/// <summary>
/// Boîte de dialogue de l'application : titre, symbole, message et boutons. Seul type de
/// fenêtre de message : les questions, informations et erreurs comme la proposition de
/// mise à jour (classe dérivée), qui devient fenêtre d'attente puis message de fin.
/// </summary>
public class DialogViewModel : ObservableObject
{
    private DialogKind _kind;
    private string _message;
    private bool _isWaiting;
    private IReadOnlyList<DialogButton> _buttons = [];

    /// <summary>Boîte simple : chaque bouton note son rang dans <see cref="Choice"/> et ferme.</summary>
    /// <param name="buttons">Textes des boutons : le premier est le bouton principal, le dernier répond à Échap.</param>
    public DialogViewModel(DialogKind kind, string message, params string[] buttons)
        : this(kind, message)
    {
        _buttons = ChoiceButtons(buttons);
    }

    /// <summary>Pour une classe dérivée, qui fournit ses propres <see cref="Buttons"/>.</summary>
    protected DialogViewModel(DialogKind kind, string message)
    {
        _kind = kind;
        _message = message;
    }

    public event Action? RequestClose;

    public string Title => AppInfo.Title;

    public DialogKind Kind
    {
        get => _kind;
        private set => Set(ref _kind, value);
    }

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>
    /// Attente d'une opération : barre de progression indéterminée à la place des
    /// boutons, fenêtre jamais au premier plan forcé (elle ne doit masquer ni l'UAC ni
    /// les fenêtres d'un autre programme).
    /// </summary>
    public bool IsWaiting
    {
        get => _isWaiting;
        private set => Set(ref _isWaiting, value);
    }

    /// <summary>Transforme la boîte en fenêtre d'attente, avec le message donné.</summary>
    public void ShowWaiting(string message)
    {
        Kind = DialogKind.Info;
        Message = message;
        IsWaiting = true;
    }

    /// <summary>
    /// Transforme la boîte (fenêtre d'attente…) en message simple : boutons de
    /// <paramref name="buttons"/>, comme une boîte créée par le constructeur public.
    /// </summary>
    public void ShowMessage(DialogKind kind, string message, params string[] buttons)
    {
        Buttons = ChoiceButtons(buttons);
        Kind = kind;
        Message = message;
        IsWaiting = false;
    }

    public IReadOnlyList<DialogButton> Buttons
    {
        get => _buttons;
        protected set => Set(ref _buttons, value);
    }

    /// <summary>Rang du bouton choisi ; null si la fenêtre a été fermée autrement.</summary>
    public int? Choice { get; private set; }

    /// <summary>Faux tant que la fenêtre ne doit pas se fermer (opération en cours).</summary>
    public virtual bool CanClose => true;

    protected void Close() => RequestClose?.Invoke();

    /// <summary>Boutons qui notent leur rang dans <see cref="Choice"/> et ferment la boîte.</summary>
    private DialogButton[] ChoiceButtons(string[] texts) =>
        [.. texts.Select((text, index) => new DialogButton(text, new RelayCommand(() =>
        {
            Choice = index;
            Close();
        }), index == 0, index == texts.Length - 1))];
}
