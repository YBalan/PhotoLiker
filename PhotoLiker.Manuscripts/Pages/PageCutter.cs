using PhotoLiker.Drawing;
using PhotoLiker.Stickers;

namespace PhotoLiker.Manuscripts.Pages
{
    /// <summary>
    /// Cuts the sheet of a manuscript out of its photo: the model that finds the subject of a photo (IS-Net, the same that cuts out the Telegram stickers) says where the sheet is, <see cref="PageOutline"/>
    /// makes four corners of that, and <see cref="PagePerspective"/> straightens what is inside them. A photo with no sheet in it that the model can find (a sheet that fills the photo, a photo of
    /// the page of a book from above with nothing round it) comes back as it is.
    /// </summary>
    public sealed class PageCutter : IDisposable
    {
        private readonly Func<PhotoPixels, SubjectMap> _detect;
        private readonly IDisposable? _owned;

        /// <summary>A cutter that uses the model in the file (it takes about 2 seconds and a gigabyte of memory for a photo).</summary>
        /// <exception cref="FileNotFoundException">The model's file is not there.</exception>
        public PageCutter(string modelPath, int threads = 0)
        {
            var detector = new SubjectDetector(modelPath, threads);
            _detect = detector.Detect;
            _owned = detector;
        }

        /// <summary>A cutter with another way to find the subject (a test, which has no model).</summary>
        internal PageCutter(Func<PhotoPixels, SubjectMap> detect) => _detect = detect;

        /// <summary>What a cut gave: the straight sheet, or null when no sheet was found (the photo is to be used as it is); and where the corners were.</summary>
        public sealed record Cut(Bitmap32? Page, PageQuad? Quad);

        /// <summary>The sheet in the photo, straightened (no larger than <paramref name="maximumSide"/> on its longer side).</summary>
        public Cut Find(Bitmap32 photo, int maximumSide = 3600)
        {
            ArgumentNullException.ThrowIfNull(photo);

            var bgr = new byte[photo.Width * photo.Height * 3];
            var bgra = photo.Bgra;
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                // A see-through point is the colour it is mixed with white, as the rest of the program takes it
                var alpha = bgra[i + 3];
                for (var c = 0; c < 3; c++)
                    bgr[j + c] = alpha == 255 ? bgra[i + c] : (byte)((bgra[i + c] * alpha + 255 * (255 - alpha)) / 255);
            }

            var map = _detect(new PhotoPixels(photo.Width, photo.Height, bgr));
            if (PageOutline.Find(map.Values, map.Width, map.Height, photo.Width, photo.Height) is not { } quad)
                return new Cut(null, null);

            return new Cut(PagePerspective.Straighten(photo, quad, maximumSide), quad);
        }

        public void Dispose() => _owned?.Dispose();
    }
}
