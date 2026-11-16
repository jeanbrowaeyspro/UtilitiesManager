using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UtilitiesManager;

/// <summary>
/// Icone de la zone de notifications : clic gauche ouvre la fenetre, clic droit le menu (Ouvrir, modules,
/// Parametres, Rechercher les mises a jour, Quitter), notification d'une mise a jour disponible.
/// Menu (<see cref="TrayMenu"/>) reconstruit au changement de langue et de theme.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly ShellController _shell;
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;
    private AvailableUpdate? _notified;

    public TrayIcon(ShellController shell)
    {
        _shell = shell;
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/TwinCATPlus;component/assets/icon.ico")).Stream)
        {
            _image = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        _icon = new Forms.NotifyIcon { Icon = _image, Text = $"TwinCATPlus {AppInfo.ProductVersion}" };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _shell.ShowMainWindow();
            }
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_notified is not null)
            {
                _shell.Updates.ShowUpdate(_notified);
            }
        };
        Rebuild();
        _icon.Visible = true;
        Tr.LanguageChanged += Rebuild;
        ThemeManager.ThemeChanged += Rebuild;
        _shell.Registry.ModuleFailed += OnModuleFailed;
    }

    private void OnModuleFailed(Modules.IModule module) => Rebuild();

    /// <summary>Notification de la zone pour une version disponible (clic : dialogue de mise a jour).</summary>
    public void NotifyUpdate(AvailableUpdate update)
    {
        _notified = update;
        _icon.ShowBalloonTip(10000, Tr.T("TwinCATPlus update"), Tr.T("TwinCATPlus {0} is available", update.Version), Forms.ToolTipIcon.Info);
    }

    private void Rebuild()
    {
        var menu = new TrayMenu(ThemeManager.IsDark);
        menu.AddItem(Tr.T("Open TwinCATPlus"), _image, () => _shell.ShowMainWindow(), bold: true);
        if (_shell.Registry.Modules.Count > 0)
        {
            menu.AddSeparator();
            foreach (var module in _shell.Registry.Modules)
            {
                menu.AddItem(module.Title, module.IconGlyph, () => _shell.OpenModule(module));
            }
        }

        menu.AddSeparator();
        menu.AddItem(Tr.T("Settings"), "", () => _shell.OpenSettings());
        menu.AddItem(Tr.T("Check for updates"), "", _shell.CheckUpdates);
        menu.AddSeparator();
        menu.AddItem(Tr.T("Quit"), "", _shell.Quit);

        var old = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    public void Dispose()
    {
        Tr.LanguageChanged -= Rebuild;
        ThemeManager.ThemeChanged -= Rebuild;
        _shell.Registry.ModuleFailed -= OnModuleFailed;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }
}
