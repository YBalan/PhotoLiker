namespace PhotoLiker.Core
{
    /// <summary>Setting the dates of a file that the app has just written, and letting a file written next to it take its place.</summary>
    public static class FileDates
    {
        private const int MaxMoveAttempts = 5;

        /// <summary>
        /// Sets the dates of the file through the handle it is written with, before it is closed. A network share (a Samba one, such as a router
        /// shows) moves the date of change to now when a handle that wrote is closed, a moment after a date that was set by name through another
        /// handle: it did, now and then, with the date set by name right after the write. A date set through the handle that wrote is kept.
        /// A drive that refuses this leaves the dates to be set by name afterwards (and read back, see <see cref="JpegExif.Save"/>).
        /// </summary>
        public static void SetOnHandle(FileStream stream, DateTime createdUtc, DateTime writtenUtc)
        {
            try
            {
                File.SetCreationTimeUtc(stream.SafeFileHandle, createdUtc);
                File.SetLastWriteTimeUtc(stream.SafeFileHandle, writtenUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // by name, afterwards
            }
        }

        /// <summary>
        /// Moves <paramref name="written"/> over <paramref name="path"/>. A network share now and then refuses ("access denied") to replace a file
        /// that something has just had open, such as the app itself a moment ago when it read the photo, and takes it a little later: the
        /// move is tried again a few times. A file that is read-only is not tried again, since waiting does not change that.
        /// </summary>
        public static void MoveOver(string written, string path)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(written, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when (attempt < MaxMoveAttempts && ex is IOException or UnauthorizedAccessException
                    && ex is not (FileNotFoundException or DirectoryNotFoundException)
                    && !(File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly)))
                {
                    Thread.Sleep(150 * attempt);
                }
            }
        }
    }
}
