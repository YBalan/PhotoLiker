using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PhotoLikerUI
{
    internal class Settings
    {
        [Category("1. Settings")]
        [Display(Order = 1)]
        public string CurrentFolder { get; set; } = string.Empty;
        [Category("1. Settings")]
        [Display(Order = 2)]
        public string LikedFolder { get; set; } = string.Empty;
        [Category("1. Settings")]
        [Display(Order = 3)]
        public bool GoThroughtSubFolders { get; set; } = false;
        [Category("1. Settings")]
        [Display(Order = 4)]
        public string[] PhotoExts { get; set; } =
        [
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
        ];
        [Category("1. Settings")]
        [Display(Order = 5)]
        public int CacheSize { get; set; } = 20;

        [Category("Info")]
        [JsonIgnore]
        public List<PhotoFile> Files { get; set; } = [];
        [Category("Info")]
        [JsonIgnore]
        public int TotalFiles => Files?.Count ?? 0;

        [Category("2. Current File")]
        [Display(Order = 1)]
        public string CurrentFileName { get; set; } = string.Empty;
        [Category("2. Current File")]
        [Display(Order = 2)]
        public string CurrentFilePath { get; set; } = string.Empty;
        [Category("2. Current File")]
        [Display(Order = 3)]
        public int CurrentIndex { get; set; } = 0;
        [Category("2. Current File")]
        [Display(Order = 4)]
        public string CopiedFile { get; set; } = string.Empty;
    }

    public class PhotoFile(string OriginalFilePath, string LikedFilePath)
    {
        public string OriginalFilePath { get; } = OriginalFilePath;
        public string LikedFilePath { get; set; } = LikedFilePath;
        public override string ToString()
        {
            return $"{OriginalFilePath} -> {LikedFilePath}";
        }
    }

    public static class ListExt
    {
        public static int IndexOf<T>(this List<T> list, string originalFilePath)
            where T : PhotoFile
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].OriginalFilePath, originalFilePath, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}


