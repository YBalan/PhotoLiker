// Standalone tool – run once to emit PhotoLiker.ico next to this script.
// Usage: dotnet script GenerateIcon.csx   (or just build+run as a console app)
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static void DrawHeart(Graphics g, Brush brush, float cx, float cy, float r)
{
    using var path = new System.Drawing.Drawing2D.GraphicsPath();
    float s = r * 0.55f;
    path.AddArc(cx - r, cy - s, r, r, 180, 180);
    path.AddArc(cx,     cy - s, r, r, 180, 180);
    path.AddLine(cx + r, cy, cx, cy + r * 1.3f);
    path.AddLine(cx, cy + r * 1.3f, cx - r, cy);
    path.CloseFigure();
    g.FillPath(brush, path);
}

static void FillRoundedRectangle(Graphics g, Brush brush, float x, float y, float width, float height, float radius)
{
    using var path = new System.Drawing.Drawing2D.GraphicsPath();
    path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
    path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
    path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
    path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
    path.CloseFigure();
    g.FillPath(brush, path);
}

static Bitmap RenderIcon(int sz)
{
    var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode     = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.Clear(Color.Transparent);

    float s = sz / 32f;
    using var bodyBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
    FillRoundedRectangle(g, bodyBrush, 2*s, 8*s, 28*s, 18*s, 3*s);

    using var lensBrush = new SolidBrush(Color.FromArgb(30, 30, 120));
    g.FillEllipse(lensBrush, 8*s, 10*s, 14*s, 14*s);
    using var lensPen = new Pen(Color.FromArgb(80, 130, 220), 1.5f*s);
    g.DrawEllipse(lensPen, 8*s, 10*s, 14*s, 14*s);
    g.DrawEllipse(lensPen, 11*s, 13*s, 8*s, 8*s);

    using var heartBrush = new SolidBrush(Color.FromArgb(220, 60, 60));
    DrawHeart(g, heartBrush, 22*s, 5*s, 5*s);

    using var bumpBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
    g.FillRectangle(bumpBrush, 12*s, 5*s, 8*s, 4*s);
    return bmp;
}

// Write a multi-size .ico containing 16, 32, 48, 256 px images
static void WriteIco(string path, int[] sizes)
{
    var images = sizes.Select(RenderIcon).ToArray();
    using var ms = new MemoryStream();

    // ICO header
    ms.Write(new byte[] { 0, 0 }, 0, 2);   // reserved
    ms.Write(new byte[] { 1, 0 }, 0, 2);   // type = ICO
    byte[] count = BitConverter.GetBytes((ushort)sizes.Length);
    ms.Write(count, 0, 2);

    // Directory entries – we need to know the data offsets in advance
    int headerSize = 6 + sizes.Length * 16;
    var pngBytes = images.Select(img => {
        using var m = new MemoryStream();
        img.Save(m, ImageFormat.Png);
        return m.ToArray();
    }).ToArray();

    int offset = headerSize;
    for (int i = 0; i < sizes.Length; i++)
    {
        int w = sizes[i] >= 256 ? 0 : sizes[i];
        int h = sizes[i] >= 256 ? 0 : sizes[i];
        ms.WriteByte((byte)w);
        ms.WriteByte((byte)h);
        ms.WriteByte(0); // color count
        ms.WriteByte(0); // reserved
        ms.Write(new byte[] { 1, 0 }, 0, 2); // color planes
        ms.Write(new byte[] { 32, 0 }, 0, 2); // bits per pixel
        ms.Write(BitConverter.GetBytes(pngBytes[i].Length), 0, 4);
        ms.Write(BitConverter.GetBytes(offset), 0, 4);
        offset += pngBytes[i].Length;
    }

    foreach (var png in pngBytes)
        ms.Write(png, 0, png.Length);

    File.WriteAllBytes(path, ms.ToArray());
    foreach (var img in images) img.Dispose();
}

WriteIco("PhotoLikerUI/PhotoLiker.ico", new[] { 16, 32, 48, 256 });
Console.WriteLine("PhotoLiker.ico written.");
