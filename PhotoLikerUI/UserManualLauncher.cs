namespace PhotoLikerUI
{
    /// <summary>Opens a page of the user manual (see <see cref="UserManual"/>) in the browser; from the About box and from the keys F2 / F3.</summary>
    internal static class UserManualLauncher
    {
        /// <summary>What opens a file. Windows does it (the default browser); a test can put its own here instead of launching one.</summary>
        public static Func<string, string?> Opener { get; set; } = ShellOpen.Document;

        /// <summary>Opens the manual in that language; if it cannot, says why in a message box over <paramref name="owner"/>.</summary>
        public static void Open(IWin32Window owner, string language)
        {
            var page = UserManual.Find(AppContext.BaseDirectory, language);
            if (page is null)
            {
                var folder = Path.Combine(AppContext.BaseDirectory, UserManual.FolderName);
                MessageBox.Show(owner, string.Format(AboutStrings.ManualNotFoundFormat, folder), MainFormStrings.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var error = Opener(page);
            if (error is not null)
                MessageBox.Show(owner, ShellOpen.Report(page, error), MainFormStrings.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
