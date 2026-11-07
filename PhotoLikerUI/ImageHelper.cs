using PhotoLiker.Search;

namespace PhotoLikerUI
{
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.Runtime.InteropServices;

    public static partial class ImageHelper
    {
        private const int ExifOrientationId = 0x0112; // 274
        private const int DateTakenId = 0x9003;

        public static Image LoadImageWithCorrectOrientation(string path)
        {
            var img = Image.FromFile(path);

            var rotateFlip = GetRotateFlip(img);
            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                img.RotateFlip(rotateFlip);

            return img;
        }

        /// <summary>
        /// Reads and fully decodes a photo. The file is read in one go and closed again, so a flaky network
        /// share can't fail later in the middle of a paint, and the result is a private bitmap that doesn't
        /// depend on the file any more. Safe to call from any thread.
        /// </summary>
        internal static PhotoEntry LoadPhoto(string path, int thumbSize)
        {
            if (MediaKinds.IsVideo(path))
                return LoadVideoPoster(path, thumbSize);

            var stream = new MemoryStream(FileReading.ReadAllBytesShared(path), writable: false);
            Image? source = null;
            var handedOver = false;
            try
            {
                source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                var metadata = WithFileDate(path, ReadMetadata(source));

                if (ImageAnimator.CanAnimate(source))
                {
                    // Animated GIFs keep their frames, so the source image (and its stream) must stay alive.
                    var animatedThumb = CreateThumbnail(source, thumbSize);
                    var animated = new PhotoEntry(source, animatedThumb, thumbSize, metadata, stream);
                    handedOver = true;
                    return animated;
                }

                var full = CopyOriented(source);
                Bitmap thumb;
                try
                {
                    thumb = CreateThumbnail(full, thumbSize);
                }
                catch
                {
                    full.Dispose();
                    throw;
                }
                return new PhotoEntry(full, thumb, thumbSize, metadata, backingStream: null);
            }
            finally
            {
                if (!handedOver)
                {
                    source?.Dispose();
                    stream.Dispose();
                }
            }
        }

