using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.Json;

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
        [TypeConverter(typeof(PhotoExtsConverter))]
        public string[] Extensions { get; set; } =
        [
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
        ];
        [Category("1. Settings")]
        [Display(Order = 5)]
        public int CacheSize { get; set; } = 20;

        // Window state
        [Category("3. Window")]
        [Display(Order = 1)]
        [Browsable(false)]
        public int WindowLeft { get; set; } = 100;
        [Category("3. Window")]
        [Display(Order = 2)]
        [Browsable(false)]
        public int WindowTop { get; set; } = 100;
        [Category("3. Window")]
        [Display(Order = 3)]
        [Browsable(false)]
        public int WindowWidth { get; set; } = 1200;
        [Category("3. Window")]
        [Display(Order = 4)]
        [Browsable(false)]
        public int WindowHeight { get; set; } = 700;
        [Category("3. Window")]
        [Display(Order = 5)]
        [Browsable(false)]
        public int ScreenIndex { get; set; } = 0;
        [Category("3. Window")]
        [Display(Order = 6)]
        [Browsable(false)]
        public FormWindowState WindowState { get; set; } = FormWindowState.Normal;

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
        public string LikedFile { get; set; } = string.Empty;

        [Category("1. Settings")]
        [Display(Order = 6)]
        [Browsable(false)]
        public bool IsDarkTheme { get; set; } = false;
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

    /// <summary>
    /// Shows string[] as a semicolon-separated list in PropertyGrid and allows editing it the same way.
    /// </summary>
    internal class PhotoExtsConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
            => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is string[] arr)
                return string.Join("; ", arr);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
        {
            if (value is string s)
            {
                return s.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .ToArray();
            }
            return base.ConvertFrom(context, culture, value);
        }
    }
}


