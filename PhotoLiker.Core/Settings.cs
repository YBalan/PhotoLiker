namespace PhotoLiker.Core
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class Settings
    {
        public string CurrentFolder  { get; set; } = string.Empty;
        public string LikedFolder    { get; set; } = string.Empty;
        public bool   GoThroughSubFolders { get; set; } = false;
        public string[] Extensions   { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"];
        public int    CacheSize      { get; set; } = 20;
        public int    ThumbnailSize  { get; set; } = 110;
        public bool   IsDarkTheme    { get; set; } = false;

        // Current file state (not persisted in MAUI but kept for compatibility)
        [JsonIgnore]
        public string CurrentFilePath { get; set; } = string.Empty;
        [JsonIgnore]
        public string CurrentFileName { get; set; } = string.Empty;
        [JsonIgnore]
        public int    CurrentIndex    { get; set; } = 0;
        [JsonIgnore]
        public string LikedFile       { get; set; } = string.Empty;
        [JsonIgnore]
        public List<PhotoFile> Files  { get; set; } = [];

        // ── persistence ────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private const string SettingsFileName = "photo-liker-config.json";

        public static Settings Load(string folder)
        {
            var path = GetPath(folder);
            if (!File.Exists(path)) return new Settings { CurrentFolder = folder };
            try
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings { CurrentFolder = folder };
                settings.CurrentFolder = ResolveStoredPath(folder, settings.CurrentFolder);
                settings.LikedFolder = ResolveStoredPath(folder, settings.LikedFolder);
                return settings;
            }
            catch { return new Settings { CurrentFolder = folder }; }
        }

        // The WinForms app stores paths relative to the folder the settings file is in ("." and "Liked")
        private static string ResolveStoredPath(string folder, string stored) =>
            string.IsNullOrWhiteSpace(stored) || Path.IsPathRooted(stored)
                ? stored
                : Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(folder) ? Environment.CurrentDirectory : folder, stored));

        public void Save(string? folder = null)
        {
            var path = GetPath(folder ?? CurrentFolder);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch { /* best-effort */ }
        }

        private static string GetPath(string folder) =>
            Path.Combine(string.IsNullOrWhiteSpace(folder) ? Environment.CurrentDirectory : folder,
                SettingsFileName);
    }

    public class PhotoFile(string OriginalFilePath, string LikedFilePath)
    {
        public string OriginalFilePath { get; } = OriginalFilePath;
        public string LikedFilePath    { get; set; } = LikedFilePath;
        public override string ToString() => $"{OriginalFilePath} -> {LikedFilePath}";
    }

    public static class PhotoFileListExtensions
    {
        public static int IndexOf(this List<PhotoFile> list, string originalFilePath)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].OriginalFilePath, originalFilePath, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
