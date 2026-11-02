namespace PhotoLiker.Core
{
    using System.Text.Json;

    /// <summary>Lightweight user-level config stored in %LocalAppData%\PhotoLiker\.</summary>
    public class GlobalConfig
    {
        private static readonly string FilePath = Path.Combine(AppPaths.DataFolder, "global-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public string LastFolder { get; set; } = string.Empty;

        /// <summary>The language of the program, by <see cref="AppLanguage.Code"/> ("en", "ua"). One for the whole program, so it is kept here and not in the settings of a photo folder. A language whose file is gone is English.</summary>
        public string Language { get; set; } = AppLanguage.EnglishCode;

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
