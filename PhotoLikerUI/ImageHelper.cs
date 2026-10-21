namespace PhotoLikerUI
{
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;

    public static class ImageHelper
    {
        private const int ExifOrientationId = 0x0112; // 274
        private const int MaxReadAttempts = 3;

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
            var stream = new MemoryStream(ReadAllBytesShared(path), writable: false);
            Image? source = null;
            var handedOver = false;
            try
            {
                source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                var metadata = new FriendlyImageMetadata(source).Entries;

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
            using var stream = new MemoryStream(ReadAllBytesShared(path), writable: false);
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

        private static RotateFlipType GetRotateFlip(Image img)
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

        /// <summary>Reads a whole file, retrying a couple of times on transient I/O errors (network shares).</summary>
        private static byte[] ReadAllBytesShared(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var file = new FileStream(
                        path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, bufferSize: 1 << 16, FileOptions.SequentialScan);
                    var buffer = new byte[file.Length];
                    file.ReadExactly(buffer);
                    return buffer;
                }
                catch (IOException ex) when (attempt < MaxReadAttempts && ex is not (FileNotFoundException or DirectoryNotFoundException))
                {
                    Thread.Sleep(300 * attempt);
                }
            }
        }
    }
}
