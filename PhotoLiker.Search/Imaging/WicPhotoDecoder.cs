using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoLiker.Search
{
    /// <summary>
    /// Decodes a photo file with the Windows imaging component. JPEGs are decoded at a reduced size straight away,
    /// which is much cheaper than decoding the full resolution first. The EXIF orientation is applied.
    /// Safe to call from any thread.
    /// </summary>
    public static class WicPhotoDecoder
    {
        public const int DefaultMaxSide = 1280;

        private const string OrientationQuery = "/app1/ifd/{ushort=274}";

        /// <param name="maxLongSide">The longest side of the result; smaller photos keep their size.</param>
        public static DecodedPhoto Decode(byte[] fileBytes, int maxLongSide = DefaultMaxSide)
        {
            using var stream = new MemoryStream(fileBytes, writable: false);
            var (width, height, orientation) = ReadHeader(stream);
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            if (Math.Max(width, height) > maxLongSide)
            {
                if (width >= height) image.DecodePixelWidth = maxLongSide; else image.DecodePixelHeight = maxLongSide;
            }
            image.EndInit();
            image.Freeze();

            BitmapSource source = image;
            if (OrientationTransform(orientation) is { } transform)
                source = new TransformedBitmap(source, transform);

            var bgr = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            var pixels = new byte[bgr.PixelWidth * bgr.PixelHeight * 3];
            bgr.CopyPixels(pixels, bgr.PixelWidth * 3, 0);
            return new DecodedPhoto(bgr.PixelWidth, bgr.PixelHeight, pixels);
        }

        /// <summary>
        /// Decodes a photo to fill as much as it can of a box of <paramref name="maxWidth"/> x <paramref name="maxHeight"/> pixels without changing its
        /// proportions, for showing it on a screen: upright, never enlarged (a smaller photo keeps its size). Like <see cref="Decode"/> a JPEG is
        /// decoded at the reduced size straight away, so a 24 MP photo costs about the same as a 2 MP one. Safe to call from any thread.
        /// </summary>
        public static DecodedPixels DecodeToFit(byte[] fileBytes, int maxWidth, int maxHeight)
        {
            using var stream = new MemoryStream(fileBytes, writable: false);
            var (width, height, orientation) = ReadHeader(stream);
            stream.Position = 0;

            // How much it has to shrink is decided by the size it has upright; what is decoded is the stored picture, which is turned afterwards
            var turned = orientation is >= 5 and <= 8;
            var uprightWidth = turned ? height : width;
            var uprightHeight = turned ? width : height;
            var scale = Math.Min(1d, Math.Min((double)maxWidth / uprightWidth, (double)maxHeight / uprightHeight));

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            if (scale < 1)
            {
                image.DecodePixelWidth = Math.Max(1, (int)Math.Round(width * scale));
                image.DecodePixelHeight = Math.Max(1, (int)Math.Round(height * scale));
            }
            image.EndInit();
            image.Freeze();

            BitmapSource source = image;
            if (OrientationTransform(orientation) is { } transform)
                source = new TransformedBitmap(source, transform);

            var pbgra = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var pixels = new byte[pbgra.PixelWidth * pbgra.PixelHeight * 4];
            pbgra.CopyPixels(pixels, pbgra.PixelWidth * 4, 0);
            return new DecodedPixels(pbgra.PixelWidth, pbgra.PixelHeight, pixels);
        }

        private static (int Width, int Height, int Orientation) ReadHeader(Stream stream)
        {
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);

            var orientation = 1;
            try
            {
                if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery(OrientationQuery))
                    orientation = Convert.ToInt32(metadata.GetQuery(OrientationQuery));
            }
            catch
            {
                // no readable EXIF block: the photo is shown as it is
            }
            return (frame.PixelWidth, frame.PixelHeight, orientation);
        }

        /// <summary>The transform that turns the stored pixels upright for an EXIF orientation value.</summary>
        private static Transform? OrientationTransform(int orientation) => orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null,
        };
    }
}
