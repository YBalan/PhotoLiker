using System.Globalization;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>The rows that describe a video in the metadata grid (the place where a photo has its EXIF), in the category "Video".</summary>
    public static class VideoMetadata
    {
        private const int Base = MetadataEntry.VideoTagBase;

        public const string DurationName = "Duration";
        public const string FrameSizeName = "Frame size";
        public const string FrameRateName = "Frame rate";
        public const string VideoCodecName = "Video codec";
        public const string AudioName = "Audio";
        public const string BitrateName = "Bitrate";
        public const string RecordedName = "Recorded";
        public const string DateFromName = "Date from";
        public const string LocationName = "Location";
        public const string EncoderName = "Encoder";
        public const string FileSizeName = "File size";
        public const string FileDatesName = "File dates";

        /// <summary>The rows of a video that could be read. The names of the rows are English, like those of the EXIF rows; what each means is in the language that is on.</summary>
        /// <param name="file">The file, for its dates: next to the date of recording they show whether the file is as old as the recording (a copy is not).</param>
        public static IReadOnlyList<MetadataEntry> Entries(VideoRecord record, FileInfo? file = null)
        {
            var info = record.Info ?? throw new ArgumentException("The video could not be read.", nameof(record));
            var rows = new List<MetadataEntry>
            {
                Row(0, DurationName, DurationText(info.DurationSeconds), VideoLibStrings.DescDuration),
                Row(1, FrameSizeName, SizeText(info), VideoLibStrings.DescFrameSize),
                Row(2, FrameRateName, info.FramesPerSecond > 0 ? string.Create(CultureInfo.InvariantCulture, $"{info.FramesPerSecond:0.##} fps") : "?", VideoLibStrings.DescFrameRate),
                Row(3, VideoCodecName, info.VideoCodec, VideoLibStrings.DescVideoCodec),
                Row(4, AudioName, info.Audio ?? VideoLibStrings.NoAudio, VideoLibStrings.DescAudio),
                Row(5, BitrateName, BitrateText(info.BitsPerSecond), VideoLibStrings.DescBitrate),
                Row(6, RecordedName, record.Date.Taken is { } taken ? taken.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : VideoLibStrings.DateNone, VideoLibStrings.DescRecorded),
                Row(7, DateFromName, record.Date.Source switch { DateSource.Exif => VideoLibStrings.DateFromVideo, DateSource.File => VideoLibStrings.DateFromFile, _ => VideoLibStrings.DateNone }, VideoLibStrings.DescDateFrom),
                Row(10, FileSizeName, SizeOfFile(record.Length), VideoLibStrings.DescFileSize),
            };

            if (file is { Exists: true })
                rows.Add(Row(11, FileDatesName, FileDateRow.Describe(file.CreationTime, file.LastWriteTime, record.Date.Taken, RecordedName), VideoLibStrings.DescFileDates));
            if (info.Latitude is { } latitude && info.Longitude is { } longitude)
                rows.Add(Row(8, LocationName, string.Create(CultureInfo.InvariantCulture, $"{latitude:0.####}, {longitude:0.####}"), VideoLibStrings.DescLocation));
            if (!string.IsNullOrWhiteSpace(info.Encoder))
                rows.Add(Row(9, EncoderName, info.Encoder, VideoLibStrings.DescEncoder));

            return rows;
        }

        private static MetadataEntry Row(int number, string name, string value, string description) =>
            new(Base + number, name, value, Description: description);

        internal static string DurationText(double seconds) =>
            seconds <= 0 ? "?" : string.Create(CultureInfo.InvariantCulture, $"{VideoTime.Format(TimeSpan.FromSeconds(seconds))} ({seconds:0.#} s)");

        private static string SizeText(VideoInfo info)
        {
            var shown = string.Create(CultureInfo.InvariantCulture, $"{info.DisplayWidth} × {info.DisplayHeight}");
            return info.Turned ? string.Create(CultureInfo.InvariantCulture, $"{shown} (stored {info.Width} × {info.Height}, turned {info.Rotation}°)") : shown;
        }

        internal static string BitrateText(long bitsPerSecond) => bitsPerSecond switch
        {
            <= 0 => "?",
            >= 1_000_000 => string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1_000_000d:0.#} Mb/s"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1000d:0} kb/s"),
        };

        internal static string SizeOfFile(long bytes) => bytes switch
        {
            >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.##} GB"),
            >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.#} MB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0} KB"),
        };
    }
}
