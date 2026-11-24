using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoLiker.Core;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Manuscripts.Pages;

namespace PhotoLiker.Manuscripts
{
    /// <summary>One line of a page as the scan found it: the four corners of its box in the pixels of the (upright) photo, what was read, and how sure the reader was.</summary>
    public sealed class StoredScanLine
    {
        /// <summary>X and Y of the top left, top right, bottom right and bottom left corner: eight numbers.</summary>
        public float[]? Box { get; set; }

        public string? Text { get; set; }

        public float Confidence { get; set; }

        public static StoredScanLine From(TextLine line) => new()
        {
            Box = [.. line.Box.Corners.SelectMany(corner => new[] { MathF.Round(corner.X, 1), MathF.Round(corner.Y, 1) })],
            Text = line.Text,
            Confidence = MathF.Round(line.Confidence, 3),
        };

        /// <summary>The line again, or null when its box is not four corners.</summary>
        public TextLine? ToLine()
        {
            if (Box is not { Length: 8 } b || Text is null)
                return null;

            return new TextLine(Text, Confidence, new TextRegion(new PointF(b[0], b[1]), new PointF(b[2], b[3]), new PointF(b[4], b[5]), new PointF(b[6], b[7])));
        }
    }

    /// <summary>A picture found on a page as it is kept: the corners of its outline in the pixels of the (upright) photo.</summary>
    public sealed class StoredPicture
    {
        /// <summary>X and Y of each corner of the outline, one after the other: at least six numbers.</summary>
        public float[]? Outline { get; set; }

        public static StoredPicture From(PagePicture picture) => new()
        {
            Outline = [.. picture.Outline.SelectMany(corner => new[] { MathF.Round(corner.X, 1), MathF.Round(corner.Y, 1) })],
        };

        /// <summary>The picture again, or null when the outline is not at least three corners.</summary>
        public PagePicture? ToPicture()
        {
            if (Outline is not { Length: >= 6 } o || o.Length % 2 != 0)
                return null;

            return new PagePicture([.. Enumerable.Range(0, o.Length / 2).Select(i => new PointF(o[2 * i], o[2 * i + 1]))]);
        }
    }

    /// <summary>A page of a manuscript as it is kept: the photo, the text as the person left it (RTF of the Rich Text Box), what the scan read, and where it read it.</summary>
    public sealed class ManuscriptPage
    {
        /// <summary>The photo of the page; in the file it is relative to the folder of the file when it is below it or beside it.</summary>
        public string? File { get; set; }

        /// <summary>The text of the page as RTF; null for a page that has no text yet.</summary>
        public string? Rtf { get; set; }

        /// <summary>What the scan read, as plain text (a line to a line): what the text is brought back to by "scan again", and how the program knows the person has edited it.</summary>
        public string? Scanned { get; set; }

        /// <summary>By the name of a <see cref="ScanKind"/>: what the page was scanned as; null for a page that was not.</summary>
        public string? Kind { get; set; }

        /// <summary>By the name of a <see cref="ScanLanguage"/>.</summary>
        public string? Language { get; set; }

        public DateTime? ScannedUtc { get; set; }

        /// <summary>The size of the (upright) photo the scan read, which the boxes of <see cref="Lines"/> are in the pixels of; a photo that has another shape now (it was turned or replaced) shows no boxes.</summary>
        public int? Width { get; set; }

        public int? Height { get; set; }

        /// <summary>The lines the scan found, to show where on the photo each is.</summary>
        public List<StoredScanLine>? Lines { get; set; }

        /// <summary>
        /// The pictures found on the page (in the pixels of the photo of <see cref="Width"/> by <see cref="Height"/>, like the boxes of <see cref="Lines"/>). Null: nobody looked for pictures on
        /// this page yet; an empty list: somebody did, and there are none.
        /// </summary>
        public List<StoredPicture>? Pictures { get; set; }

        /// <summary>The page is in the list but not in the book that is made.</summary>
        public bool? LeftOut { get; set; }

        /// <summary>True when the text was scanned and not changed since (what the person sees is what the scan read).</summary>
        [JsonIgnore]
        public bool IsScanned => !string.IsNullOrEmpty(Scanned);

        /// <summary>The kind of writing the page was scanned as; null for a page that was not scanned.</summary>
        public ScanKind? GetKind() => ManuscriptSettings.Read<ScanKind>(Kind);

        /// <summary>The language the page was scanned in; null for a page that was not scanned.</summary>
        public ScanLanguage? GetLanguage() => ManuscriptSettings.Read<ScanLanguage>(Language);
    }

    /// <summary>
    /// The manuscript of a folder as it was left: the pages with what was scanned and what was written, and the settings of the window, so that the work goes on the next day. It is kept in the
    /// folder of the photos as <c>photo-liker-manuscript.json</c> (see <see cref="ManuscriptFile"/>). Every value is optional: one the file does not have leaves the window as it is.
    /// </summary>
    public sealed class ManuscriptSettings
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        public DateTime? SavedUtc { get; set; }

