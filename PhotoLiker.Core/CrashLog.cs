namespace PhotoLiker.Core
{
    /// <summary>
    /// Appends unexpected exceptions to %LocalAppData%\PhotoLiker\crash.log so a crash leaves a stack trace behind.
    /// </summary>
    public static class CrashLog
    {
        public static readonly string FilePath = Path.Combine(AppPaths.DataFolder, "crash.log");

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
