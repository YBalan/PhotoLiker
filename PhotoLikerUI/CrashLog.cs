namespace PhotoLikerUI
{
    /// <summary>
    /// Appends unexpected exceptions to %LocalAppData%\PhotoLiker\crash.log so a crash leaves a stack trace behind.
    /// </summary>
    internal static class CrashLog
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoLiker",
            "crash.log");

        public static void Write(string source, Exception exception)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(
                    FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
