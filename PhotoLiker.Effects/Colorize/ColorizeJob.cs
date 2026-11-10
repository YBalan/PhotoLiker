using PhotoLiker.Core;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Colorize
{
    /// <summary>What Colorize does to one photo at a time: how much colour it has, and its colorized copy.</summary>
    public static class ColorizeJob
    {
        /// <summary>The longer side of the picture that is looked at to tell whether a photo is black and white: a JPEG is decoded at about this size straight away, so it costs a few milliseconds.</summary>
        public const int MeasureSide = 128;

        /// <summary>The longest side a photo is colorized at: larger ones (a 100 megapixel scan) are made smaller first, since the copy is a JPEG of it.</summary>
        public const int MaxSide = 8000;

        /// <summary>How much colour the photo in this file has. Safe to call from any thread, for many files at once.</summary>
        /// <exception cref="Exception">The file can not be read or is no picture (IOException, or whatever the imaging component throws).</exception>
        public static ColorAmount Measure(string path)
        {
            var bytes = FileReading.ReadAllBytesShared(path);
            return BlackAndWhite.Measure(WicPhotoDecoder.Decode(bytes, MeasureSide));
        }

        /// <summary>
        /// Gives colours to the photo in this file and writes the colorized copy next to it (see <see cref="ColorizedCopy"/>), whatever the photo is like (a photo that has colours already gets the colours of
        /// the model instead, from its lightness). Takes the 2 seconds the model needs and 270 MB of memory besides the photo. Returns the path of the copy.
        /// </summary>
        /// <param name="saturation">How strong the colours are: 1 as the model gives them (see <see cref="Colorizer.Colorize"/>).</param>
        public static string Colorize(Colorizer colorizer, string path, float saturation = 1f)
        {
            var bytes = FileReading.ReadAllBytesShared(path);
            var colorized = colorizer.Colorize(WicPhotoDecoder.Decode(bytes, MaxSide), saturation);
            return ColorizedCopy.Save(path, bytes, colorized);
        }
    }
}
