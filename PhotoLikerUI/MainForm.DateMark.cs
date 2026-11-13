using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// The date a photo was taken (a video: recorded) in the bottom right corner of the viewer, white on a dark band as the slideshow writes it, but only while the panel of
    /// properties is folded away (or its window is put away): with the panel open the date is in its grid, and without it there would be nowhere to read it. The date is the one the
    /// slideshow would write, see <see cref="ShownDate"/>. It is drawn on the picture box with the other marks (the check, the folders, the legend), so it is on the photo and
    /// on the poster of a video; a video that plays is a window of its own that nothing can be drawn over.
    /// </summary>
    public partial class MainForm
    {
        private PhotoEntry? _dateMarkEntry; // the entry the text below was worked out for
        private string? _dateMarkText;

        /// <summary>The panel of properties is not on the screen: folded into its tab, or its floating window put away.</summary>
        private bool PanelHidden => _collapsed;

        /// <summary>
        /// The text of the date mark for the file that is shown, or null when it is not to be shown: the panel is there, nothing is shown yet, or the file has no date that can be
        /// believed. Worked out once for a photo that is loaded (the entry), not at every repaint.
        /// </summary>
        internal string? DateMarkText()
        {
            if (!PanelHidden || pictureBox1.Image is null || pictureBox1.Tag is not string file || !_photoCache.TryGet(file, out var entry))
                return null;

            if (ReferenceEquals(entry, _dateMarkEntry))
                return _dateMarkText;

            // The times of the file are the ones of the folder listing; a file that is not in it has no dates to fall back on
            var photo = _indexByPath.TryGetValue(file, out var index) && index < _currentConfig.Files.Count ? _currentConfig.Files[index] : null;
            var taken = ShownDate.Of(entry.Metadata, MediaKinds.IsVideo(file), photo?.CreationTimeUtc ?? default, photo?.LastWriteTimeUtc ?? default, DateTime.Now);

            _dateMarkEntry = entry;
            _dateMarkText = taken is { } date ? DateText.Display(date) : null;
            return _dateMarkText;
        }

        /// <summary>
        /// The part of the picture box that is on the screen: all of it, or, while the photo is enlarged (it is bigger than the viewer and scrolls), the part the viewer
        /// shows, so that the date stays in the corner of what is seen and not of the whole enlarged photo.
        /// </summary>
        private Rectangle SeenPartOfViewer()
        {
            var area = pictureBox1.ClientRectangle;
            if (!IsEnlarged)
                return area;

            var seen = pictureBox1.RectangleToClient(scrollPanel.RectangleToScreen(scrollPanel.ClientRectangle));
            return Rectangle.Intersect(area, seen);
        }

        private void DrawDateMark(Graphics g)
        {
            if (DateMarkText() is not { } text)
                return;

            var area = SeenPartOfViewer();
            if (area.Width <= 0 || area.Height <= 0)
                return;

            const float pad = MainFormConstants.SavedInPadding;
            const float margin = MainFormConstants.DateMarkMargin;
            var format = StringFormat.GenericTypographic; // shared instance, must not be disposed

            using var font = new Font(Font.FontFamily, MainFormConstants.DateMarkFontSize, FontStyle.Bold);
            var size = g.MeasureString(text, font, PointF.Empty, format);

            var box = new RectangleF(area.Right - margin - size.Width - pad * 2, area.Bottom - margin - size.Height - pad * 2, size.Width + pad * 2, size.Height + pad * 2);

            // The poster of a video has its length in this corner (see DrawVideoMark): the date goes just above it
            if (ShowingVideo && !PlayerShown && pictureBox1.Tag is string file && _photoCache.TryGet(file, out var entry) && entry.VideoDuration is { } duration)
            {
                using var lengthFont = new Font(Font.FontFamily, VideoLengthFontSize, FontStyle.Bold);
                var length = VideoLengthBox(g, pictureBox1.ClientRectangle, lengthFont, VideoTime.Format(duration));
                if (box.IntersectsWith(length))
                    box.Y = length.Top - MainFormConstants.DateMarkGap - box.Height;
            }

            using var back = new SolidBrush(Color.FromArgb(MainFormConstants.SavedInBgAlpha, Color.Black));
            g.FillRectangle(back, box);
            g.DrawString(text, font, Brushes.White, box.Left + pad, box.Top + pad, format);
        }
    }
}
