using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoLiker.GenerateVideo
{
    /// <summary>
    /// What the Generate Video dialog was set to for a video, kept in the folder the video was made for (see <see cref="VideoSettingsFile"/>)
    /// so that the next video of that folder starts from the same values. Every value is optional: one the file does not have (an older or
    /// a hand-made file) leaves the dialog as it is; a path that is "" means none (the audio or the cover is cleared). The folder of the
    /// photos is not in it: it is the folder the file is in.
    /// </summary>
    public sealed class VideoSettings
    {
        public const int CurrentVersion = 1;

        /// <summary>Which layout of this file it is; a file of a newer version is read for what this version knows of it.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>When it was written, for whoever looks at the file.</summary>
        public DateTime? SavedUtc { get; set; }

        /// <summary>The order of the photos, by the name of a <see cref="PhotoSortOrder"/> (a name this version does not know is ignored).</summary>
        public string? SortOrder { get; set; }

        public string? OutputFolder { get; set; }

        public string? OutputFileName { get; set; }

        public string? AudioPath { get; set; }

        /// <summary>Photos: true = Repeat (until a longer audio ends), false = Once.</summary>
        public bool? RepeatPhotos { get; set; }

        /// <summary>Audio: true = Repeat (over and over when it is shorter than the video), false = Once.</summary>
        public bool? RepeatAudio { get; set; }

        public string? CoverPath { get; set; }

        public bool? CoverAtStart { get; set; }

        public double? CoverStartSeconds { get; set; }

        public bool? CoverAtEnd { get; set; }

        public double? CoverEndSeconds { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public double? StartFrameTimeSeconds { get; set; }

        public double? EndFrameTimeSeconds { get; set; }

        public bool? UseRandom { get; set; }

        public string? Codec { get; set; }

        public int? Crf { get; set; }

        /// <summary>The experimental "Cut photos on the beat".</summary>
        public bool? CutOnBeat { get; set; }

        /// <summary>How many times ÷2 (negative) or ×2 (positive) was pressed for the beats found.</summary>
        public int? TempoOctaves { get; set; }

        /// <summary>The sort order, if the file has one this version knows.</summary>
        public PhotoSortOrder? GetSortOrder() =>
            Enum.TryParse<PhotoSortOrder>(SortOrder, ignoreCase: true, out var order) && Enum.IsDefined(order) ? order : null;
    }

    /// <summary>
    /// The <see cref="VideoSettings"/> file of a folder: <c>photo-liker-video.json</c>, next to the photos the video is made of. The paths in
    /// it (the output folder, the audio, the cover) are stored relative to that folder when they are below it or beside it, so a folder that
    /// is moved, or opened from another machine or drive letter, keeps working (see <see cref="StoredPaths"/>).
    /// </summary>
    public static class VideoSettingsFile
    {
        public const string FileName = "photo-liker-video.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string GetPath(string folder) => Path.Combine(folder, FileName);

        /// <summary>
        /// The settings of the folder, with the paths made full, or null when it has none or its file cannot be read (no file, no access,
        /// not JSON, a value of the wrong kind): a video can always be made without them.
        /// </summary>
        public static VideoSettings? Load(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            try
            {
                var path = GetPath(folder);
                if (!File.Exists(path))
                    return null;

                var settings = JsonSerializer.Deserialize<VideoSettings>(File.ReadAllText(path));
                if (settings is null)
                    return null;

                var baseFolder = Path.GetFullPath(folder);
                settings.OutputFolder = Resolve(baseFolder, settings.OutputFolder);
                settings.AudioPath = Resolve(baseFolder, settings.AudioPath);
                settings.CoverPath = Resolve(baseFolder, settings.CoverPath);
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes the settings to the file of the folder (not making the folder: one that is gone is not brought back), a temporary file
        /// first and then moved over the old one, so a failure half way never leaves a broken file.
        /// </summary>
        /// <param name="error">Why it could not be written; null when it was.</param>
        public static bool TrySave(string folder, VideoSettings settings, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                error = $"The folder is not there: {folder}";
                return false;
            }

            var path = GetPath(folder);
            var temporary = path + ".tmp";
            try
            {
                var baseFolder = Path.GetFullPath(folder);
                var stored = (VideoSettings)Copy(settings);
                stored.Version = VideoSettings.CurrentVersion;
                stored.SavedUtc = DateTime.UtcNow;
                stored.OutputFolder = Store(baseFolder, stored.OutputFolder);
                stored.AudioPath = Store(baseFolder, stored.AudioPath);
                stored.CoverPath = Store(baseFolder, stored.CoverPath);

                File.WriteAllText(temporary, JsonSerializer.Serialize(stored, JsonOptions));
                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                error = ex.Message;
                try { File.Delete(temporary); } catch { /* best effort */ }
                return false;
            }
        }

        private static VideoSettings Copy(VideoSettings settings) =>
            JsonSerializer.Deserialize<VideoSettings>(JsonSerializer.Serialize(settings, JsonOptions))!;

        // A blank path is written as "" (none: the audio or the cover was cleared), a path the file does not have is null (leave it alone)
        private static string? Store(string baseFolder, string? path) =>
            path is null ? null : string.IsNullOrWhiteSpace(path) ? string.Empty : StoredPaths.ToStored(baseFolder, path.Trim());

        private static string? Resolve(string baseFolder, string? stored) =>
            stored is null ? null : string.IsNullOrWhiteSpace(stored) ? string.Empty : StoredPaths.FromStored(baseFolder, stored.Trim());
    }
}
