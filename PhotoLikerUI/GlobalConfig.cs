namespace PhotoLikerUI
{
    using System.Text.Json;

    /// <summary>
    /// Lightweight user-level config stored in %LocalAppData%\PhotoLiker\.
    /// Persists data that spans folder sessions (e.g. last opened folder).
    /// </summary>
    internal class GlobalConfig
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoLiker",
            "global-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public string LastFolder { get; set; } = string.Empty;

        // ── persistence ─────────────────────────────────────────────────────

        public static GlobalConfig Load()
        {
            if (!File.Exists(FilePath))
                return new GlobalConfig();

            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<GlobalConfig>(json) ?? new GlobalConfig();
            }
            catch
            {
                return new GlobalConfig();
            }
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
