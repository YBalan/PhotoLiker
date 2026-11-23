using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoLiker.Imaging
{
    /// <summary>The picture formats a photo can be saved as: the ones Windows writes by itself (WebP and HEIC can be read, but there is no encoder for them).</summary>
    public enum PhotoFormat
    {
        Jpeg,
        Png,
        Bmp,
        Gif,
        Tiff,
    }

    /// <summary>What the program knows about the formats a photo can be converted to: how they are named, and which one a file name says.</summary>
    public static class PhotoFormats
    {
        /// <summary>Every format, in the order the Save As window lists them.</summary>
        public static IReadOnlyList<PhotoFormat> All { get; } = [PhotoFormat.Jpeg, PhotoFormat.Png, PhotoFormat.Bmp, PhotoFormat.Gif, PhotoFormat.Tiff];

        /// <summary>The format a file name (or just an extension) says it is, or null for a name that is none of them.</summary>
        public static PhotoFormat? ForPath(string? path) => Path.GetExtension(path)?.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => PhotoFormat.Jpeg,
            ".png" => PhotoFormat.Png,
            ".bmp" => PhotoFormat.Bmp,
            ".gif" => PhotoFormat.Gif,
            ".tif" or ".tiff" => PhotoFormat.Tiff,
            _ => null,
        };

        /// <summary>The extension a file of the format is given when the name has none: the ones the program lists photos by (its default list has <c>.tiff</c>, not <c>.tif</c>).</summary>
        public static string Extension(PhotoFormat format) => format switch
        {
            PhotoFormat.Jpeg => ".jpg",
            PhotoFormat.Png => ".png",
            PhotoFormat.Bmp => ".bmp",
            PhotoFormat.Gif => ".gif",
            _ => ".tiff",
        };

        /// <summary>The kind of file as the "Save as type" list of a Save As window says it: the text, a bar, and the patterns (the first one is what a name without an extension gets).</summary>
        public static string Filter(PhotoFormat format) => format switch
        {
            PhotoFormat.Jpeg => "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            PhotoFormat.Png => "PNG (*.png)|*.png",
            PhotoFormat.Bmp => "BMP (*.bmp)|*.bmp",
            PhotoFormat.Gif => "GIF (*.gif)|*.gif",
            _ => "TIFF (*.tiff;*.tif)|*.tiff;*.tif",
        };

        /// <summary>The name with the extension of <paramref name="fallback"/> added unless it already says a format (<c>x.png</c> stays, <c>x.xyz</c> becomes <c>x.xyz.jpg</c> for a JPEG).</summary>
        public static string WithExtension(string path, PhotoFormat fallback) => ForPath(path) is null ? path + Extension(fallback) : path;
    }

    /// <summary>
    /// Turns a photo into a file of another format: the whole picture, at its full size, turned upright (a PNG has no orientation to say how it is to be turned), with what can go with it: the colour
    /// profile and the date taken, the camera, the title, the author and the copyright where the format has room for them. The file dates are the ones of the photo, because this program sorts and
    /// searches by date. Safe to call from any thread.
    /// </summary>
    public static class PhotoConverter
    {
        /// <summary>The quality of a JPEG that is written (1 to 100): high, because the photo was decoded and is coded again.</summary>
        public const int JpegQuality = 95;

        /// <summary>
        /// Writes <paramref name="source"/> as a file of the format that the extension of <paramref name="destination"/> says (a file that is there is written over: the person was asked) and gives it the dates of
        /// <paramref name="source"/>. The file is written under another name first and moved into place, so a picture is whole or not there. Returns <paramref name="destination"/>.
        /// </summary>
        /// <exception cref="NotSupportedException">The extension is no format that can be written.</exception>
        /// <exception cref="IOException">The file could not be read or written.</exception>
        public static string Convert(string source, string destination)
        {
            var format = PhotoFormats.ForPath(destination) ?? throw new NotSupportedException($"A picture cannot be saved as {Path.GetExtension(destination)}.");
            var picture = Encode(File.ReadAllBytes(source), format);
            Place(picture, destination, File.GetCreationTimeUtc(source), File.GetLastWriteTimeUtc(source));
            return destination;
        }

        /// <summary>
        /// Saves a picture that was made from the photo <paramref name="photo"/> (drawn on, cropped; straight blue, green, red, alpha pixels) as the file <paramref name="destination"/>, of the format its extension says
        /// (the photo itself when the two are the same: the picture is written over it), with the notes of the photo and its file dates. Written under another name first and moved into place, so a picture is whole or not there.
        /// Returns <paramref name="destination"/>.
        /// </summary>
        /// <exception cref="NotSupportedException">The extension is no format that can be written, or the photo is no picture that Windows can read.</exception>
        /// <exception cref="IOException">The file could not be read or written.</exception>
        public static string SaveEdited(string photo, string destination, int width, int height, byte[] bgra)
        {
            var format = PhotoFormats.ForPath(destination) ?? throw new NotSupportedException($"A picture cannot be saved as {Path.GetExtension(destination)}.");
            var picture = EncodeEdited(File.ReadAllBytes(photo), width, height, bgra, format);
            Place(picture, destination, File.GetCreationTimeUtc(photo), File.GetLastWriteTimeUtc(photo));
            return destination;
        }

        /// <summary>Writes the file under another name and moves it into place (over a file that is there), then gives it the dates it is to have.</summary>
        private static void Place(byte[] picture, string destination, DateTime createdUtc, DateTime writtenUtc)
        {
            var partial = destination + ".part";
            try
            {
                File.WriteAllBytes(partial, picture);
                Retry(() => File.Move(partial, destination, overwrite: true));
            }
            catch
            {
                try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }

            try
            {
                Retry(() =>
                {
                    File.SetCreationTimeUtc(destination, createdUtc);
                    File.SetLastWriteTimeUtc(destination, writtenUtc);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the picture is there; it only keeps the date it was made
            }
        }

        /// <summary>The bytes of the photo (a file of any format Windows reads) as a file of <paramref name="format"/>.</summary>
        /// <exception cref="NotSupportedException">The bytes are no picture that Windows can read.</exception>
        public static byte[] Encode(byte[] original, PhotoFormat format) => Encode(original, format, null);

        /// <summary>
        /// A picture that was made from the photo (drawn on, cropped) as a file of <paramref name="format"/>, with the notes and the colour profile of the photo it was made from: the date taken, the camera,
        /// the author. The pixels are straight blue, green, red, alpha, upright (the orientation of the photo is not carried over: it would turn the picture again).
        /// </summary>
        /// <exception cref="NotSupportedException"><paramref name="original"/> is no picture that Windows can read.</exception>
        public static byte[] EncodeEdited(byte[] original, int width, int height, byte[] bgra, PhotoFormat format)
        {
            ArgumentNullException.ThrowIfNull(bgra);
            if (bgra.Length != width * height * 4)
                throw new ArgumentException($"A picture of {width} by {height} has {width * height * 4} bytes, not {bgra.Length}.", nameof(bgra));

            var edited = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
            edited.Freeze();
            return Encode(original, format, edited);
        }

        private static byte[] Encode(byte[] original, PhotoFormat format, BitmapSource? edited)
        {
            using var input = new MemoryStream(original, writable: false);
            BitmapFrame frame;
            try
            {
                frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            }
            catch (Exception ex) when (ex is FileFormatException or InvalidOperationException or COMException or ArgumentException)
            {
                throw new NotSupportedException("This is not a picture that Windows can read.", ex);
            }

            // The orientation is done on the pixels: the new file says nothing of it (it would be turned twice); a picture that was made from the photo has been turned upright already
            BitmapSource picture = edited ?? frame;
            if (edited is null && WicPhotoDecoder.OrientationTransform(WicPhotoDecoder.OrientationOf(frame)) is { } turn)
                picture = new TransformedBitmap(frame, turn);

            // A format with no room for transparency shows it as white, not black
            if (format is PhotoFormat.Jpeg or PhotoFormat.Bmp && HasAlpha(picture))
                picture = OnWhite(picture);

            var withMetadata = format is PhotoFormat.Jpeg or PhotoFormat.Png or PhotoFormat.Tiff;
            var metadata = withMetadata ? CarriedMetadata(frame, format) : null;
            var profiles = withMetadata ? frame.ColorContexts : null;
            try
            {
                return Write(format, picture, metadata, profiles);
            }
            catch (Exception ex) when ((metadata is not null || profiles is not null) && ex is NotSupportedException or ArgumentException or InvalidOperationException or COMException)
            {
                return Write(format, picture, null, null); // a profile or a note that this encoder will not take: the picture is written without it
            }
        }

        private static byte[] Write(PhotoFormat format, BitmapSource picture, BitmapMetadata? metadata, ReadOnlyCollection<ColorContext>? profiles)
        {
            BitmapEncoder encoder = format switch
            {
                PhotoFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = JpegQuality },
                PhotoFormat.Png => new PngBitmapEncoder(),
                PhotoFormat.Bmp => new BmpBitmapEncoder(),
                PhotoFormat.Gif => new GifBitmapEncoder(),
                _ => new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw },
            };
            encoder.Frames.Add(BitmapFrame.Create(picture, null, metadata, profiles));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }

        /// <summary>
        /// The notes of the Exif of a photo that go with it to another format: the dates (taken, digitized, changed), the camera, the description, the artist and the copyright. They are the text
        /// the camera wrote, moved as it is: the date a metadata object gives as a property is a string in the form of the culture, with two digits for the year (1925 would come back as 2025).
        /// Where: <c>""</c> is the main directory of the Exif, <c>"/exif"</c> its Exif directory.
        /// </summary>
        private static readonly (string Where, ushort Tag)[] Notes =
        [
            ("/exif", 0x9003), // DateTimeOriginal: when the picture was taken
            ("/exif", 0x9004), // DateTimeDigitized
            ("", 0x0132),      // DateTime
            ("", 0x010F),      // Make
            ("", 0x0110),      // Model
            ("", 0x010E),      // ImageDescription
            ("", 0x013B),      // Artist
            ("", 0x8298),      // Copyright
        ];

        /// <summary>
        /// The notes of a photo (see <see cref="Notes"/>) ready for the new file; only photos with an Exif block (a JPEG or a TIFF) have any, and only a JPEG, a TIFF and a PNG can hold them. Null when there
        /// are none to move, or they cannot be moved.
        /// </summary>
        private static BitmapMetadata? CarriedMetadata(BitmapFrame frame, PhotoFormat format)
        {
            if (frame.Metadata is not BitmapMetadata source)
                return null;

            try
            {
                var from = source.Format switch { "jpg" => "/app1/ifd", "tiff" => "/ifd", _ => null };
                if (from is null)
                    return null;

                var to = format switch { PhotoFormat.Jpeg => "/app1/ifd", PhotoFormat.Tiff => "/ifd", _ => null };
                var target = new BitmapMetadata(format switch { PhotoFormat.Jpeg => "jpg", PhotoFormat.Png => "png", _ => "tiff" });
                var carried = false;
                foreach (var (where, tag) in Notes)
                {
                    var query = $"{from}{where}/{{ushort={tag}}}";
                    if (!source.ContainsQuery(query) || source.GetQuery(query) is not string raw || raw.Trim('\0', ' ').Length == 0)
                        continue;

                    try
                    {
                        if (to is not null)
                            target.SetQuery($"{to}{where}/{{ushort={tag}}}", raw);
                        else
                            CarryToPng(target, tag, raw.Trim('\0', ' '));
                        carried = true;
                    }
                    catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or FormatException or COMException)
                    {
                        // this note has no place in the new file: the rest goes on
                    }
                }
                return carried ? target : null;
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or COMException)
            {
                return null; // the photo is saved without the notes; it is the picture that was asked for
            }
        }

        /// <summary>A PNG keeps its notes as text with names of their own, which the metadata object has properties for (the date there is written in a form that cannot be misread).</summary>
        private static void CarryToPng(BitmapMetadata target, ushort tag, string text)
        {
            switch (tag)
            {
                case 0x9003 when DateTime.TryParseExact(text, ExifDateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var taken):
                    target.DateTaken = taken.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 0x010F:
                    target.CameraManufacturer = text;
                    break;
                case 0x0110:
                    target.CameraModel = text;
                    break;
                case 0x010E:
                    target.Title = text;
                    break;
                case 0x013B:
                    target.Author = new ReadOnlyCollection<string>([text]);
                    break;
                case 0x8298:
                    target.Copyright = text;
                    break;
                default:
                    throw new NotSupportedException("A PNG has no place for this note.");
            }
        }

        private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

        private static bool HasAlpha(BitmapSource picture)
        {
            var format = picture.Format;
            if (format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64 || format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float)
                return true;

            return picture.Palette?.Colors.Any(colour => colour.A < 255) == true; // an indexed picture (a GIF, a PNG with a palette) has its transparency in the palette
        }

        /// <summary>The picture with its transparent parts white (a half transparent pixel is a mix of its colour and white).</summary>
        internal static BitmapSource OnWhite(BitmapSource picture)
        {
            var width = picture.PixelWidth;
            var height = picture.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            new FormatConvertedBitmap(picture, PixelFormats.Pbgra32, null, 0).CopyPixels(pixels, stride, 0);

            for (var i = 0; i < pixels.Length; i += 4)
            {
                var gap = (byte)(255 - pixels[i + 3]); // the colours are multiplied by the alpha already, so the white that is missing is added
                pixels[i] += gap;
                pixels[i + 1] += gap;
                pixels[i + 2] += gap;
                pixels[i + 3] = 255;
            }

            var result = BitmapSource.Create(width, height, picture.DpiX, picture.DpiY, PixelFormats.Bgr32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        /// <summary>A file that has just been written is looked at by the virus scanner and the indexer of Windows, which can hold it for a moment: moving it, or setting its dates, then fails for a moment.</summary>
        private static void Retry(Action action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(120 * attempt);
                }
            }
        }
    }
}
