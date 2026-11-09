using System.Drawing.Drawing2D;

namespace PhotoLikerUI
{
    /// <summary>
    /// What marks a place in a video, or a video, that a search found in what is said (the speech) and not in the pictures: a small speech bubble in teal, and ticks of the same
    /// teal on the slider. The places the pictures found are orange. The bubble is drawn, not written with a font: the emoji for it is not in every font.
    /// </summary>
    internal static class SpeechMark
    {
        /// <summary>The colour of what was found by speech.</summary>
        public static readonly Color Colour = Color.FromArgb(0, 190, 168);

        /// <summary>Draws the bubble so that it fills <paramref name="box"/> (its tail takes the lowest fifth).</summary>
        public static void Draw(Graphics g, Rectangle box)
        {
            if (box.Width < 8 || box.Height < 8)
                return;

            var before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var width = (float)box.Width;
            var body = new RectangleF(box.Left, box.Top, width, box.Height * 0.78f);
            var radius = Math.Max(2f, box.Height * 0.2f);
            var diameter = radius * 2;

            using var path = new GraphicsPath();
            path.AddArc(body.Left, body.Top, diameter, diameter, 180, 90);
            path.AddArc(body.Right - diameter, body.Top, diameter, diameter, 270, 90);
            path.AddArc(body.Right - diameter, body.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddLine(body.Left + width * 0.52f, body.Bottom, body.Left + width * 0.18f, box.Bottom); // the tail, down to the left
            path.AddLine(body.Left + width * 0.18f, box.Bottom, body.Left + width * 0.28f, body.Bottom);
            path.AddArc(body.Left, body.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            using (var fill = new SolidBrush(Color.FromArgb(235, Colour)))
                g.FillPath(fill, path);
            using (var edge = new Pen(Color.FromArgb(230, Color.White), Math.Max(1f, box.Height / 14f)) { LineJoin = LineJoin.Round })
                g.DrawPath(edge, path);

            var dot = Math.Max(1.5f, box.Height * 0.075f);
            var middle = body.Top + body.Height / 2f;
            using (var white = new SolidBrush(Color.White))
            {
                foreach (var at in new[] { 0.28f, 0.5f, 0.72f })
                    g.FillEllipse(white, body.Left + width * at - dot, middle - dot, dot * 2, dot * 2);
            }

            g.SmoothingMode = before;
        }
    }
}
