using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PhotoLiker.Core
{
    /// <summary>
    /// The file name of a copy in the liked folder or a group folder: the name of the photo with an id of the photo in
    /// brackets, "IMG_0001 [3f9a1c2e].jpg". Photos of different folders often share a name (every camera folder has its
    /// IMG_0001.jpg), and a copy must neither replace the copy of another photo nor be taken for it.
    ///
    /// The id is made from what the folder listing tells about the photo, its size and the time it was last written (to the
    /// second), and from nothing else: not its folder, its drive or the computer. The same photo gets the same name wherever
    /// it is found and whatever else is open, so copies made on different computers or from different folders can share a folder.
    /// </summary>
    public static partial class CopyName
    {
        public const int IdLength = 8;

        [GeneratedRegex(@"^(?<name>.+) \[(?<id>[0-9a-f]{8})\]$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex IdSuffix();

        // Before the ids, the second of two photos called a.jpg was saved as "a (2).jpg"
        [GeneratedRegex(@"^(?<name>.+) \((?<number>\d+)\)$", RegexOptions.CultureInvariant)]
        private static partial Regex NumberSuffix();

        /// <summary>The name for a new copy of the photo: "IMG_0001 [3f9a1c2e].jpg".</summary>
        public static string For(PhotoFile photo)
        {
            var fileName = Path.GetFileName(photo.OriginalFilePath);
            return $"{Path.GetFileNameWithoutExtension(fileName)} [{IdOf(photo)}]{Path.GetExtension(fileName)}";
        }

        /// <summary>
        /// Eight hex digits that follow from the size and the last-write time of the photo, to the second (file systems
        /// differ in how exactly they keep a time). SHA-256 and not <see cref="string.GetHashCode"/>, which differs on every run.
        /// </summary>
        public static string IdOf(PhotoFile photo)
        {
            Span<byte> input = stackalloc byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(input, photo.Length);
            BinaryPrimitives.WriteInt64LittleEndian(input[8..], photo.LastWriteTimeUtc.Ticks / TimeSpan.TicksPerSecond);

            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(input, hash);
            return Convert.ToHexString(hash[..(IdLength / 2)]).ToLowerInvariant();
        }

        /// <summary>Takes the id off a name made by <see cref="For"/>: "a [3f9a1c2e].jpg" is the copy of "a.jpg" with id 3f9a1c2e.</summary>
        public static bool TryGetId(string copyFileName, out string photoFileName, out string id)
        {
            var match = IdSuffix().Match(Path.GetFileNameWithoutExtension(copyFileName));
            if (!match.Success)
            {
                photoFileName = copyFileName;
                id = string.Empty;
                return false;
            }

            photoFileName = match.Groups["name"].Value + Path.GetExtension(copyFileName);
            id = match.Groups["id"].Value;
            return true;
        }

        /// <summary>
        /// The names of photos the file can be a copy of. A copy made before the ids is named like its photo ("a.jpg"), or
        /// "a (2).jpg" for the second photo of that name, which is why a photo can be called "a.jpg" and "a (2).jpg" too.
        /// </summary>
        public static IEnumerable<string> PhotoNamesOf(string copyFileName)
        {
            yield return copyFileName;

            if (TryGetId(copyFileName, out var photoFileName, out _))
            {
                yield return photoFileName;
                yield break;
            }

            var match = NumberSuffix().Match(Path.GetFileNameWithoutExtension(copyFileName));
            if (match.Success && int.TryParse(match.Groups["number"].Value, out var number) && number >= 2)
                yield return match.Groups["name"].Value + Path.GetExtension(copyFileName);
        }
    }
}
