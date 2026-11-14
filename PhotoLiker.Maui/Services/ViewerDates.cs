using PhotoLiker.Core;

namespace PhotoLiker.Maui.Services;

/// <summary>
/// The date of a photo as the light viewer knows it: from the times of its file (the MAUI app reads no EXIF yet,
/// unlike the WinForms app which also asks its search index). Feeds <see cref="MonthNavigation"/> for PageUp / PageDown.
/// </summary>
public static class ViewerDates
{
    /// <summary>When the photo was taken, by the file: its creation time, else its last change, as a believable local date; null when neither is believable.</summary>
    public static DateTime? TakenOf(PhotoFile file, DateTime now) =>
        Believable(file.CreationTimeUtc, now) ?? Believable(file.LastWriteTimeUtc, now);

    /// <summary>The month of the photo as <see cref="MonthNavigation.MonthOf"/> counts it; null for a photo without a believable date.</summary>
    public static int? MonthOf(PhotoFile file, DateTime now) => MonthNavigation.MonthOf(TakenOf(file, now));

    // A file without a time reads as 1601; a camera with a reset clock writes 1980 or earlier; nothing is taken in the future.
    private static DateTime? Believable(DateTime utc, DateTime now)
    {
        if (utc.Year <= 1700)
            return null;

        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.Year > 1980 && local <= now.AddDays(1) ? local : null;
    }
}
