using PhotoLiker.Drawing;
using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Turns the photo of a page in its file, for the Scan manuscripts window: the same as the Rotate buttons of the main window do for a JPEG (the turn goes into its Exif orientation, so the picture is not
    /// coded again and everything else the photo has stays; its Date Taken (Original) and the dates of its file become one moment: the date taken it has, else the date of its file), and for a photo of
    /// another kind (PNG, BMP, GIF, TIFF) its pixels are turned and written again with the notes of the photo and the dates of its file.
    /// </summary>
    internal static class PhotoTurner
    {
        /// <summary>What was done: <paramref name="Lossless"/> is true when only the Exif of a JPEG was changed.</summary>
        internal sealed record Turned(bool Lossless);

        /// <summary>Turns the photo by quarters (positive clockwise, negative counterclockwise); a whole number of circles changes nothing.</summary>
        /// <exception cref="NotSupportedException">The photo is no picture that Windows can read or write.</exception>
        /// <exception cref="IOException">The file could not be read or written; it is left as it was.</exception>
        public static Turned Turn(string file, int quarterTurns)
        {
            var turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0)
                return new Turned(Lossless: true);

            if (JpegOrientation.IsJpeg(file))
            {
                var dates = DateAlignment.DatesForSaving(file, DateTime.Now);
                try
                {
                    JpegOrientation.TurnClockwise(file, quarterTurns, dates?.FileUtc, dates is { WriteTaken: true } ? dates.Taken : null);
                    return new Turned(Lossless: true);
                }
                catch (InvalidDataException) when (dates is { WriteTaken: true })
                {
                    // The Exif has no room for the date (or is damaged where the date goes) and the file was not touched: a turn alone may still fit, and is worth more than the date
                    JpegOrientation.TurnClockwise(file, quarterTurns);
                    return new Turned(Lossless: true);
                }
                catch (InvalidDataException)
                {
                    // A damaged Exif: the pixels are turned below
                }
            }

            var decoded = WicPhotoDecoder.DecodeFull(File.ReadAllBytes(file));
            var turned = new Bitmap32(decoded.Width, decoded.Height, decoded.Bgra).Rotate(quarterTurns);
            PhotoConverter.SaveEdited(file, file, turned.Width, turned.Height, turned.Bgra);
            return new Turned(Lossless: false);
        }
    }
}
