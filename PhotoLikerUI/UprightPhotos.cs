namespace PhotoLikerUI
{
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;

    /// <summary>
    /// The photos of a video with the EXIF orientation applied, the way the app shows them. ffmpeg is not left to do it: it turns a
    /// photo that has an orientation (a portrait shot of a phone) and then, wrongly, the photos after it that have none or "as stored",
    /// so landscape photos came out as portrait (see <see cref="VideoGenerationService.BuildFfmpegArguments"/>). Each photo that has to
    /// be turned or mirrored is copied upright, and no bigger than the video frame (ffmpeg would make it that size anyway, and the
    /// copies stay small), into a temporary folder that goes away with this object. The other photos are used where they are.
    /// </summary>
    internal sealed class UprightPhotos : IDisposable
    {
        private const long JpegQuality = 95;

        private readonly string? _folder;

        private UprightPhotos(IReadOnlyList<string> files, string? folder, int turned, int failed)
        {
            Files = files;
            _folder = folder;
            Turned = turned;
            Failed = failed;
        }

        /// <summary>The photos in their order: the upright copy of those that had to be turned, the photo itself of the others.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>How many photos have an upright copy.</summary>
        public int Turned { get; }

        /// <summary>How many photos had to be turned but could not be copied; they are used as stored.</summary>
        public int Failed { get; }

        /// <param name="reportProgress">Percent of the photos that are looked at, from any thread.</param>
        public static Task<UprightPhotos> CreateAsync(IReadOnlyList<string> files, int frameWidth, int frameHeight, Action<int> reportProgress) =>
            Task.Run(() => Create(files, frameWidth, frameHeight, reportProgress));

        internal static UprightPhotos Create(IReadOnlyList<string> files, int frameWidth, int frameHeight, Action<int> reportProgress)
        {
            var result = new string[files.Count];
            string? folder = null;
            var gate = new object();
            int turned = 0, failed = 0, done = 0;

            try
            {
                // Looking at a photo only reads its start; a copy reads and decodes all of it, so not too many at once
                var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 6) };
                Parallel.For(0, files.Count, options, i =>
                {
                    result[i] = files[i];
                    try
                    {
                        if (IsTurned(files[i]))
                        {
                            string target;
                            lock (gate)
                            {
                                folder ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "photoliker_upright_" + Guid.NewGuid().ToString("N"))).FullName;
                                target = Path.Combine(folder, $"{i:D6}.jpg");
                            }

                            SaveUpright(files[i], target, frameWidth, frameHeight);
                            result[i] = target;
                            Interlocked.Increment(ref turned);
                        }
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref failed);
                    }

                    var count = Interlocked.Increment(ref done);
                    if (count == files.Count || count % 25 == 0)
                        reportProgress(count * 100 / files.Count);
                });
            }
            catch
            {
                Remove(folder);
                throw;
            }

            return new UprightPhotos(result, folder, turned, failed);
        }

        /// <summary>Whether the photo's EXIF orientation turns or mirrors it. Only the start of the file is read.</summary>
        private static bool IsTurned(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1 << 16);
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                return ImageHelper.GetRotateFlip(image) != RotateFlipType.RotateNoneFlipNone;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or UnauthorizedAccessException)
            {
                // Not a photo GDI+ knows: ffmpeg is left to say what it makes of it
                return false;
            }
        }

        private static void SaveUpright(string path, string target, int frameWidth, int frameHeight)
        {
            using var stream = new MemoryStream(FileReading.ReadAllBytesShared(path), writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

            var rotateFlip = ImageHelper.GetRotateFlip(source);
            var quarterTurn = rotateFlip is RotateFlipType.Rotate90FlipNone or RotateFlipType.Rotate270FlipNone
                or RotateFlipType.Rotate90FlipX or RotateFlipType.Rotate270FlipX;

            // Made small first, then turned: the size is the one the photo has upright
            var (uprightWidth, uprightHeight) = quarterTurn ? (source.Height, source.Width) : (source.Width, source.Height);
            var scale = Math.Min(1d, Math.Min((double)frameWidth / uprightWidth, (double)frameHeight / uprightHeight));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var copy = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(copy))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark rim at the edges
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            copy.RotateFlip(rotateFlip);

            var jpeg = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
            copy.Save(target, jpeg, parameters);
        }

        public void Dispose() => Remove(_folder);

        private static void Remove(string? folder)
        {
            if (folder is null)
                return;

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left in the temp folder; nothing to do about it here
            }
        }
    }
}
