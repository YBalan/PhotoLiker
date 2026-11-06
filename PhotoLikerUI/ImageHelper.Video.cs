using PhotoLiker.Video;

namespace PhotoLikerUI
{
    public static partial class ImageHelper
    {
        /// <summary>
        /// What the viewer shows for a video until it is played: its poster picture (kept in the catalog of the folder, made by ffmpeg the first time) in place
        /// of the photo, and the facts about the video in place of the EXIF rows. Safe to call from any thread; it waits for ffmpeg, so it belongs on a
        /// background thread, which is where the photo cache calls it.
        /// </summary>
        internal static PhotoEntry LoadVideoPoster(string path, int thumbSize)
        {
            var (record, poster) = ReadVideo(path);
            using var stream = new MemoryStream(poster, writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

            var full = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                using (var g = Graphics.FromImage(full))
                    g.DrawImage(source, 0, 0, source.Width, source.Height);

                var thumb = CreateThumbnail(full, thumbSize);
                return new PhotoEntry(full, thumb, thumbSize, VideoMetadata.Entries(record, new FileInfo(path)), backingStream: null)
                {
                    VideoDuration = record.Info is { DurationSeconds: > 0 } info ? info.Duration : null,
                };
            }
            catch
            {
                full.Dispose();
                throw;
            }
        }

        /// <summary>The thumbnail of a video alone (see <see cref="LoadVideoPoster"/>).</summary>
        internal static Bitmap LoadVideoThumbnail(string path, int thumbSize)
        {
            var (_, poster) = ReadVideo(path);
            using var stream = new MemoryStream(poster, writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return CreateThumbnail(source, thumbSize);
        }

        private static (VideoRecord Record, byte[] Poster) ReadVideo(string path)
        {
            var catalog = VideoLibrary.Current ?? throw new InvalidOperationException("The videos of this folder are not open.");
            var record = catalog.GetAsync(path).GetAwaiter().GetResult();
            if (record.Info is null)
                throw new InvalidDataException(record.Failure);

            return (record, catalog.GetPosterAsync(path).GetAwaiter().GetResult());
        }
    }
}
