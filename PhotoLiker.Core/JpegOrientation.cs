namespace PhotoLiker.Core
{
    /// <summary>
    /// Turns a JPEG by changing its EXIF orientation, the number that tells every viewer how the stored pixels are turned to show the
    /// photo upright (this app, Windows, phones and browsers all follow it). The picture data is never decoded or written again, so
    /// nothing is lost however often a photo is turned, and a large photo on a network share costs two bytes instead of a new file.
    ///
    /// A photo that has the orientation already (every phone and most cameras write it, also when it is 1) gets the new number in
    /// the same place: its size does not change. A photo without it gets it added, which makes the Exif block larger and the file is
    /// written anew (see <see cref="JpegExif"/>). In both cases the dates of the file are set again afterwards (writing moves them to
    /// now), to the ones it had or to the date it is given, because the app knows a photo by its size and date (see <see cref="CopyName"/>).
    /// </summary>
    public static class JpegOrientation
    {
        // The orientation a photo gets when it is turned a quarter clockwise, by the orientation it has (index 0 is not an orientation):
        // 1 normal -> 6, 6 -> 3 (upside down) -> 8 -> 1; the mirrored ones 2 -> 7 -> 4 -> 5 -> 2
        private static readonly int[] QuarterClockwise = [0, 6, 7, 8, 5, 2, 3, 4, 1];

        public static bool IsJpeg(string path) =>
            Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

        /// <summary>The orientation of a photo that is turned clockwise by quarter turns. A number that is not an orientation is taken for 1 (nothing).</summary>
        public static int Turn(int orientation, int clockwiseQuarterTurns)
        {
            if (orientation is < 1 or > 8)
                orientation = 1;

            for (var turns = ((clockwiseQuarterTurns % 4) + 4) % 4; turns > 0; turns--)
                orientation = QuarterClockwise[orientation];
            return orientation;
        }

        /// <summary>The orientation a JPEG has, or 1 when it says none.</summary>
        /// <exception cref="InvalidDataException">Not a JPEG, or its Exif block is damaged.</exception>
        public static int Read(Stream jpeg) => JpegExif.Locate(jpeg).Orientation ?? 1;

        /// <summary>
        /// Turns the photo a quarter clockwise (or by <paramref name="quarterTurns"/>, negative ones counterclockwise) for everyone who shows
        /// it upright. The date of change and of creation of the file stay as they were, or both become <paramref name="fileDatesUtc"/> if
        /// that is given (the date taken, for a photo that has one). <paramref name="dateTaken"/>, if given, is written into the photo
        /// as its Date Taken (Original), added if it has none. A photo that is not turned (no turns, or a full circle that lands on the same
        /// orientation) and has no date to write is not touched, except for the dates of the file, if they are given.
        /// </summary>
        /// <exception cref="InvalidDataException">Not a JPEG, its Exif block is damaged or has no room for the orientation. The file is not touched.</exception>
        /// <exception cref="IOException">The file could not be read or written, or another program changed it meanwhile. The file is left as it was.</exception>
        public static ExifSaveResult TurnClockwise(string path, int quarterTurns = 1, DateTime? fileDatesUtc = null, DateTime? dateTaken = null) =>
            JpegExif.Save(path, new ExifChange { QuarterTurns = quarterTurns, Original = dateTaken }, fileDatesUtc);
    }
}
