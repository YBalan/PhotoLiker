using System.ComponentModel;

namespace PhotoLikerUI
{
    /// <summary>The colour lookup tables (<c>.cube</c>) that come with the program, in the folder <c>Luts</c> beside it (the installer packs them, see PhotoLikerUI.csproj).</summary>
    internal static class BundledLuts
    {
        public const string FolderName = "Luts";

        /// <summary>The folder of the tables that came with the program.</summary>
        public static string Folder => Path.Combine(AppContext.BaseDirectory, FolderName);

        /// <summary>
        /// Where the file dialog of the LookFile row starts: the folder of the file that is chosen already, if it is still there (a person who chose a table of their own looks for the next one beside it), else
        /// the folder of the tables that came with the program, else nowhere in particular (null: the dialog uses its own idea).
        /// </summary>
        public static string? StartFolder(string? current, string bundled)
        {
            if (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    var folder = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        return folder;
                }
                catch (ArgumentException)
                {
                    // a path that is none: the same as no file
                }
            }

            return Directory.Exists(bundled) ? bundled : null;
        }
    }

    /// <summary>
    /// How the path of the colour lookup table (<see cref="Settings.SlideshowLookFile"/>) is shown in the PropertyGrid: only the name of the file, without its folder and its extension (<c>Teal and Orange</c>),
    /// which is what a person looks for; the setting itself keeps the whole path (the tip of the row says it, see <see cref="GridValueTips"/>). A name typed in the cell is looked for beside the file that is chosen
    /// and in the folder of the tables that came with the program, so <c>Teal and Orange</c> gives that table, and a whole path is taken as it is.
    /// </summary>
    internal sealed class LutFileConverter : TypeConverter
    {
        /// <summary>The text of a table in the grid: the name of its file without the extension; nothing for none.</summary>
        public static string Display(string? path) => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileNameWithoutExtension(path.Trim());

        /// <summary>
        /// The path that a text typed in the cell stands for: empty for nothing; a whole path as it is (without the quotes Explorer puts round one); else a name, which is looked for (with and without
        /// <c>.cube</c>) in the folder of <paramref name="current"/>, then in <paramref name="bundledFolder"/>; a name that is found nowhere is kept as typed, and the slideshow says it can not use it.
        /// </summary>
        public static string Resolve(string? text, string? current, string bundledFolder)
        {
            var typed = text?.Trim().Trim('"') ?? string.Empty;
            if (typed.Length == 0 || Path.IsPathRooted(typed))
                return typed;

            var folders = new List<string>();
            try
            {
                if (!string.IsNullOrWhiteSpace(current) && Path.GetDirectoryName(current) is { Length: > 0 } own)
                    folders.Add(own);
            }
            catch (ArgumentException)
            {
                // a path that is none: nowhere to look beside it
            }
            folders.Add(bundledFolder);

            foreach (var folder in folders)
            {
                foreach (var name in new[] { typed, typed + ".cube" })
                {
                    try
                    {
                        var candidate = Path.Combine(folder, name);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch (ArgumentException)
                    {
                        // a name with characters a file can not have
                    }
                }
            }
            return typed;
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value) =>
            value is string text
                ? Resolve(text, context?.PropertyDescriptor?.GetValue(context.Instance) as string, BundledLuts.Folder)
                : base.ConvertFrom(context, culture, value);

        public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) ? Display(value as string) : base.ConvertTo(context, culture, value, destinationType);
    }

    /// <summary>
    /// Gives the path of a colour lookup table (<c>.cube</c>) in the PropertyGrid a "..." button that opens the file dialog, in the folder of the tables that came with the program (or in the folder of the file
    /// that is chosen already), with that folder among the places on the left of the dialog so that it is a click away from anywhere. Cancelling keeps the value as it was. The file is not read here: the
    /// slideshow reads it when it starts, and tells what is wrong with it if it can not use it.
    /// </summary>
    internal sealed class LutFileEditor : System.Drawing.Design.UITypeEditor
    {
        public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
            => System.Drawing.Design.UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            using var dialog = new OpenFileDialog
            {
                Title = SlideshowStrings.LookFileDialogTitle,
                Filter = SlideshowStrings.LookFileFilter,
                CheckFileExists = true,
            };

            var current = value as string;
            if (BundledLuts.StartFolder(current, BundledLuts.Folder) is { } start)
                dialog.InitialDirectory = start;
            if (!string.IsNullOrEmpty(current))
                dialog.FileName = Path.GetFileName(current);

            if (Directory.Exists(BundledLuts.Folder))
            {
                try
                {
                    dialog.CustomPlaces.Add(BundledLuts.Folder);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PlatformNotSupportedException)
                {
                    // the dialog has no such list on this system: it still starts in the folder
                }
            }

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : value;
        }
    }
}
