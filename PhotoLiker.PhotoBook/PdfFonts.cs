using Microsoft.Win32;
using PdfSharp.Fonts;

namespace PhotoLiker.PhotoBook
{
    /// <summary>
    /// The fonts a PDF can be set in: the TrueType fonts that are installed on this computer, found by the name of the family. (PDFsharp finds only a handful of the fonts of Windows by itself:
    /// Arial, Times New Roman, Verdana and Courier New.) The file carries the letters it uses, so a reader of the PDF needs none of them.
    /// </summary>
    public static class PdfFonts
    {
        /// <summary>
        /// The families the dialog offers, in this order, when they are installed. A font is on the list only if it has Cyrillic (and Latin) letters, so that a book in Ukrainian does not come out with
        /// empty boxes: the fonts of Windows that are made for text, not the ones of symbols or of a single script.
        /// </summary>
        private static readonly string[] Recommended =
        [
            "Segoe UI", "Arial", "Calibri", "Cambria", "Candara", "Constantia", "Corbel", "Georgia", "Palatino Linotype", "Book Antiqua",
            "Times New Roman", "Tahoma", "Trebuchet MS", "Verdana", "Courier New", "Consolas", "Comic Sans MS",
        ];

        private static readonly Lazy<Dictionary<string, FamilyFiles>> Installed = new(Scan);

        /// <summary>The families of <see cref="Recommended"/> that this computer has, in the order of the list.</summary>
        public static IReadOnlyList<string> Available() =>
            OperatingSystem.IsWindows() ? [.. Recommended.Where(name => Installed.Value.TryGetValue(name, out var files) && files.HasAny)] : [];

        /// <summary>True when a family can be used for a PDF (it is installed as a TrueType font).</summary>
        public static bool IsInstalled(string family) =>
            OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(family) && Installed.Value.TryGetValue(family.Trim(), out var files) && files.HasAny;

        /// <summary>Makes the program's resolver the one PDFsharp asks; done once, before the first font is made.</summary>
        internal static void Register()
        {
            if (OperatingSystem.IsWindows() && !_registered)
            {
                lock (Installed)
                {
                    if (!_registered)
                    {
                        GlobalFontSettings.FontResolver = new Resolver();
                        _registered = true;
                    }
                }
            }
        }

        private static volatile bool _registered;

        /// <summary>The files of the faces of one family.</summary>
        private sealed class FamilyFiles
        {
            public string? Regular, Bold, Italic, BoldItalic;

            public bool HasAny => Regular is not null || Bold is not null || Italic is not null || BoldItalic is not null;

            /// <summary>Notes the file of a face; the first one found for a face is kept (the list of the system comes before the fonts of the user).</summary>
            public void Set(bool bold, bool italic, string path)
            {
                if (bold && italic)
                    BoldItalic ??= path;
                else if (bold)
                    Bold ??= path;
                else if (italic)
                    Italic ??= path;
                else
                    Regular ??= path;
            }
        }

        /// <summary>
        /// A name of the list of the fonts of Windows ("Segoe UI Bold Italic (TrueType)") as the family and the face: ("Segoe UI", bold, italic). Null for a name that is no single TrueType face (a
        /// collection of fonts in one file, "Cambria &amp; Cambria Math (TrueType)", is not read by PDFsharp, and a font that is not TrueType-based cannot be embedded).
        /// </summary>
        internal static (string Family, bool Bold, bool Italic)? ParseName(string registryName)
        {
            var name = registryName.Trim();
            var kind = new[] { " (TrueType)", " (OpenType)" }.FirstOrDefault(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (kind is null)
                return null;

            name = name[..^kind.Length].Trim();
            if (name.Length == 0 || name.Contains(" & ", StringComparison.Ordinal))
                return null;

            var bold = false;
            var italic = false;
            while (true)
            {
                if (name.EndsWith(" Bold", StringComparison.OrdinalIgnoreCase)) { bold = true; name = name[..^5].TrimEnd(); }
                else if (name.EndsWith(" Italic", StringComparison.OrdinalIgnoreCase)) { italic = true; name = name[..^7].TrimEnd(); }
                else if (name.EndsWith(" Oblique", StringComparison.OrdinalIgnoreCase)) { italic = true; name = name[..^8].TrimEnd(); }
                else if (name.EndsWith(" Regular", StringComparison.OrdinalIgnoreCase)) { name = name[..^8].TrimEnd(); }
                else break;
            }

            return name.Length == 0 ? null : (name, bold, italic);
        }

        private static Dictionary<string, FamilyFiles> Scan()
        {
            var families = new Dictionary<string, FamilyFiles>(StringComparer.OrdinalIgnoreCase);
            if (!OperatingSystem.IsWindows())
                return families;

            var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            foreach (var (hive, folder) in new[] { (Registry.LocalMachine, system), (Registry.CurrentUser, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts")) })
            {
                try
                {
                    using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                    if (key is null)
                        continue;

                    foreach (var valueName in key.GetValueNames())
                    {
                        if (key.GetValue(valueName) is not string file || ParseName(valueName) is not var (family, bold, italic))
                            continue;

                        if (!file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                            continue; // a collection (.ttc) holds several faces; PDFsharp reads a file of one

                        var path = Path.IsPathRooted(file) ? file : Path.Combine(folder, file);
                        if (!File.Exists(path))
                            continue;

                        if (!families.TryGetValue(family, out var files))
                            families[family] = files = new FamilyFiles();

                        files.Set(bold, italic, path);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // The list of fonts of this part of the system cannot be read: the fonts of the rest are all there is
                }
            }

            return families;
        }

        /// <summary>Tells PDFsharp which file holds a face of a family, and gives it the bytes of the file.</summary>
        private sealed class Resolver : IFontResolver
        {
            public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
            {
                if (!Installed.Value.TryGetValue(familyName.Trim(), out var files) || !files.HasAny)
                    return null;

                // The face that was asked for; when the family has not got it, the nearest one with the rest made up (a slanted or smeared letter, which is what a program does that has no real face)
                if (isBold && isItalic)
                    return Pick(files.BoldItalic, simulateBold: false, simulateItalic: false) ??
                           Pick(files.Bold, false, true) ?? Pick(files.Italic, true, false) ?? Pick(files.Regular, true, true) ?? Any(files);
                if (isBold)
                    return Pick(files.Bold, false, false) ?? Pick(files.Regular, true, false) ?? Any(files);
                if (isItalic)
                    return Pick(files.Italic, false, false) ?? Pick(files.Regular, false, true) ?? Any(files);

                return Pick(files.Regular, false, false) ?? Any(files);
            }

            private static FontResolverInfo? Pick(string? path, bool simulateBold, bool simulateItalic) =>
                path is null ? null : new FontResolverInfo(path, simulateBold, simulateItalic);

            private static FontResolverInfo? Any(FamilyFiles files) =>
                Pick(files.Regular ?? files.Bold ?? files.Italic ?? files.BoldItalic, false, false);

            public byte[]? GetFont(string faceName) => File.Exists(faceName) ? File.ReadAllBytes(faceName) : null;
        }
    }
}
