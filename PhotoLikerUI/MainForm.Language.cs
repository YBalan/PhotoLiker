using System.Globalization;

namespace PhotoLikerUI
{
    /// <summary>
    /// The language of the window: the texts of the controls are written again when the language is changed (the Language row of the
    /// settings grid), so nothing has to be restarted. The texts that come up later (the status line, the dialogs) are looked up when
    /// they are needed, in the language that is on then.
    /// </summary>
    public partial class MainForm
    {
        private void InitializeLanguage()
        {
            Loc.Changed += OnLanguageChanged;
            FormClosed += (_, _) => Loc.Changed -= OnLanguageChanged;
        }

        /// <summary>Puts a language on for the whole program: its texts, and the culture .NET itself speaks in (the messages of the framework, the grids).</summary>
        internal static void SetLanguage(AppLanguage language)
        {
            Loc.Language = language;
            CultureInfo.DefaultThreadCurrentUICulture = language.Culture;
            CultureInfo.CurrentUICulture = language.Culture;
        }

        /// <summary>
        /// The language was changed (in the settings grid): it is kept for the next start, and the texts of the window are written again once
        /// the grid is done with the change, which would be disturbed if its own rows were rebuilt while it commits one.
        /// </summary>
        private void OnLanguageChanged()
        {
            if (IsDisposed || Disposing)
                return;

            SetLanguage(Loc.Language);
            _globalConfig.Language = Loc.Language.Code;
            _globalConfig.Save();
            BeginInvoke(ApplyLanguage);
        }

        private void ApplyLanguage()
        {
            SuspendLayout();
            try
            {
                ApplyControlTexts();
                _player?.ApplyTexts();

                // Those that depend on how things stand
                ApplyTheme(_currentConfig.IsDarkTheme);
                UpdateContextMenuButton();
                RefreshContextMenuEntry();
            }
            finally
            {
                ResumeLayout(performLayout: true);
            }

            RefreshGrids();
            pictureBox1.Invalidate();
            SetStatus(LanguageReport());
        }

        /// <summary>
        /// Empty when the language is as it should be; else what is wrong with its file, so that the person who translates it sees it as soon as
        /// the language is picked (the texts it lacks are shown in English).
        /// </summary>
        private static string LanguageReport()
        {
            var language = Loc.Language;
            var report = language.Check();
            if (report.IsClean)
                return string.Empty;

            var parts = new List<string>();
            if (report.Missing.Count > 0)
                parts.Add(string.Format(DialogStrings.LanguageMissing, report.Missing.Count));
            if (report.Unknown.Count > 0)
                parts.Add(string.Format(DialogStrings.LanguageUnknown, report.Unknown.Count));
            if (report.WrongPlaceholders.Count > 0)
                parts.Add(string.Format(DialogStrings.LanguageWrong, report.WrongPlaceholders.Count));
            if (report.FileProblems.Count > 0)
                parts.Add(string.Format(DialogStrings.LanguageFileProblem, report.FileProblems.Count));

            var first = report.Missing.Concat(report.Unknown).Concat(report.WrongPlaceholders).Concat(report.FileProblems).First();
            return string.Format(DialogStrings.LanguageReportFormat, language.NativeName, string.Join(", ", parts), first);
        }

        /// <summary>
        /// The texts of the controls that stand still: the buttons of the toolbar, the status bar and the search strip. The designer writes
        /// English into the ones it makes, so this runs once at the start too.
        /// </summary>
        private void ApplyControlTexts()
        {
            // The buttons of the designer
            newToolStripButton.Text = ToolbarStrings.New;
            openToolStripButton.Text = ToolbarStrings.Open;
            saveToolStripButton.Text = ToolbarStrings.Save;
            copyToolStripButton.Text = ToolbarStrings.Copy;
            pasteToolStripButton.Text = ToolbarStrings.Paste;
            helpToolStripButton.Text = ToolbarStrings.Help;
            themeToggleToolStripButton.ToolTipText = ToolbarStrings.ThemeTooltip;
            openSourceFolderToolStripButton.Text = ToolbarStrings.OpenSource;
            openSourceFolderToolStripButton.ToolTipText = ToolbarStrings.OpenSourceTooltip;
            openLikedFolderToolStripButton.Text = ToolbarStrings.OpenLiked;
            openLikedFolderToolStripButton.ToolTipText = ToolbarStrings.OpenLikedTooltip;
            loadLikedFolderToolStripButton.Text = ToolbarStrings.LoadLiked;
            loadLikedFolderToolStripButton.ToolTipText = ToolbarStrings.LoadLikedTooltip;
            generateVideoToolStripButton.Text = MainFormStrings.GenerateVideoButtonText;
            generateVideoToolStripButton.ToolTipText = MainFormStrings.GenerateVideoTooltip;
            toolStripStatusLabelGpsLink.ToolTipText = ToolbarStrings.GpsTooltip;
            if (toolStripStatusLabelGpsLink.Visible)
                toolStripStatusLabelGpsLink.Text = AppStrings.GpsMapLinkLabel;

            // The buttons made in code
            _alignDatesButton.Text = DateAlignmentStrings.ButtonText;
            _alignDatesButton.ToolTipText = DateAlignmentStrings.ButtonTooltip;
            _removeEmptyFoldersButton.Text = EmptyFoldersStrings.ButtonText;
            _removeEmptyFoldersButton.ToolTipText = EmptyFoldersStrings.ButtonTooltip;
            _findDuplicatesButton.Text = DuplicatesStrings.ButtonText;
            _findDuplicatesButton.ToolTipText = DuplicatesStrings.ButtonTooltip;
            ApplyVideoSpeechLanguage();
            ApplyRotateLanguage();
            ApplySearchLanguage();
        }

        /// <summary>The entry the context menu of the folders has is written into the registry in the language that was on then: write it again.</summary>
        private static void RefreshContextMenuEntry()
        {
            try
            {
                if (ContextMenuRegistry.IsRegistered())
                    ContextMenuRegistry.Register(Application.ExecutablePath);
            }
            catch
            {
                // The entry keeps the language it had; nobody asked for it to be changed
            }
        }

        /// <summary>
        /// The two grids read the help text of a row when it is selected, and the metadata grid its descriptions when it is filled: select the
        /// row that is selected again, and fill the metadata again with the photo that is shown.
        /// </summary>
        private void RefreshGrids()
        {
            var selected = settingsPropertyGrid.SelectedGridItem?.PropertyDescriptor?.Name;
            settingsPropertyGrid.SelectedObject = null;
            settingsPropertyGrid.SelectedObject = _currentConfig;
            if (selected is not null)
                SelectGridRow(settingsPropertyGrid, selected);

            if (pictureBox1.Tag is string file && _photoCache.TryGet(file, out var entry))
                ShowPhoto(entry);
        }

        private static void SelectGridRow(PropertyGrid grid, string propertyName)
        {
            var root = grid.SelectedGridItem;
            while (root?.Parent is not null)
                root = root.Parent;

            foreach (var item in Descendants(root))
            {
                if (item.PropertyDescriptor?.Name == propertyName)
                {
                    grid.SelectedGridItem = item;
                    return;
                }
            }
        }

        private static IEnumerable<GridItem> Descendants(GridItem? parent)
        {
            if (parent is null)
                yield break;

            foreach (GridItem child in parent.GridItems)
            {
                yield return child;
                foreach (var grandchild in Descendants(child))
                    yield return grandchild;
            }
        }
    }
}
