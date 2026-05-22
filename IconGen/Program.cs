using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// ── helpers ───────────────────────────────────────────────────────────────────

static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
{
    var p = new GraphicsPath();
    p.AddArc(x,         y,         r*2, r*2, 180, 90);
    p.AddArc(x+w-r*2,   y,         r*2, r*2, 270, 90);
    p.AddArc(x+w-r*2,   y+h-r*2,   r*2, r*2,   0, 90);
    p.AddArc(x,         y+h-r*2,   r*2, r*2,  90, 90);
    p.CloseFigure();
    return p;
}

static GraphicsPath HeartPath(float cx, float cy, float r)
{
    var p = new GraphicsPath();
    float s = r * 0.55f;
    p.AddArc(cx-r, cy-s, r, r, 180, 180);
    p.AddArc(cx,   cy-s, r, r, 180, 180);
    p.AddLine(cx+r, cy, cx, cy+r*1.35f);
    p.AddLine(cx, cy+r*1.35f, cx-r, cy);
    p.CloseFigure();
    return p;
}

static void DropShadow(Graphics g, GraphicsPath path, float blur, Color shadowColor)
{
    // Approximate drop-shadow with a few translucent offset fills
    for (int i = (int)blur; i >= 1; i--)
    {
        int alpha = (int)(80 * (1f - (float)i / (blur + 1)));
        using var shadowBrush = new SolidBrush(Color.FromArgb(alpha, shadowColor));
        var state = g.Save();
        g.TranslateTransform(i * 0.7f, i * 0.7f);
        g.FillPath(shadowBrush, path);
        g.Restore(state);
    }
}

// ── main render ───────────────────────────────────────────────────────────────

