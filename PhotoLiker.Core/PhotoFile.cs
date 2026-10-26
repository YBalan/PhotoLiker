namespace PhotoLiker.Core
{
    public class PhotoFile(string OriginalFilePath, string LikedFilePath)
    {
        public string OriginalFilePath { get; } = OriginalFilePath;
        public string LikedFilePath { get; set; } = LikedFilePath;

        // Size and timestamp from the folder listing; they tell same-named photos in different folders apart.
        public long Length { get; init; }
        public DateTime LastWriteTimeUtc { get; init; }

        public override string ToString()
        {
            return $"{OriginalFilePath} -> {LikedFilePath}";
        }
    }

    public static class PhotoFileListExtensions
    {
        public static int IndexOf(this List<PhotoFile> list, string originalFilePath)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].OriginalFilePath, originalFilePath, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
