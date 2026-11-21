using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoLiker.Imaging
{
    /// <summary>A photo as a JPEG file's bytes, and the size of the picture in it.</summary>
    public sealed record JpegPicture(byte[] Bytes, int Width, int Height);

    /// <summary>
    /// Photos for a book (a PDF or an EPUB): each as an upright JPEG that is no bigger than the book needs. A photo is read at the reduced size straight away (a 24 MP JPEG costs about what a 2 MP one
    /// does), turned by its EXIF orientation (a book has no orientation to say how a picture is to be turned), put on white where it is transparent, and written again; what the camera wrote in
    /// the file (the Exif, the thumbnail, the GPS place) is not carried over: a book is something that is given away. Safe to call from any thread.
    /// </summary>
    public static class BookPictures
    {
        /// <summary>
        /// The photo (a file of any format Windows reads) as a JPEG of at most <paramref name="maxLongSide"/> pixels on its long side (a smaller photo keeps its size).
        /// </summary>
        /// <param name="quality">The JPEG quality, 1 to 100.</param>
        /// <param name="rotation">How much the book turns the photo, clockwise, on top of the way it is upright: 90, 180 or 270 degrees (any other number is brought into 0 to 270 and rounded down to a quarter).</param>
        /// <exception cref="NotSupportedException">The bytes are no picture that Windows can read.</exception>
        public static JpegPicture ToJpeg(byte[] original, int maxLongSide, int quality, int rotation = 0)
        {
            DecodedPixels pixels;
            try
            {
                pixels = WicPhotoDecoder.DecodeToFit(original, maxLongSide, maxLongSide);
            }
            catch (Exception ex) when (ex is FileFormatException or InvalidOperationException or COMException or ArgumentException or NotSupportedException or IOException) // the stream is in memory, so an IOException is the decoder not making a picture of it
            {
                throw new NotSupportedException("This is not a picture that Windows can read.", ex);
            }

            pixels = Rotated(pixels, rotation);
            var picture = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32, null, pixels.Pbgra, pixels.Width * 4);
            BitmapSource opaque = PhotoConverter.OnWhite(picture);

            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            using var output = new MemoryStream();
            encoder.Save(output);
            return new JpegPicture(output.ToArray(), pixels.Width, pixels.Height);
        }

        /// <summary>The same for a file; the file is read once, whole.</summary>
        public static JpegPicture ToJpeg(string path, int maxLongSide, int quality, int rotation = 0) => ToJpeg(File.ReadAllBytes(path), maxLongSide, quality, rotation);

        /// <summary>The pixels turned clockwise by 90, 180 or 270 degrees (a quarter turn puts the photo on its side: the width and the height change places); the same pixels for 0.</summary>
        internal static DecodedPixels Rotated(DecodedPixels pixels, int degrees)
        {
            var quarters = (((degrees % 360) + 360) % 360) / 90;
            if (quarters == 0)
                return pixels;

            var width = pixels.Width;
            var height = pixels.Height;
            var sideways = quarters != 2;
            var rotated = new byte[pixels.Pbgra.Length];
            var newWidth = sideways ? height : width;

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Where the pixel at (x, y) lands: a quarter clockwise puts the top row on the right side
                var (nx, ny) = quarters switch
                {
                    1 => (height - 1 - y, x),
                    2 => (width - 1 - x, height - 1 - y),
                    _ => (y, width - 1 - x),
                };
                Buffer.BlockCopy(pixels.Pbgra, (y * width + x) * 4, rotated, (ny * newWidth + nx) * 4, 4);
            }

            return new DecodedPixels(newWidth, sideways ? width : height, rotated);
        }
    }
}
