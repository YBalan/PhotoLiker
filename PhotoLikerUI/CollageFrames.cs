using System.Drawing.Drawing2D;

namespace PhotoLikerUI
{
    /// <summary>
    /// A page of the collage show (see <see cref="SlideshowCollage"/>): several photos of one event shown at once. For the rest of the slideshow it is one photo like any other (its file is that of the
    /// first of its photos, so the show can end on it, and the frame it has is made of them all, see <see cref="CollageFrames"/>).
    /// </summary>
    internal sealed class CollagePage : PhotoFile
    {
        public CollagePage(IReadOnlyList<PhotoFile> members) : base(members[0].OriginalFilePath, members[0].LikedFilePath)
        {
            Members = members;
            Length = members[0].Length;
            LastWriteTimeUtc = members[0].LastWriteTimeUtc;
            CreationTimeUtc = members[0].CreationTimeUtc;
        }

        /// <summary>The photos of the page, in the order of the list (at least two).</summary>
        public IReadOnlyList<PhotoFile> Members { get; }
    }

    /// <summary>
    /// What a frame of the slideshow is kept under. A photo is its file; a page of the collage is its first photo and how many it has (the photos of a page are those that follow its first in the list, so
    /// that is all it takes to tell two pages apart): the page of two photos that begin with one file and the page of three are two frames, and neither is the frame of the photo alone, whose file is the same.
    /// So the collage can be changed while the show runs, with the frames of the old pages still on the screens.
    /// </summary>
    internal static class FrameKeys
    {
        /// <summary>The key of a photo, or of a page of the collage.</summary>
        public static string Of(PhotoFile photo) => photo is CollagePage page ? PageKey(page.OriginalFilePath, page.Members.Count) : photo.OriginalFilePath;

        /// <summary>The key of the page that begins with <paramref name="firstFile"/> and has <paramref name="photos"/> photos.</summary>
        public static string PageKey(string firstFile, int photos) => $"{firstFile}|page{photos}";
    }

    /// <summary>Makes the frame of a page of the collage show: the photos laid out on a screen-sized picture (see <see cref="CollagePlan"/>), each as large as its cell lets it be, in the middle of it.</summary>
    internal static class CollageFrames
    {
        /// <summary>The gap between the photos and round them, as a share of the height of the screen.</summary>
        private const float GapShare = 0.012f;

        /// <summary>
        /// The frame of a page, for a screen of this size. The photos of the page are read one after the other (the caller makes it on a thread of the pool, like any frame); one that can not be read leaves its
        /// cell black, and a page of which none can be read is a failure, as a photo that can not be read is. The date it carries is that of the first photo that has one.
        /// </summary>
        /// <param name="load">Makes the frame of one photo that fits in the cell it is given (the size).</param>
        public static SlideshowFrame Make(CollagePage page, Size screen, Func<PhotoFile, Size, SlideshowFrame> load)
        {
            var gap = Math.Max(2, (int)Math.Round(screen.Height * GapShare));
            var cells = CollagePlan.Cells(page.Members.Count, screen.Width, screen.Height, gap);
            var bitmap = new Bitmap(screen.Width, screen.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var (taken, drawn, failure) = Draw(bitmap, page, cells, load);
                if (drawn == 0)
                    throw failure ?? new InvalidOperationException("The page has no photos.");

                return new SlideshowFrame(bitmap, taken, FrameKeys.Of(page)); // takes the bitmap, and disposes it
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static (DateTime? Taken, int Drawn, Exception? Failure) Draw(Bitmap bitmap, CollagePage page, System.Drawing.RectangleF[] cells, Func<PhotoFile, Size, SlideshowFrame> load)
        {
            using var g = Graphics.FromImage(bitmap); // gone when this returns: the bitmap is handed over after
            g.Clear(Color.Black);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            DateTime? taken = null;
            Exception? failure = null;
            var drawn = 0;
            for (var i = 0; i < page.Members.Count; i++)
            {
                try
                {
                    using var photo = load(page.Members[i], new Size(Math.Max(1, (int)cells[i].Width), Math.Max(1, (int)cells[i].Height)));
                    var x = (int)Math.Round(cells[i].X + (cells[i].Width - photo.Width) / 2);
                    var y = (int)Math.Round(cells[i].Y + (cells[i].Height - photo.Height) / 2);
                    g.DrawImage(photo.Image, new Rectangle(x, y, photo.Width, photo.Height));
                    taken ??= photo.Taken;
                    drawn++;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failure ??= ex;
                }
            }
            return (taken, drawn, failure);
        }
    }
}
