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
