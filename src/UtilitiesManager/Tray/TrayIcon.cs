using System.Runtime.InteropServices;
using System.Windows;
using UtilitiesManager.Software;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UtilitiesManager;

/// <summary>
/// Icône de la zone de notifications : nombre d'actions en attente dessiné sur l'icône, clic gauche ouvre la
/// fenêtre, menu Ouvrir / Vérifier maintenant / Quitter (avec Pause et Arrêter pendant une série d'installations),
/// notifications. Masquable dans Paramètres : elle n'apparaît
/// alors que le temps d'une notification.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Action _open;
    private readonly Action _check;
    private readonly Action _quit;
    private readonly Func<BatchControl?> _batch;
    private readonly Action _pause;
    private readonly Action _stop;
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;
    private Drawing.Icon? _badge;
    private Action? _clicked;
    private bool _wanted;

    public TrayIcon(Action open, Action check, Action quit, bool visible, Func<BatchControl?> batch, Action pause, Action stop)
    {
        _open = open;
        _check = check;
        _quit = quit;
        _batch = batch;
        _pause = pause;
        _stop = stop;
        using (var stream = Application.GetResourceStream(new Uri(Ui.IconUri)).Stream)
            _image = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        _icon = new Forms.NotifyIcon { Icon = _image, Text = AppInfo.Title };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                _open();
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var clicked = _clicked;
            _clicked = null;
            _icon.Visible = _wanted;
            clicked?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _icon.Visible = _wanted;
        Rebuild();
        Visible = visible;
        ThemeManager.ThemeChanged += Rebuild;
    }

    public bool Visible
    {
        get => _wanted;
        set
        {
            _wanted = value;
            _icon.Visible = value;
        }
    }

    /// <summary>Nombre d'actions en attente : pastille sur l'icône et infobulle.</summary>
    public void SetPending(int count)
    {
        var old = _badge;
        _badge = count > 0 ? Badge(_image, count) : null;
        _icon.Icon = _badge ?? _image;
        _icon.Text = count > 0 ? $"{AppInfo.Title} - {L.T("{0} action(s) waiting", count)}" : AppInfo.Title;
        old?.Dispose();
    }

    /// <summary>Notification Windows ; <paramref name="clicked"/> est appelé si on clique dessus.</summary>
    public void Notify(string title, string text, Action? clicked = null)
    {
        _clicked = clicked;
        _icon.Visible = true;
        _icon.ShowBalloonTip(10000, title, text, Forms.ToolTipIcon.Info);
    }

    /// <summary>Menu reconstruit au changement de thème et de langue.</summary>
    public void Rebuild()
    {
        var menu = new TrayMenu(ThemeManager.CurrentIsDark);
        menu.AddItem(L.T("Open"), _image, _open, bold: true);
        menu.AddItem(L.T("Check now"), "", _check);
        var pause = menu.AddItem(L.T("Pause"), "", _pause);
        var stop = menu.AddItem(L.T("Stop the installations"), "", _stop);
        menu.Opening += (_, _) =>
        {
            var batch = _batch();
            pause.Visible = stop.Visible = batch is { Stopped: false };
            pause.Text = batch is { Paused: true } ? L.T("Resume") : L.T("Pause");
        };
        menu.AddSeparator();
        menu.AddItem(L.T("Quit"), "", _quit);
        var old = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    /// <summary>Icône de l'application avec une pastille rouge portant le nombre (9+ au-delà).</summary>
    private static Drawing.Icon Badge(Drawing.Icon image, int count)
    {
        var size = Forms.SystemInformation.SmallIconSize;
        using var bitmap = new Drawing.Bitmap(size.Width, size.Height);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.DrawIcon(image, new Drawing.Rectangle(0, 0, size.Width, size.Height));
            var diameter = size.Width * 0.66f;
            var circle = new Drawing.RectangleF(size.Width - diameter, size.Height - diameter, diameter, diameter);
            using var red = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xD1, 0x34, 0x38));
            graphics.FillEllipse(red, circle);
            using var font = new Drawing.Font("Segoe UI", diameter * (count > 9 ? 0.5f : 0.7f), Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
            graphics.DrawString(count > 9 ? "9+" : count.ToString(), font, Drawing.Brushes.White, circle, format);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        ThemeManager.ThemeChanged -= Rebuild;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
        _badge?.Dispose();
    }
}
