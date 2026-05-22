namespace PhotoLiker.Core
{
    using System.Text.Json;

    /// <summary>Lightweight user-level config stored in %LocalAppData%\PhotoLiker\.</summary>
    public class GlobalConfig
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoLiker",
            "global-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public string LastFolder { get; set; } = string.Empty;

        public static GlobalConfig Load()
        {
            if (!File.Exists(FilePath)) return new GlobalConfig();
            try
            {
                return JsonSerializer.Deserialize<GlobalConfig>(File.ReadAllText(FilePath)) ?? new GlobalConfig();
            }
            catch { return new GlobalConfig(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch { /* best-effort */ }
        }
    }
}
