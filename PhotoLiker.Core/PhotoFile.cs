namespace PhotoLiker.Core
{
    public class PhotoFile(string OriginalFilePath, string LikedFilePath)
    {
        public string OriginalFilePath { get; } = OriginalFilePath;
        public string LikedFilePath { get; set; } = LikedFilePath;

        // Size and timestamp from the folder listing; they tell same-named photos in different folders apart.
        // The size changes when the app adds the orientation to a photo it turns (see JpegOrientation); the time never does.
        public long Length { get; set; }
        public DateTime LastWriteTimeUtc { get; init; }

        // When the file was put on this disk (a copy or an import gets the date of the copy), also from the folder listing
        public DateTime CreationTimeUtc { get; init; }

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
