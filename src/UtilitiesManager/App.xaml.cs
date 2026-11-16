using System.Windows;
using System.Windows.Threading;

namespace UtilitiesManager;

/// <summary>
/// Démarrage : une seule instance (une deuxième lui passe sa ligne de commande), thème, langue, puis le contrôleur
/// qui vit en arrière-plan avec l'icône de notification.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.FromEnvironment();
        Log.Configure(paths.LogFile);
        var args = AppArguments.Parse(e.Args);
        var settings = AppSettings.Load(paths.SettingsFile);
        L.Use(settings.Language);

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            _instance.Forward(e.Args);
            Shutdown();
            return;
        }

        ThemeManager.Apply(this, settings.Theme);
        AppController? controller = null;
        var dialogs = new WpfDialogs(() => controller?.DialogOwner);
        DispatcherUnhandledException += (_, error) =>
        {
            Log.Error($"Erreur non gérée : {error.Exception}");
            dialogs.Error(L.T("Unexpected error:\n\n{0}", error.Exception.Message));
            error.Handled = true;
        };
        Exit += (_, _) => _instance?.Dispose();

        var services = new AppServices(paths, settings, dialogs);
        services.SelfUpdate.CleanUp();
        controller = new AppController(this, services);
        _instance.Listen(forwarded => Dispatcher.BeginInvoke(() => controller.OnForwarded(AppArguments.Parse(forwarded))));
        Log.Info($"Démarrage {AppInfo.Version}{(args.Startup ? " (session)" : "")}");
        await controller.StartAsync(args);
    }
}
