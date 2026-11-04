namespace PhotoLiker.Search
{
    /// <summary>Writing a small file so that a failure half way never leaves a broken one.</summary>
    internal static class AtomicFile
    {
        /// <summary>
        /// Writes the text next to the file first and then moves it over the file. Windows sometimes refuses the move for a moment (a virus scanner or the
        /// indexer is looking at the new file), so it is tried again a few times before the failure is given up on and thrown (the half-written file is deleted).
        /// </summary>
        /// <param name="createFolder">Make the folder of the file if it is not there (not for a photo folder: a folder that is gone is not brought back).</param>
        public static void Write(string file, string text, bool createFolder = false)
        {
            var temporary = file + ".tmp";
            try
            {
                if (createFolder)
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);

                File.WriteAllText(temporary, text);
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        File.Move(temporary, file, overwrite: true);
                        return;
                    }
                    catch (Exception ex) when (attempt < 4 && (ex is IOException or UnauthorizedAccessException))
                    {
                        Thread.Sleep(40 * attempt);
                    }
                }
            }
            catch
            {
                try { File.Delete(temporary); } catch { /* best effort */ }
                throw;
            }
        }
    }
}
