using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UtilitiesManager;

/// <summary>
/// Menu de la zone de notifications au style de Windows 11 : coins arrondis, icone a gauche de chaque element
/// (glyphe Segoe Fluent Icons ou icone), survol en rectangle arrondi, couleurs du theme clair ou sombre.
/// </summary>
internal sealed class TrayMenu : Forms.ContextMenuStrip
{
    private const string FluentIcons = "Segoe Fluent Icons";
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmRound = 2;

    private readonly Palette _palette;
    private readonly Drawing.Font _glyphFont;
    private Drawing.Font? _boldFont;

    public TrayMenu(bool dark)
    {
        _palette = dark
            ? new Palette(Rgb(0x2C2C2C), Rgb(0x3D3D3D), Rgb(0x404040), Rgb(0xFFFFFF))
            : new Palette(Rgb(0xF9F9F9), Rgb(0xE9E9E9), Rgb(0xDEDEDE), Rgb(0x1B1B1B));
        _glyphFont = new Drawing.Font(FluentIcons, 11f);
        if (_glyphFont.Name != FluentIcons)
        {
            _glyphFont.Dispose();
            _glyphFont = new Drawing.Font("Segoe MDL2 Assets", 11f);
        }

        BackColor = _palette.Back;
        Padding = new Forms.Padding(0, Scaled(4), 0, Scaled(4));
        Renderer = new MenuRenderer(this);
    }

    /// <summary>Element precede d'un glyphe Segoe Fluent Icons.</summary>
    public Forms.ToolStripMenuItem AddItem(string text, string glyph, Action click) => AddItem(text, (object)glyph, click);

    /// <summary>Element precede d'une icone, en gras si c'est l'action par defaut du menu.</summary>
    public Forms.ToolStripMenuItem AddItem(string text, Drawing.Icon icon, Action click, bool bold = false)
    {
        var item = AddItem(text, (object)icon, click);
        if (bold)
        {
            item.Font = _boldFont ??= new Drawing.Font(Font, Drawing.FontStyle.Bold);
        }

        return item;
    }

    public void AddSeparator() => Items.Add(new Forms.ToolStripSeparator());

    private Forms.ToolStripMenuItem AddItem(string text, object icon, Action click)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Tag = icon,
            Padding = new Forms.Padding(Scaled(10), Scaled(6), Scaled(16), Scaled(6)),
        };
        item.Click += (_, _) => click();
        Items.Add(item);
        return item;
    }

    private int Scaled(int pixels) => pixels * DeviceDpi / 96;

    private static Drawing.Color Rgb(int rgb) => Drawing.Color.FromArgb(0xFF << 24 | rgb);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Coins arrondis et bordure de Windows 11, sans effet sur Windows 10 (resultat ignore)
        var corner = DwmRound;
        _ = DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref corner, sizeof(int));
        var border = _palette.Line.R | _palette.Line.G << 8 | _palette.Line.B << 16;
        _ = DwmSetWindowAttribute(Handle, DwmBorderColor, ref border, sizeof(int));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _glyphFont.Dispose();
            _boldFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private sealed record Palette(Drawing.Color Back, Drawing.Color Hover, Drawing.Color Line, Drawing.Color Text);

    /// <summary>Dessin complet du menu : rien ne vient des couleurs du systeme.</summary>
    private sealed class MenuRenderer(TrayMenu menu) : Forms.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(menu._palette.Back);

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
            {
                return;
            }

            var inset = menu.Scaled(4);
            var diameter = menu.Scaled(8);
            var bounds = new Drawing.Rectangle(inset, 0, e.Item.Width - 2 * inset - 1, e.Item.Height - 1);
            using var path = new Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var brush = new Drawing.SolidBrush(menu._palette.Hover);
            e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            // Texte et icone centres sur toute la hauteur de l'element : la mise en page de WinForms place le
            // texte en haut de la ligne quand la hauteur de l'element depasse celle du texte
            var inset = menu.Scaled(4);
            var margin = new Drawing.Rectangle(inset, 0, e.TextRectangle.Left - inset, e.Item.Height);
            var text = new Drawing.Rectangle(e.TextRectangle.Left, 0, e.Item.Width - e.TextRectangle.Left, e.Item.Height);
            switch (e.Item.Tag)
            {
                case string glyph:
                    Forms.TextRenderer.DrawText(
                        e.Graphics,
                        glyph,
                        menu._glyphFont,
                        margin,
                        menu._palette.Text,
                        Forms.TextFormatFlags.HorizontalCenter | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.NoPadding | Forms.TextFormatFlags.NoPrefix);
                    break;
                case Drawing.Icon icon:
                    var size = menu.Scaled(16);
                    e.Graphics.DrawIcon(icon, new Drawing.Rectangle(margin.Left + (margin.Width - size) / 2, margin.Top + (margin.Height - size) / 2, size, size));
                    break;
            }

            Forms.TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                text,
                menu._palette.Text,
                Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.HidePrefix);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(menu._palette.Line);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }
    }
}
