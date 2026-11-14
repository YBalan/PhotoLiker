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

        // The size the next picture is drawn at. A toolbar button is 16; the icon of a window is drawn again at each size a title bar or
        // Alt+Tab can ask for (the drawings are in the units of 16, so they are scaled, and stay sharp). Set only by WindowIcon.
        [ThreadStatic]
        private static int _renderSize;

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

        // ── People and groups ─────────────────────────────────────────────
        // Two people side by side: a blue one behind, an amber one in front (the colours of the search and faces icons)
        public static Bitmap People() => DrawOnBitmap(g =>
        {
            using var back = new SolidBrush(Color.FromArgb(95, 150, 235));
            g.FillEllipse(back, 8.2f, 1.6f, 5f, 5f);
            using var backBody = new GraphicsPath();
            backBody.AddArc(6.6f, 7.4f, 8.4f, 8.4f, 180f, 180f);
            backBody.CloseFigure();
            g.FillPath(back, backBody);

            using var front = new SolidBrush(Color.FromArgb(255, 190, 70));
            using var edge = new Pen(Color.FromArgb(180, 110, 20), 0.8f);
            g.FillEllipse(front, 2.4f, 3.6f, 5.6f, 5.6f);
            g.DrawEllipse(edge, 2.4f, 3.6f, 5.6f, 5.6f);
            using var frontBody = new GraphicsPath();
            frontBody.AddArc(0.8f, 10f, 8.8f, 9f, 180f, 180f);
            frontBody.CloseFigure();
            g.FillPath(front, frontBody);
            g.DrawPath(edge, frontBody);
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

        // ── Rotate ────────────────────────────────────────────────────────
        // A blue arrow that runs three quarters of a circle clockwise
        public static Bitmap RotateClockwise() => DrawOnBitmap(g =>
        {
            const float centreX = 8f, centreY = 8.5f, radius = 5f, endAngle = 240f;
            var blue = Color.FromArgb(60, 135, 235);

            using var pen = new Pen(blue, 1.9f);
            g.DrawArc(pen, centreX - radius, centreY - radius, radius * 2, radius * 2, -30f, endAngle + 30f);

            // The head sits on the end of the arc and points the way the arc runs (clockwise, so the angle grows)
            var angle = endAngle * MathF.PI / 180f;
            var end = new PointF(centreX + radius * MathF.Cos(angle), centreY + radius * MathF.Sin(angle));
            var ahead = new PointF(-MathF.Sin(angle), MathF.Cos(angle));
            var outward = new PointF(MathF.Cos(angle), MathF.Sin(angle));
            using var head = new SolidBrush(blue);
            g.FillPolygon(head, new PointF[]
            {
                new(end.X + ahead.X * 2.7f, end.Y + ahead.Y * 2.7f),
                new(end.X + outward.X * 2.4f, end.Y + outward.Y * 2.4f),
                new(end.X - outward.X * 2.4f, end.Y - outward.Y * 2.4f),
            });
        });

        // The same arrow the other way round: it runs three quarters of a circle counterclockwise
        public static Bitmap RotateCounterClockwise()
        {
            var bitmap = RotateClockwise();
            bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);
            return bitmap;
        }

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

        // ── Colorize ──────────────────────────────────────────────────────
        // A photo that is grey in its lower left half and in colour in its upper right half: a sky, a sun and a hill
        public static Bitmap Colorize() => DrawOnBitmap(g =>
        {
            DrawScene(g, colour => colour);

            // The lower left half again in grey, over the colours: the line between them goes from the top left corner to the bottom right one
            using (var lowerLeft = new GraphicsPath())
            {
                lowerLeft.AddPolygon([new PointF(0f, 2f), new PointF(0f, 16f), new PointF(16f, 16f)]);
                g.SetClip(lowerLeft);
                DrawScene(g, Grey);
                g.ResetClip();
            }

            using var seam = new Pen(Color.FromArgb(190, 255, 255, 255), 0.8f);
            g.DrawLine(seam, 1.4f, 2.6f, 14.6f, 13.4f);
            using var rim = new Pen(Color.FromArgb(25, 55, 130), 1.2f);
            g.DrawRectangle(rim, 1.5f, 2.5f, 13f, 11f);
        });

        // ── Refocus ───────────────────────────────────────────────────────
        // A photo with a sharp ring where the focus is and soft discs of light round it: a portrait mode picture
        public static Bitmap Refocus() => DrawOnBitmap(g =>
        {
            using (var sky = new LinearGradientBrush(new RectangleF(1.5f, 2.5f, 13f, 11f), Color.FromArgb(86, 156, 222), Color.FromArgb(34, 82, 156), 90f))
                g.FillRectangle(sky, 1.5f, 2.5f, 13f, 11f);

            // The out of focus lights, large and soft
            using (var soft = new SolidBrush(Color.FromArgb(105, 255, 255, 255)))
            {
                g.FillEllipse(soft, 9.2f, 3.3f, 4.2f, 4.2f);
                g.FillEllipse(soft, 11.2f, 8.2f, 2.8f, 2.8f);
            }

            // The sharp thing in focus: a ring with a point in its middle
            using (var ring = new Pen(Color.White, 1.25f))
                g.DrawEllipse(ring, 3.3f, 5.3f, 5.4f, 5.4f);
            using (var point = new SolidBrush(Color.White))
                g.FillEllipse(point, 5.2f, 7.2f, 1.6f, 1.6f);

            using var rim = new Pen(Color.FromArgb(25, 55, 130), 1.2f);
            g.DrawRectangle(rim, 1.5f, 2.5f, 13f, 11f);
        });

        // ── Show original (the Colorize bar) ──────────────────────────────
        // The same photo all in grey: what the button brings back while the colours are on (the Colorize picture is what it does while they are off)
        public static Bitmap OriginalPhoto() => DrawOnBitmap(g =>
        {
            DrawScene(g, Grey);
            using var rim = new Pen(Color.FromArgb(70, 70, 70), 1.2f);
            g.DrawRectangle(rim, 1.5f, 2.5f, 13f, 11f);
        });

        // ── Save a copy (the Colorize bar) ────────────────────────────────
        // A floppy disk: a blue body with a cut corner, the metal shutter at the top and the white label at the bottom
        public static Bitmap SaveCopy() => DrawOnBitmap(g =>
        {
            using var body = new GraphicsPath();
            body.AddPolygon([new PointF(1.5f, 2.5f), new PointF(2.5f, 1.5f), new PointF(11.5f, 1.5f), new PointF(14.5f, 4.5f), new PointF(14.5f, 13.5f), new PointF(13.5f, 14.5f), new PointF(2.5f, 14.5f), new PointF(1.5f, 13.5f)]);
            using var fill = new LinearGradientBrush(new RectangleF(1, 1, 14, 14), Color.FromArgb(70, 135, 235), Color.FromArgb(25, 70, 170), 90f);
            g.FillPath(fill, body);
            using var rim = new Pen(Color.FromArgb(20, 45, 115), 0.9f) { LineJoin = LineJoin.Round };
            g.DrawPath(rim, body);

            using var shutter = new SolidBrush(Color.FromArgb(225, 232, 245));
            g.FillRectangle(shutter, 4.5f, 1.5f, 6.5f, 4.2f);
            using var slot = new SolidBrush(Color.FromArgb(60, 75, 120));
            g.FillRectangle(slot, 8.6f, 2.3f, 1.4f, 2.6f);

            using var label = new SolidBrush(Color.FromArgb(246, 248, 253));
            g.FillRectangle(label, 3.5f, 8.2f, 9f, 6.3f);
            using var lines = new Pen(Color.FromArgb(150, 165, 200), 0.8f);
            g.DrawLine(lines, 5f, 10.3f, 11f, 10.3f);
            g.DrawLine(lines, 5f, 12.4f, 11f, 12.4f);
        });

        // ── Many photos (the Colorize bar) ────────────────────────────────
        // A stack of three photos, the one in front with its sky, sun and hill (the window that works on many photos at once)
        public static Bitmap ManyPhotos() => DrawOnBitmap(g =>
        {
            using var rim = new Pen(Color.FromArgb(110, 130, 170), 0.9f);
            using var back = new SolidBrush(Color.FromArgb(185, 200, 225));
            g.FillRoundedRectangle(back, 5.5f, 0.8f, 9.5f, 8f, 1.2f);
            g.DrawRectangle(rim, 6f, 1.3f, 8.5f, 7f);
            using var middle = new SolidBrush(Color.FromArgb(212, 224, 244));
            g.FillRoundedRectangle(middle, 3.2f, 3f, 9.5f, 8f, 1.2f);
            g.DrawRectangle(rim, 3.7f, 3.5f, 8.5f, 7f);

            using var sky = new LinearGradientBrush(new RectangleF(1, 5.5f, 10.5f, 9.5f), Color.FromArgb(200, 230, 255), Color.FromArgb(140, 190, 245), 90f);
            g.FillRoundedRectangle(sky, 1, 5.5f, 10.5f, 9.5f, 1.2f);
            using var sun = new SolidBrush(Color.FromArgb(250, 170, 40));
            g.FillEllipse(sun, 7.6f, 7.1f, 2.4f, 2.4f);
            using var hill = new SolidBrush(Color.FromArgb(45, 150, 80));
            g.FillPolygon(hill, [new PointF(1.6f, 14.3f), new PointF(4.6f, 10f), new PointF(6.6f, 12.4f), new PointF(7.8f, 11.2f), new PointF(11f, 14.3f)]);
            using var front = new Pen(Color.FromArgb(25, 55, 130), 1.1f);
            g.DrawRectangle(front, 1.5f, 6f, 9.5f, 8.5f);
        });

        // ── Photos (the search filter) ────────────────────────────────────
        // A picture: sky, sun and a green hill in a navy frame (readable on the blue of a checked button, and on the dark and the light strip)
        public static Bitmap Photo() => DrawOnBitmap(g =>
        {
            using var sky = new LinearGradientBrush(new RectangleF(1, 2, 14, 12), Color.FromArgb(200, 230, 255), Color.FromArgb(140, 190, 245), 90f);
            g.FillRoundedRectangle(sky, 1, 2, 14, 12, 1.5f);

            using var sun = new SolidBrush(Color.FromArgb(250, 170, 40));
            g.FillEllipse(sun, 9.4f, 3.8f, 3.2f, 3.2f);
            using var hill = new SolidBrush(Color.FromArgb(45, 150, 80));
            g.FillPolygon(hill, [new PointF(2f, 13.2f), new PointF(6.2f, 7.4f), new PointF(9.2f, 11f), new PointF(10.8f, 9.2f), new PointF(14f, 13.2f)]);

            using var rim = new Pen(Color.FromArgb(25, 55, 130), 1.2f);
            g.DrawRectangle(rim, 1.5f, 2.5f, 13f, 11f);
        });

        // ── Videos (the search filter) ────────────────────────────────────
        // A video camera: a dark body with a light rim and a red recording light, and the lens (the dark body and the light rim keep it
        // readable on the blue of a checked button, and on the dark and the light strip)
        public static Bitmap Video() => DrawOnBitmap(g =>
        {
            using var body = new LinearGradientBrush(new RectangleF(1, 4, 9.5f, 8), Color.FromArgb(85, 95, 125), Color.FromArgb(35, 40, 65), 90f);
            g.FillRoundedRectangle(body, 1, 4, 9.5f, 8, 1.5f);
            using var rim = new Pen(Color.FromArgb(235, 240, 250), 1f);
            g.DrawRectangle(rim, 1.5f, 4.5f, 8.5f, 7f);

            using var lens = new SolidBrush(Color.FromArgb(225, 232, 245));
            g.FillPolygon(lens, [new PointF(10.8f, 7f), new PointF(15f, 4.6f), new PointF(15f, 11.4f), new PointF(10.8f, 9f)]);
            using var lensEdge = new Pen(Color.FromArgb(35, 40, 65), 0.7f);
            g.DrawPolygon(lensEdge, [new PointF(10.8f, 7f), new PointF(15f, 4.6f), new PointF(15f, 11.4f), new PointF(10.8f, 9f)]);

            using var light = new SolidBrush(Color.FromArgb(240, 60, 60));
            g.FillEllipse(light, 2.8f, 5.6f, 2.4f, 2.4f);
        });

        // ── Video speech (the table of what is said in the videos) ────────
        // A dark video frame with a play mark, and the teal speech bubble of "found in what is said" over its lower right corner
        public static Bitmap VideoSpeech() => DrawOnBitmap(g =>
        {
            using var frame = new LinearGradientBrush(new RectangleF(0.5f, 1.5f, 11.5f, 9.5f), Color.FromArgb(85, 95, 125), Color.FromArgb(35, 40, 65), 90f);
            g.FillRoundedRectangle(frame, 0.5f, 1.5f, 11.5f, 9.5f, 1.5f);
            using var rim = new Pen(Color.FromArgb(235, 240, 250), 0.9f);
            g.DrawRectangle(rim, 1f, 2f, 10.5f, 8.5f);

            using var play = new SolidBrush(Color.FromArgb(225, 232, 245));
            g.FillPolygon(play, [new PointF(3.6f, 3.9f), new PointF(3.6f, 8.7f), new PointF(7.4f, 6.3f)]);

            // The bubble: the body, the tail down to the left, a white rim so that it stands out of the frame, and three dots for words
            using var bubble = new GraphicsPath();
            bubble.AddArc(6.2f, 6.4f, 3f, 3f, 180, 90);
            bubble.AddArc(12.4f, 6.4f, 3f, 3f, 270, 90);
            bubble.AddArc(12.4f, 10.2f, 3f, 3f, 0, 90);
            bubble.AddLine(10.9f, 13.2f, 8.4f, 15.5f);
            bubble.AddLine(8.4f, 15.5f, 8.7f, 13.2f);
            bubble.AddArc(6.2f, 10.2f, 3f, 3f, 90, 90);
            bubble.CloseFigure();
            using var teal = new SolidBrush(Color.FromArgb(0, 190, 168));
            g.FillPath(teal, bubble);
            using var edge = new Pen(Color.White, 0.9f) { LineJoin = LineJoin.Round };
            g.DrawPath(edge, bubble);

            using var dot = new SolidBrush(Color.White);
            foreach (var x in new[] { 8.2f, 10.8f, 13.4f })
                g.FillEllipse(dot, x - 0.75f, 9.65f, 1.5f, 1.5f);
        });

        // ── Help / About ──────────────────────────────────────────────────
        // A blue disc with a white rim and a white question mark (the Help button, which opens the About box)
        public static Bitmap Help() => DrawOnBitmap(g =>
        {
            using var disc = new LinearGradientBrush(new RectangleF(0.5f, 0.5f, 15, 15), Color.FromArgb(70, 185, 240), Color.FromArgb(20, 135, 210), 90f);
            g.FillEllipse(disc, 0.8f, 0.8f, 14.4f, 14.4f);
            using var rim = new Pen(Color.FromArgb(235, 255, 255, 255), 0.9f);
            g.DrawEllipse(rim, 0.9f, 0.9f, 14.2f, 14.2f);

            using var mark = new Pen(Color.White, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawArc(mark, 5.4f, 3.1f, 5.2f, 5f, 180f, 270f);
            g.DrawLine(mark, 8f, 8.1f, 8f, 9.5f);
            using var dot = new SolidBrush(Color.White);
            g.FillEllipse(dot, 6.95f, 10.8f, 2.1f, 2.1f);
        });

        // ── Hamburger ─────────────────────────────────────────────────────
        /// <summary>Three bars: the button on top of the toolbar that shows and hides the names of the buttons. Drawn in <paramref name="bars"/>, the text colour of the theme.</summary>
        public static Bitmap Hamburger(Color bars) => DrawOnBitmap(g =>
        {
            using var pen = new Pen(bars, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            foreach (var y in new[] { 3.5f, 8f, 12.5f })
                g.DrawLine(pen, 2.5f, y, 13.5f, y);
        });

        // ── Properties ────────────────────────────────────────────────────
        // A small window with a title bar and two rows of a grid (the panel of the property grids)
        public static Bitmap Properties() => DrawOnBitmap(g =>
        {
            using var body = new LinearGradientBrush(new RectangleF(1, 1.5f, 14, 13), Color.FromArgb(235, 244, 255), Color.FromArgb(190, 212, 245), 90f);
            g.FillRoundedRectangle(body, 1, 1.5f, 14, 13, 1.8f);
            using var bar = new SolidBrush(Color.FromArgb(40, 105, 215));
            g.FillRoundedRectangle(bar, 1, 1.5f, 14, 3.6f, 1.8f);
            g.FillRectangle(bar, 1, 3f, 14, 2.1f);

            using var rim = new Pen(Color.FromArgb(40, 105, 215), 0.9f);
            g.DrawRoundedRectangle(rim, 1, 1.5f, 14, 13, 1.8f);

            using var rows = new Pen(Color.FromArgb(70, 95, 140), 1.1f);
            foreach (var y in new[] { 7.6f, 10.2f, 12.6f })
            {
                g.DrawLine(rows, 3f, y, 6.2f, y);
                g.DrawLine(rows, 8f, y, 12.6f, y);
            }
        });

        // ── Dark / light theme ────────────────────────────────────────────
        // A crescent: what the button turns on while the theme is light
        public static Bitmap ThemeDark() => DrawOnBitmap(g =>
        {
            using var moon = new GraphicsPath();
            moon.AddEllipse(2f, 1.5f, 12f, 12f);
            using var bite = new GraphicsPath();
            bite.AddEllipse(6.6f, -0.4f, 10f, 10f);
            using var region = new Region(moon);
            region.Exclude(bite);

            using var fill = new LinearGradientBrush(new RectangleF(2, 1.5f, 12, 12), Color.FromArgb(255, 232, 140), Color.FromArgb(235, 175, 50), 60f);
            g.FillRegion(fill, region);
        });

        // A sun with rays: what the button turns on while the theme is dark
        public static Bitmap ThemeLight() => DrawOnBitmap(g =>
        {
            using var rays = new Pen(Color.FromArgb(240, 160, 30), 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4;
                g.DrawLine(rays, 8f + (float)Math.Cos(angle) * 5.2f, 8f + (float)Math.Sin(angle) * 5.2f,
                    8f + (float)Math.Cos(angle) * 7.2f, 8f + (float)Math.Sin(angle) * 7.2f);
            }

            using var disc = new LinearGradientBrush(new RectangleF(4, 4, 8, 8), Color.FromArgb(255, 225, 110), Color.FromArgb(245, 165, 40), 90f);
            g.FillEllipse(disc, 4f, 4f, 8f, 8f);
        });

        /// <summary>
        /// One of the pictures of this class drawn at <paramref name="size"/> px instead of 16 (the drawings are in the units of 16, so they are scaled and stay sharp): for a button
        /// that is bigger than one of the toolbar. The caller disposes the bitmap.
        /// </summary>
        public static Bitmap Sized(int size, Func<Bitmap> picture)
        {
            _renderSize = size;
            try
            {
                return picture();
            }
            finally
            {
                _renderSize = 0;
            }
        }

        // ── The icon of a window ──────────────────────────────────────────
        /// <summary>
        /// The icon of a window that a toolbar button opens, made from the picture of the button: the same drawing at 16, 20, 24, 32, 40, 48 and
        /// 64 px in one icon, so that Windows takes the nearest to what the title bar (at 100 % to 300 % scaling) or Alt+Tab asks for.
        /// </summary>
        /// <param name="toolbarIcon">One of the pictures of this class, e.g. <c>AppIcons.AlignDates</c>.</param>
        public static Icon WindowIcon(Func<Bitmap> toolbarIcon)
        {
            int[] sizes = [16, 20, 24, 32, 40, 48, 64];
            var pictures = new List<byte[]>();
            foreach (var size in sizes)
            {
                using var bitmap = Sized(size, toolbarIcon);
                using var png = new MemoryStream();
                bitmap.Save(png, ImageFormat.Png);
                pictures.Add(png.ToArray());
            }

            // An .ico file: the header, one entry for each picture (its size and where it is), then the pictures (PNG, which Windows reads
            // since Vista)
            using var file = new MemoryStream();
            using (var writer = new BinaryWriter(file, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((short)0);
                writer.Write((short)1);
                writer.Write((short)sizes.Length);
                var offset = 6 + (16 * sizes.Length);
                for (var i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)sizes[i]);
                    writer.Write((byte)sizes[i]);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((short)1);
                    writer.Write((short)32);
                    writer.Write(pictures[i].Length);
                    writer.Write(offset);
                    offset += pictures[i].Length;
                }

                foreach (var picture in pictures)
                    writer.Write(picture);
            }

            file.Position = 0;
            return new Icon(file);
        }

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
            var size = _renderSize > 0 ? _renderSize : Size;
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / (float)Size, size / (float)Size);
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

        /// <summary>The sky, the sun and the hill of a photo, every colour passed through <paramref name="tone"/>: the same picture in colour and in grey.</summary>
        private static void DrawScene(Graphics g, Func<Color, Color> tone)
        {
            using var sky = new LinearGradientBrush(new RectangleF(1, 2, 14, 12), tone(Color.FromArgb(200, 230, 255)), tone(Color.FromArgb(140, 190, 245)), 90f);
            g.FillRoundedRectangle(sky, 1, 2, 14, 12, 1.5f);
            using var sun = new SolidBrush(tone(Color.FromArgb(250, 170, 40)));
            g.FillEllipse(sun, 9.4f, 3.8f, 3.2f, 3.2f);
            using var hill = new SolidBrush(tone(Color.FromArgb(45, 150, 80)));
            g.FillPolygon(hill, [new PointF(2f, 13.2f), new PointF(6.2f, 7.4f), new PointF(9.2f, 11f), new PointF(10.8f, 9.2f), new PointF(14f, 13.2f)]);
        }

        /// <summary>The grey of a colour, as the brightness the eye sees.</summary>
        private static Color Grey(Color colour)
        {
            var grey = (int)(0.30 * colour.R + 0.59 * colour.G + 0.11 * colour.B);
            return Color.FromArgb(grey, grey, grey);
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

    /// <summary>
    /// The icons of the windows (dialogs) of the program: each one is the icon of the toolbar button that opens it (the date and the group
    /// windows that a tool opens in turn have the one of that tool). Made when first asked for and kept, since a window does not dispose
    /// its icon.
    /// </summary>
    internal static class DialogIcons
    {
        private static Icon? _generateVideo, _alignDates, _findDuplicates, _removeEmptyFolders, _people, _faceRecognition, _help, _videoSpeech, _colorize, _refocus;

        public static Icon GenerateVideo => _generateVideo ??= AppIcons.WindowIcon(AppIcons.GenerateVideo);

        public static Icon AlignDates => _alignDates ??= AppIcons.WindowIcon(AppIcons.AlignDates);

        public static Icon FindDuplicates => _findDuplicates ??= AppIcons.WindowIcon(AppIcons.FindDuplicates);

        public static Icon RemoveEmptyFolders => _removeEmptyFolders ??= AppIcons.WindowIcon(AppIcons.RemoveEmptyFolders);

        public static Icon People => _people ??= AppIcons.WindowIcon(AppIcons.People);

        public static Icon FaceRecognition => _faceRecognition ??= AppIcons.WindowIcon(AppIcons.FaceRecognition);

        public static Icon Help => _help ??= AppIcons.WindowIcon(AppIcons.Help);

        public static Icon VideoSpeech => _videoSpeech ??= AppIcons.WindowIcon(AppIcons.VideoSpeech);

        public static Icon Colorize => _colorize ??= AppIcons.WindowIcon(AppIcons.Colorize);

        public static Icon Refocus => _refocus ??= AppIcons.WindowIcon(AppIcons.Refocus);
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float width, float height, float radius)
        {
            using var path = RoundedRectangle(x, y, width, height, radius);
            g.FillPath(brush, path);
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float width, float height, float radius)
        {
            using var path = RoundedRectangle(x, y, width, height, radius);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }
}
