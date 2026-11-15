using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Colorize
{
    /// <summary>
    /// The colorized copy of a photo: the photo itself is never changed. The copy is saved next to it as a JPEG called "name (colorized).jpg" (numbered when that name is taken: "name (colorized) 2.jpg"; a
    /// copy is never written over anything), with the Exif of the photo (the date taken, the place, the camera) and the dates of its file, so that it is listed beside the photo and found by the same search
    /// by date. The photo is read upright, so the copy is upright and says so.
    /// </summary>
    public static class ColorizedCopy
    {
        public const string Suffix = " (colorized)";

        /// <summary>The quality of the JPEG that is written (1 to 100): high, because the photo was decoded and is coded again.</summary>
        public const int Quality = 95;

        private const string OrientationQuery = "/app1/ifd/{ushort=274}";

        /// <summary>The name of the first copy of a photo: next to it, with the suffix, always a JPEG.</summary>
        public static string PathFor(string photoPath, string suffix = Suffix) =>
            Path.Combine(Path.GetDirectoryName(photoPath) ?? string.Empty, Path.GetFileNameWithoutExtension(photoPath) + suffix + ".jpg");

        /// <summary>The name for a new copy of a photo: <see cref="PathFor"/>, or the first of the numbered ones after it that is not taken ("... 2.jpg", "... 3.jpg").</summary>
        public static string NextFreePath(string photoPath, string suffix = Suffix)
        {
            var first = PathFor(photoPath, suffix);
            if (!File.Exists(first))
                return first;

            var folder = Path.GetDirectoryName(first) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(first);
            for (var number = 2; ; number++)
            {
                var candidate = Path.Combine(folder, $"{name} {number}.jpg");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        /// <summary>The copy a photo has already (the first name it would be given, or a numbered one), or null when it has none.</summary>
        public static string? ExistingCopy(string photoPath)
        {
            var first = PathFor(photoPath);
            if (File.Exists(first))
                return first;

            var folder = Path.GetDirectoryName(first) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(first);
            if (!Directory.Exists(folder))
                return null;

            return Directory.EnumerateFiles(folder, name + " *.jpg").FirstOrDefault(file => IsNumbered(Path.GetFileNameWithoutExtension(file), name));
        }

        private static bool IsNumbered(string fileName, string copyName) =>
            fileName.Length > copyName.Length + 1 && fileName.StartsWith(copyName + " ", StringComparison.OrdinalIgnoreCase) && fileName[(copyName.Length + 1)..].All(char.IsAsciiDigit);

        /// <summary>Whether a file is a colorized copy itself (its name ends with the suffix, perhaps with a number after it): a copy is not colorized again.</summary>
        public static bool IsCopy(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                return true;

            var space = name.LastIndexOf(' ');
            return space > 0 && space < name.Length - 1 && name[(space + 1)..].All(char.IsAsciiDigit) && name[..space].EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes <paramref name="colorized"/> (the photo with its colours, upright) as the copy of <paramref name="photoPath"/> and gives it the dates of the photo. The Exif of the photo goes with it when the
        /// photo is a JPEG. The file is written under another name first and moved into place, so a copy is whole or not there. Returns the path of the copy.
        /// </summary>
        /// <param name="original">The bytes of the photo, as they were read: where the Exif is taken from.</param>
        /// <exception cref="IOException">The copy could not be written.</exception>
        public static string Save(string photoPath, byte[] original, DecodedPhoto colorized, string suffix = Suffix) =>
            SaveTo(NextFreePath(photoPath, suffix), photoPath, original, colorized, overwrite: false);

        /// <summary>
        /// Writes the copy to <paramref name="path"/>, which the person chose (Save As): as <see cref="Save"/> does it, to a place of their own. A file that is there is written over when
        /// <paramref name="overwrite"/> says so (the person was asked), else it is an error. The copy is always a JPEG, whatever the extension of <paramref name="path"/> says.
        /// </summary>
        /// <param name="photoPath">The photo the copy is of: where the dates of the copy are taken from.</param>
        public static string SaveTo(string path, string photoPath, byte[] original, DecodedPhoto colorized, bool overwrite)
        {
            var partial = path + ".part";
            try
            {
                var bitmap = BitmapSource.Create(colorized.Width, colorized.Height, 96, 96, PixelFormats.Bgr24, null, colorized.Bgr, colorized.Width * 3);
                bitmap.Freeze();

                var written = false;
                if (MetadataOf(original) is { } metadata)
                {
                    try
                    {
                        Encode(bitmap, metadata, partial);
                        written = true;
                    }
                    catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                    {
                        // Exif that this encoder will not take: the copy is made without it
                    }
                }
                if (!written)
                    Encode(bitmap, null, partial);

                Retry(() => File.Move(partial, path, overwrite));
            }
            catch
            {
                try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }

            try
            {
                var created = File.GetCreationTimeUtc(photoPath);
                var written = File.GetLastWriteTimeUtc(photoPath);
                Retry(() =>
                {
                    File.SetCreationTimeUtc(path, created);
                    File.SetLastWriteTimeUtc(path, written);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the copy is there; it only keeps the date it was made
            }
            return path;
        }

        /// <summary>
        /// A file that has just been written is looked at by the virus scanner and the search indexer of Windows, which can hold it for a moment: moving it, or setting its dates, then fails
        /// with a sharing violation that is gone a moment later. So that is tried a few times before it is given up.
        /// </summary>
        private static void Retry(Action action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException && !IsPermanent(ex))
                {
                    Thread.Sleep(120 * attempt);
                }
            }
        }

        /// <summary>A file that is already there is no sharing violation: it will not be there less a moment later.</summary>
        private static bool IsPermanent(Exception ex) => ex is IOException && ex.HResult == unchecked((int)0x80070050); // ERROR_FILE_EXISTS

        /// <summary>The Exif and other metadata of a JPEG, ready to go with its upright picture (the orientation says that it is upright); null for a photo that is not a JPEG or has none.</summary>
        private static BitmapMetadata? MetadataOf(byte[] original)
        {
            try
            {
                using var stream = new MemoryStream(original, writable: false);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                if (decoder is not JpegBitmapDecoder || decoder.Frames[0].Metadata is not BitmapMetadata source)
                    return null;

                var metadata = source.Clone();
                if (metadata.ContainsQuery(OrientationQuery))
                    metadata.SetQuery(OrientationQuery, (ushort)1);
                return metadata;
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or IOException or FileFormatException or System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        private static void Encode(BitmapSource bitmap, BitmapMetadata? metadata, string path)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = Quality };
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
            using var file = File.Create(path);
            encoder.Save(file);
        }
    }
}