        /// <summary>Decodes a photo just to make its thumbnail. Safe to call from any thread.</summary>
        internal static Bitmap LoadThumbnail(string path, int thumbSize)
        {
            if (MediaKinds.IsVideo(path))
                return LoadVideoThumbnail(path, thumbSize);

            using var stream = new MemoryStream(FileReading.ReadAllBytesShared(path), writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

            var rotateFlip = GetRotateFlip(source);
            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                source.RotateFlip(rotateFlip);

            return CreateThumbnail(source, thumbSize);
        }

        /// <summary>
        /// Decodes a picture, upright, just to show it no bigger than <paramref name="maxSize"/> (it is not enlarged), and says how big it
        /// is in full. Safe to call from any thread.
        /// </summary>
        internal static (Bitmap Preview, Size FullSize) LoadPreview(string path, Size maxSize)
        {
            using var stream = new MemoryStream(FileReading.ReadAllBytesShared(path), writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

            var rotateFlip = GetRotateFlip(source);
            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                source.RotateFlip(rotateFlip);

            var scale = Math.Min(1d, Math.Min((double)maxSize.Width / source.Width, (double)maxSize.Height / source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            var preview = new Bitmap(width, height);
            try
            {
                using var g = Graphics.FromImage(preview);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, width, height);
                return (preview, new Size(source.Width, source.Height));
            }
            catch
            {
                preview.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads a photo for the slideshow: decoded, turned upright and scaled to fill as much of a screen of <paramref name="screen"/> pixels as it
        /// can with its proportions (a small photo is enlarged), so that showing it is only a copy of its pixels. The date it was taken is read as well,
        /// for the watermark, and, if <paramref name="peopleOf"/> is given, the line of names that goes with the photo. Safe to call from any thread
        /// (<paramref name="peopleOf"/> must be too).
        /// </summary>
        internal static SlideshowFrame LoadSlide(PhotoFile photo, Size screen, Func<string, string?>? peopleOf = null)
        {
            var bytes = FileReading.ReadAllBytesShared(photo.OriginalFilePath);
            var taken = TakenOf(photo, bytes);

            Bitmap frame;
            try
            {
                frame = SlideWithWic(bytes, screen);
            }
            catch
            {
                // A file the Windows imaging component will not take, which GDI+ (what the window reads with) may
                frame = SlideWithGdi(bytes, screen);
            }
            string? people = null;
            try
            {
                people = peopleOf?.Invoke(photo.OriginalFilePath);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The names are a trimming: a photo is shown without them rather than not at all
            }

            return new SlideshowFrame(frame, taken, photo.OriginalFilePath, people); // takes the bitmap: its pixels are copied into the frame's own canvas
        }

        /// <summary>
        /// The quick way: the decoder makes the photo of the size of the screen itself (a JPEG is decoded at that size straight away, about 40 ms
        /// for 12 MP where GDI+ needs 170 ms to decode and scale it, and it keeps up when several are made at once, which GDI+ does not).
        /// </summary>
        private static Bitmap SlideWithWic(byte[] bytes, Size screen)
        {
            var decoded = WicPhotoDecoder.DecodeToFit(bytes, screen.Width, screen.Height);

            var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppPArgb); // the format the screen draws fastest
            try
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, decoded.Width, decoded.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var rowBytes = decoded.Width * 4;
                    for (var y = 0; y < decoded.Height; y++)
                        Marshal.Copy(decoded.Pbgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return EnlargedToFit(bitmap, screen);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        /// <summary>The decoder never enlarges: a photo smaller than the screen is blown up here, from its few pixels, which is cheap.</summary>
        private static Bitmap EnlargedToFit(Bitmap photo, Size screen)
        {
            var scale = Math.Min((double)screen.Width / photo.Width, (double)screen.Height / photo.Height);
            if (scale <= 1)
                return photo;

            var width = Math.Max(1, (int)Math.Round(photo.Width * scale));
            var height = Math.Max(1, (int)Math.Round(photo.Height * scale));
            var enlarged = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            try
            {
                using (var g = Graphics.FromImage(enlarged))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(photo, 0, 0, width, height);
                }
                photo.Dispose();
                return enlarged;
            }
            catch
            {
                enlarged.Dispose();
                throw;
            }
        }

        private static Bitmap SlideWithGdi(byte[] bytes, Size screen)
        {
            // GDI+ reads the stream again while it draws, so nobody else may use this one
            using var stream = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

            var rotateFlip = GetRotateFlip(source);
            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                source.RotateFlip(rotateFlip);

            var scale = Math.Min((double)screen.Width / source.Width, (double)screen.Height / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            var frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            try
            {
                using var g = Graphics.FromImage(frame);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, width, height);
                return frame;
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }

        /// <summary>When the photo was taken by the rule the program uses everywhere (see <see cref="PhotoDates"/>): the camera's date if it is believable, else the date of the file.</summary>
        private static DateTime? TakenOf(PhotoFile photo, byte[] bytes)
        {
            var exif = ExifDates.None;
            if (JpegOrientation.IsJpeg(photo.OriginalFilePath))
            {
                using var stream = new MemoryStream(bytes, writable: false);
                exif = ExifReader.Read(stream);
            }

            return PhotoDates.Resolve(exif, PhotoDates.FileTime(photo.CreationTimeUtc), PhotoDates.FileTime(photo.LastWriteTimeUtc), DateTime.Now).Taken;
        }

        internal static Bitmap CreateThumbnail(Image source, int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            int w = (int)(source.Width * scale);
            int h = (int)(source.Height * scale);
            g.DrawImage(source, (size - w) / 2, (size - h) / 2, w, h);
            return bmp;
        }

        private static Bitmap CopyOriented(Image source)
        {
            var rotateFlip = GetRotateFlip(source);
            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (var g = Graphics.FromImage(copy))
                {
                    // 1:1 copy, no resampling
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(source, 0, 0, source.Width, source.Height);
                }

                if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                    copy.RotateFlip(rotateFlip);

                return copy;
            }
            catch
            {
                copy.Dispose();
                throw;
            }
        }

        /// <summary>The EXIF entries GDI+ has read from the image.</summary>
        private static IReadOnlyList<MetadataEntry> ReadMetadata(Image image) =>
            FriendlyImageMetadata.FromProperties(
                image.PropertyItems.Select(p => new RawExifProperty(p.Id, p.Type, p.Value))).Entries;

        /// <summary>
        /// The EXIF entries and, next to them, the dates of the file itself (read here, off the UI thread). A JPEG that has no Date Taken
        /// (Original) gets an empty row for it, so that the grid can give it one.
        /// </summary>
        private static IReadOnlyList<MetadataEntry> WithFileDate(string path, IReadOnlyList<MetadataEntry> exif)
        {
            if (JpegOrientation.IsJpeg(path) && !exif.Any(e => e.TagId == DateTakenId))
                exif = [.. exif, new MetadataEntry(DateTakenId, ExifTagMap.Tags[DateTakenId], ImageHelperStrings.NoDateShown)];

            return FileDateRow.Create(path, exif) is { } row ? [.. exif, row] : exif;
        }

        /// <summary>How the stored pixels are turned and mirrored to show the photo upright, by its EXIF orientation.</summary>
        internal static RotateFlipType GetRotateFlip(Image img)
        {
            if (!img.PropertyIdList.Contains(ExifOrientationId))
                return RotateFlipType.RotateNoneFlipNone;

            var prop = img.GetPropertyItem(ExifOrientationId);
            int orientation = BitConverter.ToUInt16(prop?.Value ?? new byte[2], 0);

            return orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
        }
    }
}
