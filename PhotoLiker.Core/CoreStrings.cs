using System.Runtime.CompilerServices;

namespace PhotoLiker.Core
{
    /// <summary>Messages of the library that reach the person: reasons shown in lists, errors shown in the status line.</summary>
    internal static class CoreStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(CoreStrings), key);

        public static string NoDate => T();
        public static string EmptyFileReason => T();
        public static string NotThereAnyMore => T();
        public static string FolderHoldsFolderFormat => T();
        public static string FolderHoldsFileFormat => T();
        public static string FolderNotFoundFormat => T();
        public static string FolderUnreadableFormat => T();
        public static string ReasonKeepFolder => T();
        public static string ReasonMoveFolder => T();
        public static string ReasonInsideFormat => T();
        public static string ReasonDatedFormat => T();
        public static string ReasonNamedFormat => T();
        public static string ReasonCounterFormat => T();
        public static string ReasonNoFolder => T();
        public static string ReasonNamedLikeCopy => T();
        public static string KeeperChanged => T();
        public static string CopyChanged => T();
        public static string NotDuplicateLog => T();
        public static string ChangedMeanwhile => T();
        public static string ExifNoRoom => T();
        public static string OrientationNotNumber => T();
        public static string ExifDamaged => T();
        public static string NotJpeg => T();
        public static string JpegDamaged => T();
        public static string ExifTooShort => T();
        public static string ExifByteOrder => T();
        public static string ExifNotTiff => T();
        public static string FfmpegFailedFormat => T();
        public static string FfmpegNoOutputFormat => T();
        public static string FfmpegNotFound => T();
        public static string TooManyRepeatsFormat => T();
        public static string AudioUnreadable => T();
        public static string DurationSecondsFormat => T();
    }
}
