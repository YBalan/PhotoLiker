namespace PhotoLikerUI
{
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;

    public static class ImageHelper
    {
        private const int ExifOrientationId = 0x0112; // 274

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
            using var stream = new MemoryStream(FileReading.ReadAllBytesShared(path), writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

            var rotateFlip = GetRotateFlip(source);
            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                source.RotateFlip(rotateFlip);

            return CreateThumbnail(source, thumbSize);
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

        /// <summary>The EXIF entries and, next to them, the dates of the file itself (read here, off the UI thread).</summary>
        private static IReadOnlyList<MetadataEntry> WithFileDate(string path, IReadOnlyList<MetadataEntry> exif) =>
            FileDateRow.Create(path, exif) is { } row ? [.. exif, row] : exif;

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
