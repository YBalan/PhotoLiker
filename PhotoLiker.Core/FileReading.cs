namespace PhotoLiker.Core
{
    public static class FileReading
    {
        private const int MaxReadAttempts = 3;

        /// <summary>Reads a whole file, retrying a couple of times on transient I/O errors (network shares).</summary>
        public static byte[] ReadAllBytesShared(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var file = new FileStream(
                        path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, bufferSize: 1 << 16, FileOptions.SequentialScan);
                    var buffer = new byte[file.Length];
                    file.ReadExactly(buffer);
                    return buffer;
                }
                catch (IOException ex) when (attempt < MaxReadAttempts && ex is not (FileNotFoundException or DirectoryNotFoundException))
                {
                    Thread.Sleep(300 * attempt);
                }
            }
        }
    }
}
