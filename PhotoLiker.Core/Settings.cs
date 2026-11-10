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
        /// <summary>
        /// Whether the videos of the folder are listed with its photos (see <see cref="VideoExtensions"/>): they are shown, played, liked, sorted into groups and
        /// deleted like photos. Off by default, so a folder that holds videos looks as it always did until this is turned on.
        /// </summary>
        public bool   SupportVideo   { get; set; } = false;

        /// <summary>The file extensions that count as videos when <see cref="SupportVideo"/> is on, see <see cref="MediaKinds.DefaultVideoExtensions"/>.</summary>
        public string[] VideoExtensions { get; set; } = [.. MediaKinds.DefaultVideoExtensions];

        /// <summary>
        /// Whether "Update index" also turns what is said in the videos into text (speech recognition on this computer), so that a word that is said finds the video: slow (about a third of
        /// the length of the speech with the model that works) and a model of hundreds of megabytes to download, which is why it is asked for. Needs <see cref="SupportVideo"/>.
        /// </summary>
        public bool VideoSpeech { get; set; } = false;

        /// <summary>The speech model, by the id the video library gives it ("tiny", "base", "small"); an id that is not known counts as the default, "small".</summary>
        public string SpeechModel { get; set; } = DefaultSpeechModel;
        public const string DefaultSpeechModel = "small";

        /// <summary>The language that is spoken in the videos ("ru", "uk", "en" ...), or "auto" to let the model hear it for each video.</summary>
        public string SpeechLanguage { get; set; } = "auto";

        /// <summary>The number of cores this computer has (logical processors, as Task Manager counts them): the most <see cref="CpuCores"/> can be.</summary>
        public static readonly int MaxCpuCores = Math.Max(1, Environment.ProcessorCount);

        private int _cpuCores = MaxCpuCores;

        /// <summary>
        /// How many cores of this computer the speech recognition of "Update index" may use, 1 to <see cref="MaxCpuCores"/> (all of them by default). It is read again
        /// whenever a video is started, so changing it while the index is being updated takes effect from the next video on. A value outside the range, or a file written on a
        /// computer with more cores, counts as the nearest value this computer can have.
        /// </summary>
        public int CpuCores
        {
            get => _cpuCores;
            set => _cpuCores = Math.Clamp(value, 1, MaxCpuCores);
        }

        /// <summary>What the folder listing takes for files of the folder: the photos, and the videos too if <see cref="SupportVideo"/> is on. Indexing and the tools work on <see cref="Extensions"/>, photos only.</summary>
        [JsonIgnore]
        public string[] ListedExtensions => SupportVideo ? [.. Extensions, .. VideoExtensions] : Extensions;

        [JsonConverter(typeof(PhotoSortOrderConverter))]
        public PhotoSortOrder SortOrder { get; set; } = PhotoSortOrder.OldestFirst;
        public int    CacheSize      { get; set; } = 20;
        public int    ThumbnailSize  { get; set; } = 110;
        public bool   IsDarkTheme    { get; set; } = false;

        /// <summary>
        /// The language of the program ("en", "ua"), for the settings grid. It is the language that is on (<see cref="Loc.Language"/>) and is
        /// kept in the global config, not in the settings file of the folder: the same for every folder.
        /// </summary>
        [JsonIgnore]
        public string Language
        {
            get => Loc.Language.Code;
            set => Loc.Language = AppLanguage.Find(value);
        }

        /// <summary>How strict the photo search is, 0 (wide: more photos, looser matches) to <see cref="MaxSearchPrecision"/> (precise: fewer, closer matches).</summary>
        public int    SearchPrecision { get; set; } = DefaultSearchPrecision;
        public const int MaxSearchPrecision = 100;
        public const int DefaultSearchPrecision = 50;

        /// <summary>
        /// The model that matches words with photos, by the id the search library gives it ("clip-vit-b32", "siglip2-b16", ...). An id
        /// that is not known (a file from a newer version, a typo) counts as <see cref="DefaultSearchModel"/>. The index of the folder holds
        /// the photos' vectors of one model at a time, so another model needs the photos to be analyzed again.
        /// </summary>
        public string SearchModel { get; set; } = DefaultSearchModel;
        public const string DefaultSearchModel = "clip-vit-b32";

        /// <summary>With videos listed (<see cref="SupportVideo"/>): whether a search looks through the photos. The "Photos" toggle of the search strip; both off counts as both on.</summary>
        public bool SearchPhotos { get; set; } = true;

        /// <summary>With videos listed: whether a search looks through the videos. The "Videos" toggle of the search strip; both off counts as both on.</summary>
        public bool SearchVideos { get; set; } = true;

        /// <summary>
        /// What a search looks through right now: the photos, the videos. The videos only when they are listed; the photos and the videos together when both
        /// toggles are on or both are off (something has to be searched).
        /// </summary>
        public (bool Photos, bool Videos) SearchKinds() =>
            !SupportVideo ? (true, false) : SearchPhotos == SearchVideos ? (true, true) : (SearchPhotos, SearchVideos);

        /// <summary>Which photo the slideshow (Alt+Enter) shows next: a random one, or the next of the list.</summary>
        [JsonConverter(typeof(SlideshowModeConverter))]
        public SlideshowMode SlideshowMode { get; set; } = SlideshowMode.Random;

        /// <summary>Which screens the slideshow covers: the one PhotoLiker is on, or all of them, each with a photo of its own.</summary>
        [JsonConverter(typeof(SlideshowMonitorsConverter))]
        public SlideshowMonitors SlideshowMonitors { get; set; } = SlideshowMonitors.Current;

        /// <summary>How many seconds the slideshow keeps a photo on the screen, see <see cref="Slideshow.TimeoutOf"/>.</summary>
        public double SlideshowTimeout { get; set; } = Slideshow.DefaultTimeoutSeconds;

        /// <summary>What the slideshow writes in the bottom right corner of the screen: the year or the date and time the photo was taken, or nothing.</summary>
        [JsonConverter(typeof(SlideshowWatermarkConverter))]
        public SlideshowWatermark SlideshowWatermark { get; set; } = SlideshowWatermark.DateTime;

        /// <summary>
        /// Whether the slideshow puts a box with the name on every face of a known person in the photo (the faces the search found, and the people they belong to). Off by default;
        /// the F key turns it on and off while the show runs, and what the show ended with becomes the setting.
        /// </summary>
        public bool SlideshowPeopleFaces { get; set; } = false;

        /// <summary>
        /// Whether the slideshow writes the names of the people in the photo in one line in the bottom left corner. Off by default; the N key turns it on and off while the show
        /// runs, and what the show ended with becomes the setting. It is independent of <see cref="SlideshowPeopleFaces"/> (both on write the names twice).
        /// </summary>
        public bool SlideshowPeopleNames { get; set; } = false;

        /// <summary>How the slideshow changes from one photo to the next, and whether a photo moves while it is shown, see <see cref="SlideshowEffects"/>.</summary>
        [JsonConverter(typeof(SlideshowEffectConverter))]
        public SlideshowEffect SlideshowEffect { get; set; } = SlideshowEffect.Fade;

        /// <summary>Whether the slideshow plays the videos of the list as well (there are some only while <see cref="SupportVideo"/> is on). Off by default: a slideshow shows photos.</summary>
        public bool SlideshowUseVideo { get; set; } = false;

        /// <summary>Whether the videos of the slideshow play without their sound.</summary>
        public bool SlideshowMuteVideo { get; set; } = false;

        /// <summary>How much of a video the slideshow plays: the places a search found in it (the default), or all of it.</summary>
        [JsonConverter(typeof(SlideshowVideoPartConverter))]
        public SlideshowVideoPart SlideshowVideoPart { get; set; } = SlideshowVideoPart.OnlyFoundTimeFrames;

        /// <summary>By how many percent the sound of the other programs is turned down while a video plays with its sound (50: to half of what it was; 0: left alone), see <see cref="SlideshowVideo.LowerOtherAudioOf"/>.</summary>
        public int SlideshowLowerOtherAudio { get; set; } = SlideshowVideo.DefaultLowerOtherAudioPercent;

        /// <summary>Whether the slideshow writes what is said in a video (the speech the index read, see <see cref="VideoSpeech"/>) over the picture as captions while the video plays. Off by default.</summary>
        public bool SlideshowShowCaptions { get; set; } = false;

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
        public List<PhotoFile> Files
        {
            get => _files;
            set { _files = value; _countedVideos = -1; }
        }
        private List<PhotoFile> _files = [];

        [JsonIgnore]
        public int TotalFiles => Files.Count;

        /// <summary>How many of the <see cref="TotalFiles"/> are photos; for the progress rows of the settings grid.</summary>
        [JsonIgnore]
        public int PhotoCount => Files.Count - VideoCount;

        /// <summary>
        /// How many of the <see cref="TotalFiles"/> are videos (there are some only while <see cref="SupportVideo"/> is on); for the progress rows of the settings grid.
        /// The count is remembered (the grid reads it again at every navigation) and made anew when the list or what counts as a video changes.
        /// </summary>
        [JsonIgnore]
        public int VideoCount
        {
            get
            {
                if (_countedVideos < 0 || _countedOf != _files.Count || _countedKinds != MediaKinds.Classification)
                {
                    _countedVideos = _files.Count(file => file.IsVideo);
                    _countedOf = _files.Count;
                    _countedKinds = MediaKinds.Classification;
                }
                return _countedVideos;
            }
        }
        private int _countedVideos = -1;
        private int _countedOf;
        private int _countedKinds;

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