static Bitmap Render(int sz)
{
    var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode     = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    float S = sz / 64f; // scale factor (design canvas = 64 px)

    // ── background rounded rect ───────────────────────────────────────────
    using var bgPath = RoundRect(2*S, 2*S, 60*S, 60*S, 10*S);
    using var bgGrad = new LinearGradientBrush(
        new PointF(0, 0), new PointF(sz, sz),
        Color.FromArgb(255, 55, 55, 65),
        Color.FromArgb(255, 25, 25, 35));
    g.FillPath(bgGrad, bgPath);

    // subtle top-gloss
    using var glossPath = RoundRect(4*S, 4*S, 56*S, 28*S, 8*S);
    using var glossBrush = new LinearGradientBrush(
        new PointF(0, 4*S), new PointF(0, 32*S),
        Color.FromArgb(60, 255, 255, 255),
        Color.FromArgb(0,  255, 255, 255));
    g.FillPath(glossBrush, glossPath);

    // ── camera body ───────────────────────────────────────────────────────
    using var bodyPath = RoundRect(6*S, 20*S, 52*S, 34*S, 6*S);
    using var bodyGrad = new LinearGradientBrush(
        new PointF(6*S, 20*S), new PointF(6*S, 54*S),
        Color.FromArgb(255, 80, 82, 90),
        Color.FromArgb(255, 45, 46, 52));
    DropShadow(g, bodyPath, 3, Color.Black);
    g.FillPath(bodyGrad, bodyPath);
    using var bodyPen = new Pen(Color.FromArgb(120, 120, 125, 140), S * 0.8f);
    g.DrawPath(bodyPen, bodyPath);

    // viewfinder bump
    using var bumpPath = RoundRect(20*S, 12*S, 20*S, 10*S, 3*S);
    using var bumpGrad = new LinearGradientBrush(
        new PointF(20*S, 12*S), new PointF(20*S, 22*S),
        Color.FromArgb(255, 70, 72, 80),
        Color.FromArgb(255, 40, 41, 48));
    g.FillPath(bumpGrad, bumpPath);

    // shutter button
    using var shutterBrush = new SolidBrush(Color.FromArgb(255, 55, 57, 65));
    g.FillEllipse(shutterBrush, 42*S, 14*S, 10*S, 7*S);
    using var shutterHighlight = new SolidBrush(Color.FromArgb(60, 200, 200, 210));
    g.FillEllipse(shutterHighlight, 43*S, 14.5f*S, 8*S, 3*S);

    // ── lens outer ring ────────────────────────────────────────────────────
    float lx = 14*S, ly = 22*S, ld = 32*S;
    DropShadow(g, new GraphicsPath(), 2, Color.Black); // implicit below

    // lens barrel
    using var barrelBrush = new SolidBrush(Color.FromArgb(255, 30, 30, 36));
    g.FillEllipse(barrelBrush, lx, ly, ld, ld);
    using var outerRingGrad = new LinearGradientBrush(
        new PointF(lx, ly), new PointF(lx+ld, ly+ld),
        Color.FromArgb(255, 110, 112, 125),
        Color.FromArgb(255, 55, 57, 65));
    using var outerRingPath = new GraphicsPath();
    outerRingPath.AddEllipse(lx, ly, ld, ld);
    outerRingPath.AddEllipse(lx+3*S, ly+3*S, ld-6*S, ld-6*S);
    g.FillPath(outerRingGrad, outerRingPath);

    // lens glass deep
    using var glassDark = new SolidBrush(Color.FromArgb(255, 18, 20, 35));
    g.FillEllipse(glassDark, lx+3*S, ly+3*S, ld-6*S, ld-6*S);

    // blue-tinted lens gradient
    float lg = 5*S;
    using var lensGlassGrad = new LinearGradientBrush(
        new PointF(lx+lg, ly+lg), new PointF(lx+ld-lg, ly+ld-lg),
        Color.FromArgb(255, 25, 40, 110),
        Color.FromArgb(255, 10, 15, 55));
    g.FillEllipse(lensGlassGrad, lx+lg, ly+lg, ld-lg*2, ld-lg*2);

    // inner lens ring
    using var innerRingPen = new Pen(Color.FromArgb(80, 80, 120, 180), S * 0.7f);
    g.DrawEllipse(innerRingPen, lx+7*S, ly+7*S, ld-14*S, ld-14*S);
    g.DrawEllipse(innerRingPen, lx+10*S, ly+10*S, ld-20*S, ld-20*S);

    // lens highlight (glare)
    using var glarePath = new GraphicsPath();
    glarePath.AddEllipse(lx+6*S, ly+6*S, 10*S, 7*S);
    using var glareBrush = new PathGradientBrush(glarePath)
    {
        CenterColor     = Color.FromArgb(160, 255, 255, 255),
        SurroundColors  = new[] { Color.FromArgb(0, 255, 255, 255) }
    };
    g.FillPath(glareBrush, glarePath);

    // ── heart badge ───────────────────────────────────────────────────────
    float hcx = 50*S, hcy = 16*S, hr = 8*S;
    using var heartPath = HeartPath(hcx, hcy, hr);

    // shadow
    {
        var st = g.Save();
        g.TranslateTransform(1.5f*S, 1.5f*S);
        using var shadowBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
        g.FillPath(shadowBrush, heartPath);
        g.Restore(st);
    }

    using var heartGrad = new LinearGradientBrush(
        new PointF(hcx-hr, hcy-hr), new PointF(hcx+hr, hcy+hr),
        Color.FromArgb(255, 255, 80,  80),
        Color.FromArgb(255, 180, 20,  20));
    g.FillPath(heartGrad, heartPath);

    // heart gloss
    using var heartGlossPath = HeartPath(hcx - 1.5f*S, hcy - 2*S, hr * 0.55f);
    using var heartGlossBrush = new LinearGradientBrush(
        new PointF(hcx-hr, hcy-hr), new PointF(hcx, hcy),
        Color.FromArgb(120, 255, 200, 200),
        Color.FromArgb(0,   255, 200, 200));
    g.FillPath(heartGlossBrush, heartGlossPath);

    return bmp;
}

// ── ICO writer ────────────────────────────────────────────────────────────────

static void WriteIco(string outPath, int[] sizes)
{
    var pngs = new List<byte[]>();
    foreach (var sz in sizes)
    {
        using var bmp = Render(sz);
        using var m = new MemoryStream();
        bmp.Save(m, ImageFormat.Png);
        pngs.Add(m.ToArray());
    }

    int headerSize = 6 + sizes.Length * 16;
    int offset = headerSize;
    using var ms = new MemoryStream();
    ms.Write(new byte[] { 0, 0, 1, 0 }, 0, 4);
    ms.Write(BitConverter.GetBytes((ushort)sizes.Length), 0, 2);
    for (int i = 0; i < sizes.Length; i++)
    {
        int w = sizes[i] >= 256 ? 0 : sizes[i];
        ms.WriteByte((byte)w); ms.WriteByte((byte)w);
        ms.WriteByte(0); ms.WriteByte(0);
        ms.Write(new byte[] { 1, 0, 32, 0 }, 0, 4);
        ms.Write(BitConverter.GetBytes(pngs[i].Length), 0, 4);
        ms.Write(BitConverter.GetBytes(offset), 0, 4);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) ms.Write(p, 0, p.Length);
    File.WriteAllBytes(outPath, ms.ToArray());
    Console.WriteLine($"Written: {outPath}");
}

WriteIco(args.Length > 0 ? args[0] : "PhotoLiker.ico", new[] { 16, 32, 48, 256 });
