using PhotoLiker.Search;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PhotoLikerUI
{
    /// <summary>
    /// The people of the photos of the slideshow, and where to ask who they are.
    /// </summary>
    /// <param name="FacesOf">The faces of a photo (by its file), each with the name it is known by, if any. It is called on the threads that make the frames, and on the window's thread when a frame has not been asked yet, so it must be safe to call from any thread.</param>
    /// <param name="ShowFaces">Whether the boxes with the names are shown when the show begins (the F key turns them on and off).</param>
    /// <param name="ShowNames">Whether the line of names is shown when the show begins (the N key turns it on and off).</param>
    internal sealed record SlideshowPeopleOptions(Func<string, IReadOnlyList<FaceLabel>> FacesOf, bool ShowFaces, bool ShowNames);

    /// <summary>
    /// Puts a box round every face of a known person on the screen, with the name on it, as the faces mode of the main window does: green and solid for a face the user
    /// named, gold and dashed for one that only looks like somebody (the name is the same, without a question mark). Faces nobody is known by are left alone.
    ///
    /// It is drawn on the window after the photo, at the place the photo has at that moment (see <see cref="SlidePlace"/>), so the boxes go with the photo through every effect
    /// (a Ken Burns drift, a slide, a zoom) and fade with it while one photo takes the place of another; lines and letters keep the size they have on the screen, so they stay sharp
    /// when the photo is magnified. It costs about a millisecond for a photo with six faces at full HD, and nothing while the boxes are turned off.
    /// </summary>
    internal static class SlideFaces
    {
        private const int BandAlpha = 170; // the dark band behind a name, so that it reads on any photo

        /// <summary>How many faces got a box (those that are on the screen).</summary>
        /// <param name="frameWidth">The size of the frame the faces were found in, in its own pixels: <paramref name="place"/> says where it lies.</param>
        /// <param name="screen">The client area of the window.</param>
        /// <param name="font">The letters of the names (bold, in pixels).</param>
        /// <param name="opacity">1 draws as it is, less lets the photo through (while one photo dissolves into another).</param>
        public static int Draw(Graphics g, IReadOnlyList<FaceLabel> faces, SlidePlace place, int frameWidth, int frameHeight, Rectangle screen, Font font, double opacity)
        {
            if (opacity <= 0 || faces.Count == 0)
                return 0;

            // The part of the photo that is on the screen: the names stay in it
            var room = RectangleF.Intersect(SlideFaceMarks.OnScreen(new RectangleF(0, 0, frameWidth, frameHeight), place, frameWidth, frameHeight), screen);
            if (room.IsEmpty)
                return 0;

            var alpha = Math.Min(1.0, opacity);
            var drawn = 0;
            var saved = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var format = new StringFormat(StringFormat.GenericTypographic);
            using var band = new SolidBrush(Color.FromArgb((int)Math.Round(BandAlpha * alpha), Color.Black));
            var pad = font.Size / 4f;
            var lineWidth = SlideFaceMarks.LineWidth(screen.Height);

            // The big faces first, so that the name of a small one in front of them is not covered
            foreach (var label in faces.Where(label => label.Name is { Length: > 0 }).OrderByDescending(label => label.Face.Width * label.Face.Height))
            {
                var box = SlideFaceMarks.OnScreen(SlideFaceMarks.BoxOf(label.Face.X, label.Face.Y, label.Face.Width, label.Face.Height, frameWidth, frameHeight), place, frameWidth, frameHeight);
                if (!box.IntersectsWith(screen))
                    continue;

                var colour = Color.FromArgb((int)Math.Round(255 * alpha), label.Confirmed ? Color.LimeGreen : Color.Gold);
                using (var pen = new Pen(colour, lineWidth) { DashStyle = label.Confirmed ? DashStyle.Solid : DashStyle.Dash })
                    g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);

                var text = label.Name!;
                var size = g.MeasureString(text, font, PointF.Empty, format);
                var bandSize = new SizeF(size.Width + pad * 2, size.Height + pad * 2);
                var at = SlideFaceMarks.LabelPlace(box, bandSize, room);

                g.FillRectangle(band, at.X, at.Y, bandSize.Width, bandSize.Height);
                using var ink = new SolidBrush(colour);
                g.DrawString(text, font, ink, at.X + pad, at.Y + pad, format);
                drawn++;
            }

            g.TextRenderingHint = saved;
            return drawn;
        }
    }
}
