using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace UtilitiesManager.ViewModels;

/// <summary>Base des modèles de vue : notification des propriétés modifiées.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Change la valeur et notifie ; renvoie faux si elle n'a pas changé.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Commande liée à une action, exécutable selon une condition.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Nom et version de l'application.</summary>
public static class AppInfo
{
    public static readonly string Version = typeof(AppInfo).Assembly.GetName().Version!.ToString(3);

    public static readonly string Title = $"{AppPaths.AppName} {Version}";
}
