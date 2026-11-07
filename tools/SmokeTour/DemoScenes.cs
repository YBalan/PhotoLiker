using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SmokeTour;

/// <summary>
/// Three simple drawn pictures (a birthday cake, a Christmas tree, a sailing boat) for the scenes of the demo videos: the hills of the demo photos look
/// alike to the search model, so a video made of them has one moment; these three are about different things, so a video has moments to find.
/// </summary>
internal static class DemoScenes
{
    private const int W = 1600, H = 1000;

    public static void Render(string path, string kind)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (kind)
            {
                case "cake": Cake(g); break;
                case "tree": Tree(g); break;
                case "boat": Boat(g); break;
                default: throw new ArgumentException(kind);
            }
        }

        var jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
        bmp.Save(path, jpeg, parameters);
    }

    private static void Fill(Graphics g, Color color, RectangleF area)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, area);
    }

    private static void Disc(Graphics g, Color color, float cx, float cy, float rx, float ry)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - rx, cy - ry, rx * 2, ry * 2);
    }

    private static void Poly(Graphics g, Color color, params PointF[] points)
    {
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points);
    }

    private static void RoundedBox(Graphics g, Color color, float x, float y, float w, float h, float radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
        path.AddArc(x + w - radius * 2, y, radius * 2, radius * 2, 270, 90);
        path.AddArc(x + w - radius * 2, y + h - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(x, y + h - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static void Line(Graphics g, Color color, float width, float x1, float y1, float x2, float y2)
    {
        using var pen = new Pen(color, width);
        g.DrawLine(pen, x1, y1, x2, y2);
    }

    private static void Background(Graphics g, Color top, Color bottom)
    {
        using var brush = new LinearGradientBrush(new Rectangle(0, 0, W, H), top, bottom, 90f);
        g.FillRectangle(brush, 0, 0, W, H);
    }

    // ── a birthday cake with candles, balloons behind it ────────────────────────────────────────

    private static void Cake(Graphics g)
    {
        Background(g, Color.FromArgb(255, 238, 215), Color.FromArgb(255, 205, 185));

        Color[] balloons = [Color.FromArgb(230, 70, 80), Color.FromArgb(70, 130, 220), Color.FromArgb(250, 200, 60), Color.FromArgb(90, 190, 110), Color.FromArgb(170, 100, 210)];
        for (var i = 0; i < balloons.Length; i++)
        {
            var x = 180 + i * 310;
            var y = 170 + (i % 2) * 70;
            Line(g, Color.FromArgb(120, 100, 90), 3, x, y + 85, x + (i % 2 == 0 ? 30 : -30), 760);
            Disc(g, balloons[i], x, y, 75, 92);
            Disc(g, Color.FromArgb(90, Color.White), x - 28, y - 35, 18, 28);
        }

        Fill(g, Color.FromArgb(150, 95, 65), new RectangleF(0, 770, W, 230));
        Fill(g, Color.FromArgb(235, 225, 210), new RectangleF(0, 770, W, 26));
        Disc(g, Color.FromArgb(215, 215, 225), 800, 790, 400, 48);

        RoundedBox(g, Color.FromArgb(240, 150, 170), 500, 560, 600, 225, 28);
        RoundedBox(g, Color.FromArgb(255, 242, 222), 590, 400, 420, 175, 26);
        RoundedBox(g, Color.FromArgb(240, 150, 170), 670, 275, 260, 140, 24);
        for (var tier = 0; tier < 3; tier++)
        {
            var (left, right, top) = tier switch { 0 => (500, 1100, 560), 1 => (590, 1010, 400), _ => (670, 930, 275) };
            for (var x = left + 16; x < right - 10; x += 44)
                Disc(g, Color.White, x, top + 18, 24, 26);
        }

        var random = new Random(5);
        for (var s = 0; s < 40; s++)
            Disc(g, new[] { Color.Gold, Color.CornflowerBlue, Color.MediumSeaGreen, Color.White }[s % 4], random.Next(530, 1070), random.Next(640, 760), 5, 5);

        Color[] candleColours = [Color.FromArgb(90, 160, 240), Color.FromArgb(250, 210, 70), Color.FromArgb(120, 200, 120), Color.FromArgb(240, 120, 150), Color.FromArgb(160, 120, 220)];
        for (var c = 0; c < 5; c++)
        {
            var x = 720 + c * 40;
            Fill(g, candleColours[c], new RectangleF(x - 7, 190, 14, 90));
            Disc(g, Color.FromArgb(255, 170, 40), x, 168, 13, 26);
            Disc(g, Color.FromArgb(255, 235, 120), x, 172, 7, 15);
        }
    }

    // ── a Christmas tree in the snow, with presents ─────────────────────────────────────────────

    private static void Tree(Graphics g)
    {
        Background(g, Color.FromArgb(10, 20, 55), Color.FromArgb(50, 75, 125));

        var random = new Random(9);
        for (var s = 0; s < 160; s++)
            Disc(g, Color.FromArgb(random.Next(150, 255), Color.White), random.Next(W), random.Next(H - 100), random.Next(2, 6), random.Next(2, 6));

        Disc(g, Color.FromArgb(245, 248, 255), 800, 1010, 1100, 240);
        Fill(g, Color.FromArgb(95, 60, 40), new RectangleF(770, 840, 60, 110));
        Poly(g, Color.FromArgb(25, 105, 55), new PointF(800, 440), new PointF(400, 880), new PointF(1200, 880));
        Poly(g, Color.FromArgb(30, 125, 65), new PointF(800, 290), new PointF(450, 680), new PointF(1150, 680));
        Poly(g, Color.FromArgb(40, 145, 75), new PointF(800, 150), new PointF(520, 480), new PointF(1080, 480));

        PointF[] star = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var radius = i % 2 == 0 ? 56 : 24;
            star[i] = new PointF(800 + (float)(Math.Cos(angle) * radius), 128 + (float)(Math.Sin(angle) * radius));
        }
        Poly(g, Color.Gold, star);

        (float X, float Y, Color C)[] balls =
        [
            (760, 330, Color.Crimson), (850, 400, Color.Gold), (700, 560, Color.DodgerBlue), (860, 590, Color.Crimson), (600, 780, Color.Gold),
            (780, 760, Color.MediumPurple), (940, 720, Color.DodgerBlue), (1010, 810, Color.Crimson), (690, 440, Color.Gold), (930, 480, Color.Crimson),
        ];
        foreach (var (x, y, color) in balls)
            Disc(g, color, x, y, 22, 22);

        // The presents
        Fill(g, Color.FromArgb(200, 50, 60), new RectangleF(230, 830, 190, 140));
        Fill(g, Color.Gold, new RectangleF(310, 830, 30, 140));
        Fill(g, Color.Gold, new RectangleF(230, 888, 190, 26));
        Fill(g, Color.FromArgb(60, 110, 210), new RectangleF(1170, 850, 220, 120));
        Fill(g, Color.White, new RectangleF(1265, 850, 30, 120));
        Fill(g, Color.White, new RectangleF(1170, 895, 220, 24));
    }

    // ── a sailing boat on the sea ───────────────────────────────────────────────────────────────

    private static void Boat(Graphics g)
    {
        Background(g, Color.FromArgb(100, 175, 235), Color.FromArgb(235, 245, 255));
        Disc(g, Color.FromArgb(255, 245, 190), 1260, 230, 80, 80);

        using (var sea = new LinearGradientBrush(new Rectangle(0, 620, W, H - 620), Color.FromArgb(40, 120, 180), Color.FromArgb(10, 55, 105), 90f))
            g.FillRectangle(sea, 0, 620, W, H - 620);
        for (var row = 0; row < 7; row++)
        {
            var y = 660 + row * 48;
            using var pen = new Pen(Color.FromArgb(120, Color.White), 3);
            for (var x = -40 + (row % 2) * 60; x < W; x += 120)
                g.DrawArc(pen, x, y, 60, 14, 0, 180);
        }

        Line(g, Color.FromArgb(70, 50, 40), 10, 800, 250, 800, 720);
        Poly(g, Color.White, new PointF(812, 270), new PointF(812, 690), new PointF(1090, 690));
        Poly(g, Color.FromArgb(245, 235, 225), new PointF(788, 330), new PointF(788, 690), new PointF(600, 690));
        Poly(g, Color.FromArgb(220, 50, 60), new PointF(800, 250), new PointF(880, 275), new PointF(800, 300));
        Poly(g, Color.FromArgb(140, 55, 40), new PointF(540, 710), new PointF(1130, 710), new PointF(1020, 800), new PointF(650, 800));
        Fill(g, Color.FromArgb(240, 240, 235), new RectangleF(560, 710, 560, 12));
    }
}
