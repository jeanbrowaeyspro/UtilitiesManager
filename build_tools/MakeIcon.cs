#:package System.Drawing.Common@10.0.0
#:property TargetFramework=net10.0-windows
#:property PublishAot=false

// Génère l'icône de l'application (assets/icon.ico et assets/icon.png).
//
// Le motif évoque une mise à jour : une flèche circulaire blanche autour d'une
// flèche montante, sur une pastille violette (celle de HAIC et TwinCATPlus). Le rendu est fait
// en 1024 px puis réduit, ce qui donne des bords nets à toutes les tailles.
//
// Exécuter depuis la racine du projet : dotnet run build_tools/MakeIcon.cs

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Taille de rendu avant réduction (suréchantillonnage x4 de la plus grande icône).
const int Canvas = 1024;
// Tailles stockées dans le .ico, de la vignette de la barre des tâches au grand
// format de l'explorateur.
int[] icoSizes = [16, 24, 32, 48, 64, 128, 256];

var gradientTop = Color.FromArgb(132, 84, 222);     // violet clair
var gradientBottom = Color.FromArgb(92, 48, 178);   // violet foncé
var mark = Color.White;

var root = Directory.GetCurrentDirectory();
var assets = Path.Combine(root, "assets");
Directory.CreateDirectory(assets);

using var master = Render(Canvas);
using (var png = Resize(master, 256))
    png.Save(Path.Combine(assets, "icon.png"), ImageFormat.Png);
WriteIco(Path.Combine(assets, "icon.ico"), master, icoSizes);
Console.WriteLine($"icône écrite dans {assets}");

Bitmap Render(int size)
{
    var scale = size / 1024f;
    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    // Pastille à coins arrondis, dégradé vertical.
    using (var path = RoundedRectangle(new RectangleF(0, 0, size - 1, size - 1), 224 * scale))
    using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(0, size), gradientTop, gradientBottom))
        g.FillPath(brush, path);

    using var brushMark = new SolidBrush(mark);
    using var pen = new Pen(mark, 88 * scale) { StartCap = LineCap.Round };

    // Flèche circulaire : arc de 290° ouvert en haut à droite, pointe au bout.
    var center = new PointF(512 * scale, 512 * scale);
    var radius = 300 * scale;
    const float start = -40, sweep = -290;
    g.DrawArc(pen, center.X - radius, center.Y - radius, 2 * radius, 2 * radius, start, sweep);
    var endAngle = (start + sweep) * MathF.PI / 180;
    var tip = new PointF(center.X + radius * MathF.Cos(endAngle), center.Y + radius * MathF.Sin(endAngle));
    // Tangente au bout de l'arc (sens de parcours anti-horaire à l'écran).
    var tangent = new PointF(MathF.Sin(endAngle), -MathF.Cos(endAngle));
    var normal = new PointF(MathF.Cos(endAngle), MathF.Sin(endAngle));
    float head = 130 * scale, half = 110 * scale;
    g.FillPolygon(brushMark,
    [
        new PointF(tip.X + tangent.X * head, tip.Y + tangent.Y * head),
        new PointF(tip.X + normal.X * half, tip.Y + normal.Y * half),
        new PointF(tip.X - normal.X * half, tip.Y - normal.Y * half),
    ]);

    // Flèche montante au centre.
    PointF P(float x, float y) => new(x * scale, y * scale);
    g.FillPolygon(brushMark, [P(512, 340), P(642, 490), P(560, 490), P(560, 660), P(464, 660), P(464, 490), P(382, 490)]);
    return bitmap;
}

static GraphicsPath RoundedRectangle(RectangleF r, float radius)
{
    var d = radius * 2;
    var path = new GraphicsPath();
    path.AddArc(r.Left, r.Top, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

static Bitmap Resize(Bitmap source, int size)
{
    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bitmap);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingMode = CompositingMode.SourceCopy;
    using var attributes = new ImageAttributes();
    attributes.SetWrapMode(WrapMode.TileFlipXY);   // pas de liseré sur les bords
    g.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel, attributes);
    return bitmap;
}

// Fichier .ico dont chaque image est un PNG (format reconnu depuis Windows Vista).
static void WriteIco(string path, Bitmap master, int[] sizes)
{
    var images = sizes.Select(size =>
    {
        using var bitmap = Resize(master, size);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return (Size: size, Data: stream.ToArray());
    }).ToList();

    using var file = File.Create(path);
    using var w = new BinaryWriter(file);
    w.Write((ushort)0);              // réservé
    w.Write((ushort)1);              // type : icône
    w.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    foreach (var (size, data) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size));   // 0 signifie 256
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);            // pas de palette
        w.Write((byte)0);
        w.Write((ushort)1);          // plans
        w.Write((ushort)32);         // bits par pixel
        w.Write(data.Length);
        w.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in images)
        w.Write(data);
}
