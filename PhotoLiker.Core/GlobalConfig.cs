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

        /// <summary>Whether the bar for colorizing floats over a black and white photo that is shown. Closing the bar turns it off; the Colorize button of the toolbar turns it on again.</summary>
        public bool ColorizeBar { get; set; } = true;

        /// <summary>The strength of the colours on the bar, in percent (100 is as the colorizing model gives them): the same for the next photo.</summary>
        public int ColorizeStrength { get; set; } = 100;

        /// <summary>The warmth of the colours on the bar, from -100 (cooler) to 100 (warmer).</summary>
        public int ColorizeWarmth { get; set; }

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
