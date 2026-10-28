using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PhotoLikerUI
{
    /// <summary>
    /// Generates all toolbar and application icons programmatically with
    /// gradient fills, gloss highlights, and drop-shadow effects.
    /// Toolbar icons are 16×16; the app icon is loaded from PhotoLiker.ico.
    /// </summary>
    internal static class AppIcons
    {
        private const int Size = 16;

        // ── Open Source Folder ─────────────────────────────────────────────
        // Warm-yellow folder + magnifying-glass badge
        public static Bitmap OpenSourceFolder() => DrawOnBitmap(g =>
        {
            DrawFolder(g, Color.FromArgb(255, 220, 140), Color.FromArgb(200, 140, 20));
            // Magnifying glass
            using var circlePen = new Pen(Color.FromArgb(30, 90, 210), 1.5f);
            g.DrawEllipse(circlePen, 8.5f, 9f, 4f, 4f);
            g.DrawLine(circlePen, 12.5f, 13f, 14.5f, 15f);
        });

        // ── Open Liked Folder ──────────────────────────────────────────────
        // Green folder + heart badge
        public static Bitmap OpenLikedFolder() => DrawOnBitmap(g =>
        {
            DrawFolder(g, Color.FromArgb(130, 215, 140), Color.FromArgb(20, 140, 50));
            // Heart
            using var hb = new SolidBrush(Color.FromArgb(230, 50, 50));
            DrawHeart(g, hb, 10f, 11f, 2.8f);
        });

        // ── Load Liked Folder ─────────────────────────────────────────────
        // Blue folder + down-arrow
        public static Bitmap LoadLikedFolder() => DrawOnBitmap(g =>
        {
            DrawFolder(g, Color.FromArgb(100, 170, 250), Color.FromArgb(30, 90, 210));
            // Down arrow
            using var ap = new Pen(Color.White, 1.5f) { EndCap = LineCap.ArrowAnchor };
            g.DrawLine(ap, 10f, 7.5f, 10f, 13f);
            using var ab = new SolidBrush(Color.White);
            g.FillPolygon(ab, new PointF[] { new(7.5f, 11f), new(12.5f, 11f), new(10f, 14f) });
        });

        // ── Generate Video ────────────────────────────────────────────────
        // Purple film frame + play marker
        public static Bitmap GenerateVideo() => DrawOnBitmap(g =>
        {
            using var body = new LinearGradientBrush(new RectangleF(1, 2, 13, 12), Color.FromArgb(170, 120, 245), Color.FromArgb(80, 45, 175), 90f);
            g.FillRoundedRectangle(body, 1, 2, 13, 12, 2f);

            using var sprocket = new SolidBrush(Color.FromArgb(220, 240, 240, 255));
            for (int y = 3; y <= 11; y += 2)
            {
                g.FillRectangle(sprocket, 2, y, 1, 1);
                g.FillRectangle(sprocket, 12, y, 1, 1);
            }

            using var play = new SolidBrush(Color.White);
            g.FillPolygon(play, [new PointF(6f, 5f), new PointF(6f, 10f), new PointF(10f, 7.5f)]);
        });

        // ── Register / Unregister Context Menu ────────────────────────────
        // Windows-flag quadrants + check-mark
        public static Bitmap RegisterContextMenu() => DrawOnBitmap(g =>
        {
            int q = 6;
            // Quadrant fills with subtle gradient
            FillQuad(g, Color.FromArgb(240, 60,  60),  2,     2,     q, q);
            FillQuad(g, Color.FromArgb(60,  180, 60),  q + 3, 2,     q, q);
            FillQuad(g, Color.FromArgb(30,  120, 240), 2,     q + 3, q, q);
            FillQuad(g, Color.FromArgb(240, 190, 0),   q + 3, q + 3, q, q);
            // White check
            using var ck = new Pen(Color.White, 1.6f) { LineJoin = LineJoin.Round };
            g.DrawLines(ck, new PointF[] { new(4.5f, 8.5f), new(7f, 11.5f), new(11.5f, 5f) });
        });

        // ── Search ────────────────────────────────────────────────────────
        // Magnifying glass: pale lens, blue rim, amber handle (readable on the dark and the light strip, and on the
        // blue of a checked button)
        public static Bitmap Search() => DrawOnBitmap(g =>
        {
            using var lens = new SolidBrush(Color.FromArgb(200, 225, 255));
            g.FillEllipse(lens, 1.5f, 1.5f, 8.5f, 8.5f);

            using var handle = new Pen(Color.FromArgb(240, 150, 30), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(handle, 9.2f, 9.2f, 14f, 14f);

            using var rim = new Pen(Color.FromArgb(30, 110, 230), 1.7f);
            g.DrawEllipse(rim, 1.5f, 1.5f, 8.5f, 8.5f);

            using var glare = new Pen(Color.FromArgb(220, 255, 255, 255), 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(glare, 3.4f, 3.4f, 4.6f, 4.6f, 200f, 70f);
        });

        // ── Face recognition ──────────────────────────────────────────────
        // The scanner brackets of a face-recognition symbol around a smiling face
        public static Bitmap FaceRecognition() => DrawOnBitmap(g =>
        {
            using var bracket = new Pen(Color.FromArgb(255, 150, 0), 1.6f) { StartCap = LineCap.Square, EndCap = LineCap.Square };
            const float near = 1f, far = 15f, arm = 4.5f;
            g.DrawLines(bracket, [new PointF(near, near + arm), new PointF(near, near), new PointF(near + arm, near)]);
            g.DrawLines(bracket, [new PointF(far - arm, near), new PointF(far, near), new PointF(far, near + arm)]);
            g.DrawLines(bracket, [new PointF(near, far - arm), new PointF(near, far), new PointF(near + arm, far)]);
            g.DrawLines(bracket, [new PointF(far - arm, far), new PointF(far, far), new PointF(far, far - arm)]);

            using var face = new SolidBrush(Color.FromArgb(255, 224, 130));
            g.FillEllipse(face, 3.8f, 3.8f, 8.4f, 8.4f);
            using var outline = new Pen(Color.FromArgb(200, 140, 20), 0.8f);
            g.DrawEllipse(outline, 3.8f, 3.8f, 8.4f, 8.4f);

            using var feature = new SolidBrush(Color.FromArgb(110, 60, 10));
            g.FillEllipse(feature, 5.7f, 6.3f, 1.3f, 1.6f);
            g.FillEllipse(feature, 9f, 6.3f, 1.3f, 1.6f);
            using var smile = new Pen(Color.FromArgb(110, 60, 10), 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(smile, 5.8f, 6.2f, 4.4f, 4.2f, 25f, 130f);
        });

        // ── Align dates ───────────────────────────────────────────────────
        // A calendar page with a red header and a green "equal" badge: the dates of the file made equal to the date taken
        public static Bitmap AlignDates() => DrawOnBitmap(g =>
        {
            using var page = new LinearGradientBrush(new RectangleF(1, 2, 12, 12), Color.White, Color.FromArgb(205, 215, 235), 90f);
            g.FillRoundedRectangle(page, 1, 2, 12, 12, 1.5f);

            using var header = new SolidBrush(Color.FromArgb(215, 60, 55));
            g.FillRectangle(header, 1.5f, 2.5f, 11f, 3.5f);

            using var rings = new SolidBrush(Color.FromArgb(120, 40, 40, 60));
            g.FillRectangle(rings, 4f, 1f, 1.2f, 2.4f);
            g.FillRectangle(rings, 9f, 1f, 1.2f, 2.4f);

            using var days = new SolidBrush(Color.FromArgb(130, 80, 95, 130));
            for (var row = 0; row < 2; row++)
                for (var col = 0; col < 3; col++)
                    g.FillRectangle(days, 2.8f + col * 3f, 7.4f + row * 2.6f, 1.8f, 1.5f);

            using var badge = new SolidBrush(Color.FromArgb(40, 160, 70));
            g.FillEllipse(badge, 8f, 8f, 7.5f, 7.5f);
            using var equals = new Pen(Color.White, 1.2f) { StartCap = LineCap.Square, EndCap = LineCap.Square };
            g.DrawLine(equals, 10f, 10.6f, 13.2f, 10.6f);
            g.DrawLine(equals, 10f, 12.9f, 13.2f, 12.9f);
        });

        // ── Remove empty folders ──────────────────────────────────────────
        // A pale, hollow folder with a red cross badge
        public static Bitmap RemoveEmptyFolders() => DrawOnBitmap(g =>
        {
            DrawFolder(g, Color.FromArgb(225, 232, 245), Color.FromArgb(130, 145, 175));

            using var badge = new SolidBrush(Color.FromArgb(215, 50, 50));
            g.FillEllipse(badge, 7.5f, 7.5f, 8f, 8f);
            using var cross = new Pen(Color.White, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(cross, 10.2f, 10.2f, 12.8f, 12.8f);
            g.DrawLine(cross, 12.8f, 10.2f, 10.2f, 12.8f);
        });

        // ── Find duplicates ───────────────────────────────────────────────
        // Two photos, one behind the other, the one at the back paler
        public static Bitmap FindDuplicates() => DrawOnBitmap(g =>
        {
            using var back = new SolidBrush(Color.FromArgb(185, 200, 225));
            g.FillRoundedRectangle(back, 1, 1, 10, 9, 1.2f);
            using var backRim = new Pen(Color.FromArgb(110, 130, 170), 0.9f);
            g.DrawRectangle(backRim, 1.5f, 1.5f, 9f, 8f);

            using var front = new LinearGradientBrush(new RectangleF(5, 5, 10, 10), Color.FromArgb(235, 244, 255), Color.FromArgb(150, 185, 235), 90f);
            g.FillRoundedRectangle(front, 5, 5, 10, 10, 1.2f);
            using var frontRim = new Pen(Color.FromArgb(40, 90, 190), 1.1f);
            g.DrawRectangle(frontRim, 5.5f, 5.5f, 9f, 9f);

            using var sun = new SolidBrush(Color.FromArgb(250, 170, 40));
            g.FillEllipse(sun, 11f, 7f, 2.4f, 2.4f);
            using var hill = new SolidBrush(Color.FromArgb(60, 150, 90));
            g.FillPolygon(hill, [new PointF(6.3f, 14.2f), new PointF(9.2f, 9.6f), new PointF(11.2f, 12.2f), new PointF(12.2f, 11f), new PointF(14.2f, 14.2f)]);
        });

        // ── Main application icon ─────────────────────────────────────────
        public static Icon AppIcon()
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "PhotoLiker.ico");
            if (File.Exists(icoPath))
                return new Icon(icoPath);
            return GenerateFallbackAppIcon();
        }

        // ─────────────────────────────────────────────────────────────────
        #region drawing helpers

        private static Bitmap DrawOnBitmap(Action<Graphics> draw)
        {
            var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            draw(g);
            return bmp;
        }

        /// <summary>Draws a classic folder shape with gradient body and tab.</summary>
        private static void DrawFolder(Graphics g, Color light, Color dark)
        {
            // Tab
            using var tabGrad = new LinearGradientBrush(
                new PointF(1, 4), new PointF(1, 7),
                light, Color.FromArgb(200, light.R, light.G, light.B));
            g.FillRectangle(tabGrad, 1, 4, 5, 3);

            // Body
            using var bodyGrad = new LinearGradientBrush(
                new PointF(1, 6), new PointF(1, 15),
                light, dark);
            g.FillRectangle(bodyGrad, 1, 6, 13, 9);

            // Outline
            using var pen = new Pen(Color.FromArgb(140, dark), 0.8f);
            g.DrawRectangle(pen, 1, 6, 12, 8);
            g.DrawRectangle(pen, 1, 4, 4, 2);

            // Gloss sheen on body top
            using var sheen = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
            g.FillRectangle(sheen, 2, 7, 11, 3);
        }

        private static void FillQuad(Graphics g, Color c, float x, float y, float w, float h)
        {
            using var grad = new LinearGradientBrush(
                new PointF(x, y), new PointF(x + w, y + h),
                Color.FromArgb(255, Math.Min(255, c.R + 40), Math.Min(255, c.G + 40), Math.Min(255, c.B + 40)),
                c);
            g.FillRectangle(grad, x, y, w, h);
        }

        private static void DrawHeart(Graphics g, Brush brush, float cx, float cy, float r)
        {
            using var path = new GraphicsPath();
            float s = r * 0.55f;
            path.AddArc(cx - r, cy - s, r, r, 180, 180);
            path.AddArc(cx,     cy - s, r, r, 180, 180);
            path.AddLine(cx + r, cy, cx, cy + r * 1.3f);
            path.AddLine(cx, cy + r * 1.3f, cx - r, cy);
            path.CloseFigure();
            g.FillPath(brush, path);
        }

        private static Icon GenerateFallbackAppIcon()
        {
            const int sz = 32;
            using var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var bg = new SolidBrush(Color.FromArgb(50, 50, 60));
                g.FillRectangle(bg, 2, 8, 28, 18);
                using var hb = new SolidBrush(Color.FromArgb(220, 60, 60));
                DrawHeart(g, hb, 22f, 5f, 5f);
            }
            IntPtr hIcon = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(hIcon).Clone();
            NativeMethods.DestroyIcon(hIcon);
            return icon;
        }

        #endregion
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float width, float height, float radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }
}
