namespace PhotoLiker.Core
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The settings of a photo folder, kept in <see cref="AppStrings.SettingsFileName"/> inside that folder and shared
    /// by all front ends. A front end keeps the settings it doesn't use (e.g. the window state) when it saves the file.
    /// The paths in the file are relative to the folder it is in, see <see cref="StoredPaths"/>.
    /// </summary>
    public class Settings
    {
        public string CurrentFolder  { get; set; } = string.Empty;
        public string LikedFolder    { get; set; } = string.Empty;

        // The WinForms app has always written this setting as "GoThroughtSubFolders", so that stays the name in the
        // file (apps of different versions can open the same folder). Files of the MAUI app have the other spelling.
        [JsonPropertyName("GoThroughtSubFolders")]
        public bool   GoThroughSubFolders { get; set; } = false;
        [JsonPropertyName("GoThroughSubFolders")]
        public bool   GoThroughSubFoldersAlternateSpelling { set => GoThroughSubFolders = value; }

        public string[] Extensions   { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"];
        [JsonConverter(typeof(PhotoSortOrderConverter))]
        public PhotoSortOrder SortOrder { get; set; } = PhotoSortOrder.OldestFirst;
        public int    CacheSize      { get; set; } = 20;
        public int    ThumbnailSize  { get; set; } = 110;
        public bool   IsDarkTheme    { get; set; } = false;

        // Main window of the WinForms app
        public int    WindowLeft     { get; set; } = 100;
        public int    WindowTop      { get; set; } = 100;
        public int    WindowWidth    { get; set; } = 1200;
        public int    WindowHeight   { get; set; } = 700;
        public int    ScreenIndex    { get; set; } = 0;
        /// <summary>The window state as the number of <c>System.Windows.Forms.FormWindowState</c>: 0 normal, 2 maximized.</summary>
        public int    WindowState    { get; set; } = 0;

        // Current file: the path is remembered to open the folder at the same photo, the rest follows from it
        public string CurrentFilePath { get; set; } = string.Empty;
        [JsonIgnore]
        public string CurrentFileName { get; set; } = string.Empty;
        [JsonIgnore]
        public int    CurrentIndex    { get; set; } = 0;
        [JsonIgnore]
        public string LikedFile       { get; set; } = string.Empty;
        // Every folder holding a copy of the current photo: the liked folder and its group folders
        [JsonIgnore]
        public string[] SavedInFolders { get; set; } = [];

        [JsonIgnore]
        public List<PhotoFile> Files  { get; set; } = [];
        [JsonIgnore]
        public int TotalFiles => Files.Count;

        // ── liked folder ───────────────────────────────────────────────────

        /// <summary>
        /// Unless the liked folder was set to some other folder (any name but "Liked"), it is the "Liked" folder
        /// inside the photo folder.
        /// </summary>
        public void ApplyDefaultLikedFolder()
        {
            var isDefault = string.IsNullOrWhiteSpace(LikedFolder) ||
                string.Equals(Path.GetFileName(LikedFolder), AppStrings.DefaultLikedFolderName, StringComparison.OrdinalIgnoreCase);

            if (isDefault)
                LikedFolder = Path.Combine(CurrentFolder, AppStrings.DefaultLikedFolderName);
        }

        // ── persistence ────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string GetFilePath(string folder) =>
            Path.Combine(string.IsNullOrWhiteSpace(folder) ? Environment.CurrentDirectory : folder,
                AppStrings.SettingsFileName);

        /// <summary>
        /// The settings of the folder, or the defaults for it if it has none or its file can't be read.
        /// The settings belong to the folder they were found in, see <see cref="ResolveStoredPaths"/>.
        /// </summary>
        public static Settings Load(string folder)
        {
            try
            {
                return Read(folder, pinCurrentFolder: true) ?? new Settings { CurrentFolder = folder };
            }
            catch
            {
                return new Settings { CurrentFolder = folder };
            }
        }

        /// <summary>The settings in the settings file of the folder; null if it has none. Throws if the file can't be read.</summary>
        public static Settings? Read(string folder, bool pinCurrentFolder)
        {
            var path = GetFilePath(folder);
            if (!File.Exists(path))
                return null;

            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
            settings?.ResolveStoredPaths(Path.GetDirectoryName(path)!, pinCurrentFolder);
            return settings;
        }

        /// <summary>Writes the settings file of the folder (the photo folder if none is given). Throws if that fails.</summary>
        public void Write(string? folder = null)
        {
            var path = GetFilePath(folder ?? CurrentFolder);
            var stored = ForStorage(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stored, JsonOptions));
        }

        /// <summary>Like <see cref="Write"/>, but creates the folder if needed and ignores failures.</summary>
        public void Save(string? folder = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GetFilePath(folder ?? CurrentFolder))!);
                Write(folder);
            }
            catch { /* best-effort */ }
        }

        /// <summary>A copy for the settings file, with the paths relative to the folder the file is in (see <see cref="StoredPaths"/>).</summary>
        public Settings ForStorage(string folder)
        {
            var copy = (Settings)MemberwiseClone();
            copy.CurrentFolder = StoredPaths.ToStored(folder, CurrentFolder);
            copy.LikedFolder = StoredPaths.ToStored(folder, LikedFolder);
            copy.CurrentFilePath = StoredPaths.ToStored(folder, CurrentFilePath);
            return copy;
        }

        /// <summary>
        /// Turns the paths read from the settings file in <paramref name="folder"/> into full paths on this machine.
        /// With <paramref name="pinCurrentFolder"/> the settings belong to the folder they were found in, wherever that
        /// folder is mounted today: a file written by an older version (full paths, e.g. of drive Y: on another machine)
        /// is moved over to it.
        /// </summary>
        public void ResolveStoredPaths(string folder, bool pinCurrentFolder)
        {
            var legacyRoot = pinCurrentFolder && Path.IsPathRooted(CurrentFolder) ? CurrentFolder : null;

            CurrentFolder = pinCurrentFolder ? folder : StoredPaths.FromStored(folder, CurrentFolder);
            LikedFolder = StoredPaths.FromStored(folder, LikedFolder, legacyRoot);
            CurrentFilePath = StoredPaths.FromStored(folder, CurrentFilePath, legacyRoot);
        }
    }
}
