using PhotoLiker.Core;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Motion;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Focus
{
    /// <summary>
    /// One photo that a person is refocusing while they look at it: read once and given its depth by the model once (about half a second), and then drawn with any <see cref="FocusLook"/> in a fraction of
    /// a second (<see cref="Render"/>), so that what they change (where the focus is, how strong the blur, how deep the zone that stays sharp, the haze) is seen as they change it. <see cref="Save"/> writes
    /// the copy "name (portrait).jpg" (see <see cref="ColorizedCopy"/>) with the look that is on screen, at the full size of the photo (up to <see cref="SaveSide"/>), without the model running again.
    /// </summary>
    public sealed class FocusPreview
    {
        /// <summary>The longer side of the picture that is drawn on screen: a photo larger than this is shown smaller, so that a change of the look stays quick. The copy that is saved is larger.</summary>
        public const int PreviewSide = 1600;

        /// <summary>The longer side of the copy that is saved, at most: a photo larger than this is made smaller (the blurred copies of the picture need memory in proportion to its size).</summary>
        public const int SaveSide = 4096;

        /// <summary>The end of the name of the copy: "IMG_1324 (portrait).jpg".</summary>
        public const string Suffix = " (portrait)";

        private readonly byte[] _original;
        private readonly uint[] _pixels;
        private readonly DepthMap _depth;
        private readonly long _length;
        private readonly DateTime _written;

        /// <param name="photo">The photo as it is shown (at most <see cref="PreviewSide"/> on its longer side), upright.</param>
        /// <param name="original">The bytes of the file, where the Exif of the copy is taken from.</param>
        internal FocusPreview(string path, byte[] original, DecodedPhoto photo, DepthMap depth, IReadOnlyList<PhotoRegion> faces, long length, DateTime written)
        {
            Path = path;
            _original = original;
            Width = photo.Width;
            Height = photo.Height;
            _pixels = PixelBuffers.FromPhoto(photo);
            _depth = depth;
            _length = length;
            _written = written;
            Initial = FocusLook.Portrait(DepthOfField.AutoFocus(depth, faces));
        }

        /// <summary>The photo this is of.</summary>
        public string Path { get; }

        /// <summary>The size of the picture that <see cref="Render"/> gives.</summary>
        public int Width { get; }

        public int Height { get; }

        /// <summary>The look to begin with: the focus on the people of the photo (the largest face; else the middle), the usual blur and zone, no haze.</summary>
        public FocusLook Initial { get; }

        /// <summary>
        /// Reads the photo and runs the depth model on it. Takes the half second the model needs; call it from a background thread.
        /// </summary>
        /// <param name="faces">Where the faces are in the photo (fractions of it), if the folder has been searched: the focus begins on the largest. May be empty.</param>
        /// <exception cref="Exception">The file can not be read or is no picture (IOException, or whatever the imaging component throws), or the model fails.</exception>
        public static FocusPreview Open(DepthEstimator estimator, string path, IReadOnlyList<PhotoRegion> faces)
        {
            var info = new FileInfo(path);
            var bytes = FileReading.ReadAllBytesShared(path);
            var photo = WicPhotoDecoder.Decode(bytes, PreviewSide);
            return new FocusPreview(path, bytes, photo, estimator.Estimate(photo), faces, info.Length, info.LastWriteTimeUtc);
        }

        /// <summary>The photo with the look. Safe to call from any thread, also while another call is going on.</summary>
        public DecodedPhoto Render(FocusLook look) => PixelBuffers.ToPhoto(DepthOfField.Render(_pixels, Width, Height, _depth, look), Width, Height);

        /// <summary>The depth to focus on when the person points at a place of the photo (<paramref name="x"/> and <paramref name="y"/> are fractions of it).</summary>
        public double FocusAt(double x, double y) => DepthOfField.FocusAt(_depth, x, y);

        /// <summary>Whether the file of the photo is as it was when it was read (the same size and time): a photo that was turned or edited since is not what this shows.</summary>
        public bool MatchesFile()
        {
            try
            {
                var info = new FileInfo(Path);
                return info.Exists && info.Length == _length && info.LastWriteTimeUtc == _written;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes the refocused copy of the photo next to it, with the look, and returns its path. It is made at the size the photo has (up to <see cref="SaveSide"/>), not at the size of the preview.
        /// </summary>
        /// <exception cref="IOException">The copy could not be written.</exception>
        public string Save(FocusLook look) => ColorizedCopy.Save(Path, _original, FullSize(look), Suffix);

        /// <summary>
        /// Writes the refocused photo where the person chose (Save As), at the same size as <see cref="Save"/>; a file that is there is written over (they were asked). Returns <paramref name="destination"/>.
        /// </summary>
        /// <exception cref="IOException">The copy could not be written.</exception>
        public string SaveAs(FocusLook look, string destination) => ColorizedCopy.SaveTo(destination, Path, _original, FullSize(look), overwrite: true);

        private DecodedPhoto FullSize(FocusLook look)
        {
            var photo = WicPhotoDecoder.Decode(_original, SaveSide);
            var refocused = DepthOfField.Render(PixelBuffers.FromPhoto(photo), photo.Width, photo.Height, _depth, look);
            return PixelBuffers.ToPhoto(refocused, photo.Width, photo.Height);
        }
    }
}