        /// <summary>By the name of a <see cref="ScanKind"/>.</summary>
        public string? Kind { get; set; }

        /// <summary>By the name of a <see cref="ScanLanguage"/>.</summary>
        public string? Language { get; set; }

        public string? Title { get; set; }

        public string? Author { get; set; }

        /// <summary>Whether the photo of each page goes into the PDF or the EPUB, above its text.</summary>
        public bool? IncludePhotos { get; set; }

        /// <summary>Whether the sheet is cut out of the photo of each page, and straightened, before the photo goes into the book (see <c>PageCutter</c>).</summary>
        public bool? CutPages { get; set; }

        /// <summary>Whether a scan looks for the pictures on the page too, and puts them into its text (the Find pictures button does that whatever this says).</summary>
        public bool? FindPictures { get; set; }

        public string? OutputFolder { get; set; }

        public string? OutputFileName { get; set; }

        /// <summary>The page that was open.</summary>
        public int? CurrentPage { get; set; }

        public List<ManuscriptPage>? Pages { get; set; }

        /// <summary>
        /// The photos that were taken out of the manuscript on purpose. A window that starts with all the photos of the folder as its pages leaves these out, so that a photo that was taken out
        /// (a duplicate, a page of something else) does not come back every time the window is opened.
        /// </summary>
        public List<string>? RemovedPages { get; set; }

        public ScanKind? GetKind() => Read<ScanKind>(Kind);

        public ScanLanguage? GetLanguage() => Read<ScanLanguage>(Language);

        internal static T? Read<T>(string? name) where T : struct, Enum =>
            Enum.TryParse<T>(name, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;
    }

    /// <summary>
    /// The <see cref="ManuscriptSettings"/> file of a folder: <c>photo-liker-manuscript.json</c>, next to the photos. The paths in it are relative to that folder when they are below it or beside
    /// it, so a folder that is moved, or opened from another machine or drive letter, keeps working (see <see cref="StoredPaths"/>).
    /// </summary>
    public static class ManuscriptFile
    {
        public const string FileName = "photo-liker-manuscript.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string GetPath(string folder) => Path.Combine(folder, FileName);

        /// <summary>The manuscript of the folder, with the paths made full, or null when it has none or its file cannot be read (no file, no access, not JSON, a value of the wrong kind).</summary>
        public static ManuscriptSettings? Load(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            try
            {
                var path = GetPath(folder);
                if (!File.Exists(path))
                    return null;

                var settings = JsonSerializer.Deserialize<ManuscriptSettings>(File.ReadAllText(path));
                if (settings is null)
                    return null;

                var baseFolder = Path.GetFullPath(folder);
                settings.OutputFolder = Resolve(baseFolder, settings.OutputFolder);
                foreach (var page in settings.Pages ?? [])
                    page.File = Resolve(baseFolder, page.File);
                settings.Pages = [.. (settings.Pages ?? []).Where(page => !string.IsNullOrWhiteSpace(page.File))];
                if (settings.RemovedPages is not null)
                    settings.RemovedPages = [.. settings.RemovedPages.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Resolve(baseFolder, path)!)];
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes the manuscript to the file of the folder (not making the folder: one that is gone is not brought back), a temporary file first and then moved over the old one, so a failure half
        /// way never leaves a broken file.
        /// </summary>
        /// <param name="error">Why it could not be written; null when it was.</param>
        public static bool TrySave(string folder, ManuscriptSettings settings, out string? error)
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
                var stored = JsonSerializer.Deserialize<ManuscriptSettings>(JsonSerializer.Serialize(settings, JsonOptions))!;
                stored.Version = ManuscriptSettings.CurrentVersion;
                stored.SavedUtc = DateTime.UtcNow;
                stored.OutputFolder = stored.OutputFolder is null ? null : string.IsNullOrWhiteSpace(stored.OutputFolder) ? string.Empty : StoredPaths.ToStored(baseFolder, stored.OutputFolder.Trim());
                foreach (var page in stored.Pages ?? [])
                {
                    page.File = page.File is null ? null : StoredPaths.ToStored(baseFolder, page.File);
                    page.Rtf = string.IsNullOrWhiteSpace(page.Rtf) ? null : page.Rtf;
                }

                if (stored.RemovedPages is not null)
                    stored.RemovedPages = [.. stored.RemovedPages.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => StoredPaths.ToStored(baseFolder, path)).Distinct(StringComparer.OrdinalIgnoreCase)];

                System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(stored, JsonOptions));
                System.IO.File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                error = ex.Message;
                try { System.IO.File.Delete(temporary); } catch { /* best effort */ }
                return false;
            }
        }

        private static string? Resolve(string baseFolder, string? stored) =>
            stored is null ? null : string.IsNullOrWhiteSpace(stored) ? string.Empty : StoredPaths.FromStored(baseFolder, stored.Trim());
    }
}
