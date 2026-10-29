using System.Text;

namespace PhotoLiker.Core
{
    /// <summary>
    /// The file a <see cref="PhotoListing"/> is kept in. Binary and flat, about 40 bytes a photo, so even 40,000 photos are
    /// read in a few milliseconds. Anything that is not exactly what was written (another version, a cut-off or damaged
    /// file, a listing made for other settings) reads as "no saved listing", and the folder is listed again.
    /// </summary>
    internal static class ListingFile
    {
        private const string Magic = "PLLS";
        private const string End = "END!";
        private const int Version = 1;
        private const int MostEntries = 50_000_000; // a damaged count must not make a huge array

        public static void Write(string path, string keyText, Dictionary<string, ListedFolder> folders)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // A crash while writing leaves the old file; two writers at once (even in one app) still leave a whole file
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(keyText);
                    writer.Write(folders.Count);
                    foreach (var folder in folders.Values)
                    {
                        writer.Write(folder.RelativePath.Replace(Path.DirectorySeparatorChar, '/'));
                        writer.Write(folder.WrittenTicks);
                        writer.Write(folder.Subfolders.Length);
                        foreach (var name in folder.Subfolders)
                            writer.Write(name);
                        writer.Write(folder.Photos.Length);
                        foreach (var photo in folder.Photos)
                        {
                            writer.Write(photo.Name);
                            writer.Write(photo.Length);
                            writer.Write(photo.WrittenTicks);
                            writer.Write(photo.CreatedTicks);
                        }
                    }
                    writer.Write(End);
                }

                File.Move(temporary, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(temporary); } catch { /* best effort */ }
                throw;
            }
        }

        /// <summary>The folders, or null if there is no file or it is not a listing for this key.</summary>
        public static Dictionary<string, ListedFolder>? Read(string path, string keyText)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                using var reader = new BinaryReader(stream, Encoding.UTF8);
                if (reader.ReadString() != Magic || reader.ReadInt32() != Version || reader.ReadString() != keyText)
                    return null;

                var count = Count(reader);
                var folders = new Dictionary<string, ListedFolder>(count, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < count; i++)
                {
                    var relative = reader.ReadString().Replace('/', Path.DirectorySeparatorChar);
                    var written = reader.ReadInt64();

                    var subfolders = new string[Count(reader)];
                    for (var s = 0; s < subfolders.Length; s++)
                        subfolders[s] = reader.ReadString();

                    var photos = new ListedPhoto[Count(reader)];
                    for (var p = 0; p < photos.Length; p++)
                        photos[p] = new ListedPhoto(reader.ReadString(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());

                    folders[relative] = new ListedFolder(relative, written, subfolders, photos);
                }

                // The end marker is how a file cut short is told from a whole one, and nothing may follow it
                if (reader.ReadString() != End || stream.Position != stream.Length)
                    return null;

                return folders.ContainsKey(string.Empty) ? folders : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OutOfMemoryException or FormatException)
            {
                return null; // EndOfStreamException is an IOException
            }
        }

        private static int Count(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            return count is < 0 or > MostEntries ? throw new InvalidDataException("Not a listing file.") : count;
        }
    }
}
