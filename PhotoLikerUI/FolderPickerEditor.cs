using System.ComponentModel;

namespace PhotoLikerUI
{
    /// <summary>
    /// Gives a folder path in the PropertyGrid a "..." button that opens the folder browser, starting at the current
    /// value. Cancelling keeps the value as it was.
    /// </summary>
    internal sealed class FolderPickerEditor : System.Drawing.Design.UITypeEditor
    {
        public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
            => System.Drawing.Design.UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = DescriptionOf(context),
                UseDescriptionForTitle = true,
            };

            if (value is string current && Directory.Exists(current))
                dialog.SelectedPath = current;

            return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : value;
        }

        // What the folder is for; the name of the setting if it is not one of the known ones
        private static string DescriptionOf(ITypeDescriptorContext? context) =>
            context?.PropertyDescriptor?.Name switch
            {
                nameof(Settings.LikedFolder) => AppStrings.FolderBrowserSelectLiked,
                { } name => name,
                null => "Folder",
            };
    }
}
