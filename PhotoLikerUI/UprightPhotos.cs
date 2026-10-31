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
    /// <para>
    /// The same goes for files that are not all of one kind (say a PNG cover and JPEG photos): ffmpeg takes the decoder of the first for
    /// all and ends in a video of one frame. Then every file that is not a JPEG is copied as a JPEG, so that they are alike.
    /// </para>
    /// </summary>
    internal sealed class UprightPhotos : IDisposable
    {
        private const long JpegQuality = 95;

        private readonly string? _folder;

        private UprightPhotos(IReadOnlyList<string> files, string? folder, int turned, int converted, int failed)
        {
            Files = files;
            _folder = folder;
            Turned = turned;
            Converted = converted;
            Failed = failed;
        }

        /// <summary>The photos in their order: the upright copy of those that had to be turned, the photo itself of the others.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>How many photos have an upright copy.</summary>
        public int Turned { get; }

        /// <summary>How many photos have a JPEG copy only because the photos are not all JPEG files (and do not have to be turned).</summary>
        public int Converted { get; }

        /// <summary>How many photos had to be copied but could not be; they are used as stored.</summary>
        public int Failed { get; }

        /// <param name="reportProgress">
        /// Percent of the photos that are looked at, from any thread; again from 0 for the copies, if any have to be made.
        /// </param>
        public static Task<UprightPhotos> CreateAsync(IReadOnlyList<string> files, int frameWidth, int frameHeight, Action<int> reportProgress) =>
            Task.Run(() => Create(files, frameWidth, frameHeight, reportProgress));

        internal static UprightPhotos Create(IReadOnlyList<string> files, int frameWidth, int frameHeight, Action<int> reportProgress)
        {
            var result = new string[files.Count];
            var looks = new Look[files.Count];
            string? folder = null;
            var gate = new object();
            int turned = 0, converted = 0, failed = 0, done = 0;

            try
            {
                // Looking at a photo only reads its start; a copy reads and decodes all of it, so not too many at once
                var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 6) };
                Parallel.For(0, files.Count, options, i =>
                {
                    result[i] = files[i];
                    looks[i] = LookAt(files[i]);

                    var count = Interlocked.Increment(ref done);
                    if (count == files.Count || count % 25 == 0)
                        reportProgress(count * 100 / files.Count);
                });

                // Photos GDI+ does not know (WebP, HEIC) say nothing about the kind: they are left to ffmpeg either way
                var mixed = looks.Where(look => look.Format is not null).Select(look => look.Format).Distinct().Skip(1).Any();
                var copies = Enumerable.Range(0, files.Count)
                    .Where(i => looks[i].Turned || (mixed && looks[i].Format is { } format && format != ImageFormat.Jpeg.Guid))
                    .ToList();

                done = 0;
                Parallel.ForEach(copies, options, i =>
                {
                    try
                    {
                        string target;
                        lock (gate)
                        {
                            folder ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "photoliker_upright_" + Guid.NewGuid().ToString("N"))).FullName;
                            target = Path.Combine(folder, $"{i:D6}.jpg");
                        }

                        SaveUpright(files[i], target, frameWidth, frameHeight);
                        result[i] = target;
                        if (looks[i].Turned)
                            Interlocked.Increment(ref turned);
                        else
                            Interlocked.Increment(ref converted);
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref failed);
                    }

                    var count = Interlocked.Increment(ref done);
                    if (count == copies.Count || count % 25 == 0)
                        reportProgress(count * 100 / copies.Count);
                });
            }
            catch
            {
                Remove(folder);
                throw;
            }

            return new UprightPhotos(result, folder, turned, converted, failed);
        }

        /// <summary>How a photo is stored: the kind of file GDI+ takes it for (null: it does not know it), and whether it has to be turned.</summary>
        private readonly record struct Look(Guid? Format, bool Turned);

        /// <summary>Only the start of the file is read.</summary>
        private static Look LookAt(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1 << 16);
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                return new Look(image.RawFormat.Guid, ImageHelper.GetRotateFlip(image) != RotateFlipType.RotateNoneFlipNone);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or UnauthorizedAccessException)
            {
                // Not a photo GDI+ knows: ffmpeg is left to say what it makes of it
                return new Look(null, false);
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
