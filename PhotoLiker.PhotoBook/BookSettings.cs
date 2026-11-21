using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoLiker.PhotoBook
{
    /// <summary>A photo of the book in the file: where it is (in the file the path is relative to the folder of the file) and what was written for it.</summary>
    public sealed class StoredBookPhoto
    {
        public string? File { get; set; }

        /// <summary>The RTF of what is written above the photo.</summary>
        public string? Above { get; set; }

        /// <summary>The RTF of what is written below the photo.</summary>
        public string? Below { get; set; }

        public bool? LeftOut { get; set; }

        /// <summary>How the photo is turned in the book, clockwise, in degrees (90, 180 or 270); left out of the file for a photo that is not turned.</summary>
        public int? Rotation { get; set; }
    }

    /// <summary>
    /// The book of a folder as it was left: the settings of the dialog, the order of the photos and what is written above and below each. It is kept in the folder of the photos (see
    /// <see cref="BookSettingsFile"/>) so that the next time the folder is made into a book, the work done on it is still there. Every value is optional: one the file does not have (an older or a
    /// hand-made file) leaves the dialog as it is.
    /// </summary>
    public sealed class BookSettings
    {
        public const int CurrentVersion = 1;

        /// <summary>Which layout of this file it is; a file of a newer version is read for what this version knows of it.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>When it was written, for whoever looks at the file.</summary>
        public DateTime? SavedUtc { get; set; }

        /// <summary>PDF or EPUB, by the name of a <see cref="BookFormat"/>.</summary>
        public string? Format { get; set; }

        public string? Title { get; set; }

        public string? Author { get; set; }

        /// <summary>By the name of a <see cref="PageFormat"/>.</summary>
        public string? PageFormat { get; set; }

        /// <summary>By the name of a <see cref="PageOrientation"/>.</summary>
        public string? Orientation { get; set; }

        public double? MarginMillimetres { get; set; }

        public string? FontFamily { get; set; }

        public bool? PageNumbers { get; set; }

        /// <summary>By the name of a <see cref="PictureQuality"/>.</summary>
        public string? Quality { get; set; }

        public string? OutputFolder { get; set; }

        public string? OutputFileName { get; set; }

        /// <summary>The size the thumbnails are shown at, in pixels.</summary>
        public int? ThumbnailSize { get; set; }

        /// <summary>The photos in the order of the book.</summary>
        public List<StoredBookPhoto>? Photos { get; set; }

        public BookFormat? GetFormat() => Read<BookFormat>(Format);

        public PageFormat? GetPageFormat() => Read<PageFormat>(PageFormat);

        public PageOrientation? GetOrientation() => Read<PageOrientation>(Orientation);

        public PictureQuality? GetQuality() => Read<PictureQuality>(Quality);

        private static T? Read<T>(string? name) where T : struct, Enum =>
            Enum.TryParse<T>(name, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;

        /// <summary>The photos of the file as entries; the paths are what the file has (made full by <see cref="BookSettingsFile.Load"/>).</summary>
        public List<BookEntry> ToEntries() =>
        [
            .. (Photos ?? []).Where(photo => !string.IsNullOrWhiteSpace(photo.File)).Select(photo => new BookEntry(photo.File!)
            {
                AboveRtf = string.IsNullOrWhiteSpace(photo.Above) ? null : photo.Above,
                BelowRtf = string.IsNullOrWhiteSpace(photo.Below) ? null : photo.Below,
                LeftOut = photo.LeftOut == true,
                Rotation = photo.Rotation ?? 0,
            })
        ];
    }

    /// <summary>
    /// The <see cref="BookSettings"/> file of a folder: <c>photo-liker-book.json</c>, next to the photos the book is made of. The paths in it (the photos, the output folder) are stored relative to
    /// that folder when they are below it or beside it, so a folder that is moved, or opened from another machine or drive letter, keeps working (see <see cref="StoredPaths"/>).
    /// </summary>
    public static class BookSettingsFile
    {
        public const string FileName = "photo-liker-book.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string GetPath(string folder) => Path.Combine(folder, FileName);

        /// <summary>
        /// The book of the folder, with the paths made full, or null when it has none or its file cannot be read (no file, no access, not JSON, a value of the wrong kind): a book can always be
        /// made without it.
        /// </summary>
        public static BookSettings? Load(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            try
            {
                var path = GetPath(folder);
                if (!File.Exists(path))
                    return null;

                var settings = JsonSerializer.Deserialize<BookSettings>(File.ReadAllText(path));
                if (settings is null)
                    return null;

                var baseFolder = Path.GetFullPath(folder);
                settings.OutputFolder = Resolve(baseFolder, settings.OutputFolder);
                foreach (var photo in settings.Photos ?? [])
                    photo.File = Resolve(baseFolder, photo.File);
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes the settings and the photos to the file of the folder (not making the folder: one that is gone is not brought back), a temporary file first and then moved over the old one, so a
        /// failure half way never leaves a broken file.
        /// </summary>
        /// <param name="error">Why it could not be written; null when it was.</param>
        public static bool TrySave(string folder, BookSettings settings, IEnumerable<BookEntry> photos, out string? error)
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
                var stored = JsonSerializer.Deserialize<BookSettings>(JsonSerializer.Serialize(settings, JsonOptions))!;
                stored.Version = BookSettings.CurrentVersion;
                stored.SavedUtc = DateTime.UtcNow;
                stored.OutputFolder = Store(baseFolder, stored.OutputFolder);
                stored.Photos =
                [
                    .. photos.Select(photo => new StoredBookPhoto
                    {
                        File = StoredPaths.ToStored(baseFolder, photo.Path),
                        Above = string.IsNullOrWhiteSpace(photo.AboveRtf) ? null : photo.AboveRtf,
                        Below = string.IsNullOrWhiteSpace(photo.BelowRtf) ? null : photo.BelowRtf,
                        LeftOut = photo.LeftOut ? true : null,
                        Rotation = photo.Rotation == 0 ? null : photo.Rotation,
                    })
                ];

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

        private static string? Store(string baseFolder, string? path) =>
            path is null ? null : string.IsNullOrWhiteSpace(path) ? string.Empty : StoredPaths.ToStored(baseFolder, path.Trim());

        private static string? Resolve(string baseFolder, string? stored) =>
            stored is null ? null : string.IsNullOrWhiteSpace(stored) ? string.Empty : StoredPaths.FromStored(baseFolder, stored.Trim());
    }
}
