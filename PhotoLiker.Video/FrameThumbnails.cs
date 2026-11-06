using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>The small picture kept for a moment of a video, to show in the strip of moments and not read the video for it.</summary>
    public static class FrameThumbnails
    {
        /// <summary>The longer side of a thumbnail, in pixels.</summary>
        public const int LongSide = 160;

        /// <summary>A JPEG of the picture, its longer side at most <see cref="LongSide"/>.</summary>
        public static byte[] Encode(DecodedPhoto picture, int longSide = LongSide)
        {
            var scale = Math.Min(1.0, longSide / (double)Math.Max(picture.Width, picture.Height));
            var width = Math.Max(1, (int)Math.Round(picture.Width * scale));
            var height = Math.Max(1, (int)Math.Round(picture.Height * scale));
            var pixels = scale >= 1 ? picture.Bgr : Resampler.ResizeBgr(picture.Bgr, picture.Width, picture.Height, width, height);

            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
            source.Freeze();
            var encoder = new JpegBitmapEncoder { QualityLevel = 72 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }
}
