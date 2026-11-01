// Generates src/Voxa.Wpf/Assets/Voxa.ico: the "F" tile from the tray, in all sizes Windows asks for.
// Run from the repo root:  dotnet run tools/IconGen/IconGen.cs -- src/Voxa.Wpf/Assets/Voxa.ico
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
// File-based apps default to PublishAot=true, which implies trimming; WinForms refuses that (NETSDK1175).
#:property PublishAot=false

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
string output = args.Length > 0 ? args[0] : "Voxa.ico";

var images = sizes.Select(size => (size, png: RenderPng(size))).ToList();

// ICO = header + one directory entry per image + the images. PNG payloads are allowed since Windows Vista.
using (var file = File.Create(output))
using (var w = new BinaryWriter(file))
{
    w.Write((short)0);            // reserved
    w.Write((short)1);            // type: icon
    w.Write((short)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);         // palette
        w.Write((byte)0);         // reserved
        w.Write((short)1);        // color planes
        w.Write((short)32);       // bits per pixel
        w.Write(png.Length);
        w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in images) w.Write(png);
}
Console.WriteLine($"{output}: {string.Join(", ", sizes)} px");

static byte[] RenderPng(int size)
{
    // Same design as TrayIcon used to draw at 32 px: dark rounded tile, light bold "F".
    float s = size / 32f;
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = size <= 24 ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var background = new SolidBrush(Color.FromArgb(255, 63, 63, 70));
        using var shape = RoundedRectangle(new RectangleF(1 * s, 1 * s, 30 * s, 30 * s), 8 * s);
        g.FillPath(background, shape);

        using var font = new Font("Segoe UI", 19 * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.FromArgb(255, 228, 228, 231));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("F", font, text, new RectangleF(0, 1 * s, size, size), format);
    }

    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static GraphicsPath RoundedRectangle(RectangleF r, float radius)
{
    float d = radius * 2;
    var path = new GraphicsPath();
    path.AddArc(r.X, r.Y, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}
