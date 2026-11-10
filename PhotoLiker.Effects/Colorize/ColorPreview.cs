using PhotoLiker.Core;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Colorize
{
    /// <summary>
    /// One photo that a person is colorizing while they look at it: read once, given its colours by the model once (the two seconds), and then drawn with any <see cref="ColorTuning"/> in tens of
    /// milliseconds (<see cref="Render"/>), so that what they change in the colours is seen as they change it. <see cref="Save"/> writes the colorized copy (see <see cref="ColorizedCopy"/>) with the
    /// tuning that is on screen, without the model running again.
    /// </summary>
    public sealed class ColorPreview
    {
        /// <summary>The longer side of the picture that is drawn on screen: a photo larger than this is shown smaller, so that a change of the colours stays quick. The copy that is saved is not smaller for it.</summary>
        public const int PreviewSide = 4096;

        private readonly byte[] _original;
        private readonly DecodedPhoto _grey;
        private readonly ModelColours _colours;
        private readonly long _length;
        private readonly DateTime _written;

        private ColorPreview(string path, byte[] original, DecodedPhoto grey, ModelColours colours, long length, DateTime written)
        {
            Path = path;
            _original = original;
            _grey = grey;
            _colours = colours;
            _length = length;
            _written = written;
        }

        /// <summary>The photo this is of.</summary>
        public string Path { get; }

        /// <summary>The size of the picture that <see cref="Render"/> gives.</summary>
        public int Width => _grey.Width;

        public int Height => _grey.Height;

        /// <summary>
        /// Reads the photo and runs the model on it. Takes the 2 seconds the model needs; call it from a background thread.
        /// </summary>
        /// <exception cref="Exception">The file can not be read or is no picture (IOException, or whatever the imaging component throws), or the model fails.</exception>
        public static ColorPreview Open(Colorizer colorizer, string path)
        {
            var info = new FileInfo(path);
            var bytes = FileReading.ReadAllBytesShared(path);
            var photo = WicPhotoDecoder.Decode(bytes, PreviewSide);
            return new ColorPreview(path, bytes, photo, colorizer.Analyze(photo), info.Length, info.LastWriteTimeUtc);
        }

        /// <summary>The photo with its colours as <paramref name="tuning"/> says. Safe to call from any thread, also while another call is going on.</summary>
        public DecodedPhoto Render(ColorTuning tuning) => Colorizer.Apply(_grey, _colours, tuning);

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
        /// Writes the colorized copy of the photo next to it, with the colours as <paramref name="tuning"/> says, and returns its path. The copy is made at the size the photo has (up to
        /// <see cref="ColorizeJob.MaxSide"/>), also when the picture on screen is smaller.
        /// </summary>
        /// <exception cref="IOException">The copy could not be written.</exception>
        public string Save(ColorTuning tuning)
        {
            var photo = _grey;
            if (Math.Max(photo.Width, photo.Height) >= PreviewSide)
                photo = WicPhotoDecoder.Decode(_original, ColorizeJob.MaxSide); // it may have been made smaller for the screen

            return ColorizedCopy.Save(Path, _original, Colorizer.Apply(photo, _colours, tuning));
        }
    }
}
