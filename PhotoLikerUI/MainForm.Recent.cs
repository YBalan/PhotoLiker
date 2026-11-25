namespace PhotoLikerUI
{
    /// <summary>
    /// The folders that were opened last (see <see cref="RecentFolders"/>): the menu that a right-click on the Open button shows, and the Jump List of the taskbar button, where
    /// Windows lets the person pin a folder. A folder is put on the list when it is opened, however it was reached: the Open button, a recent folder, Load Liked Folder, the
    /// folder the program was started on (the Explorer menu, the Jump List) or the one it was left on.
    ///
    /// The list lives in the config file of the program and is read from it each time it is used, written at once when it changes: the Jump List starts a second window of the program
    /// on a folder, and the window that is closed last must not take away what the other one added.
    /// </summary>
    public partial class MainForm
    {
        private const int RecentMenuLength = 90; // characters of a path in the menu, before its middle is shortened

        private ContextMenuStrip? _recentMenu;

        private void InitializeRecentFolders()
        {
            // A right-click on the button (a click with the left one opens the folder dialog, as it did)
            openToolStripButton.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    ShowRecentMenu(toolStrip1.PointToScreen(openToolStripButton.Bounds.Location + (Size)e.Location));
            };

            // The window is on the screen first; the Jump List is written a moment later
            Shown += (_, _) => BeginInvoke(SyncRecentJumpList);
        }

        /// <summary>The folders opened last, newest first, as the file of the program has them now.</summary>
        internal IReadOnlyList<string> LoadRecentFolders()
        {
            _globalConfig.RecentFolders = RecentFolders.Remember(GlobalConfig.Load().RecentFolders, null);
            return _globalConfig.RecentFolders;
        }

        /// <summary>
        /// Puts the folder first on the list, in the config of this window (the caller saves it, and writes the Jump List: the window that is just being made leaves that for later).
        /// False if the list is as it was, because there is no folder in <paramref name="folder"/> or it was first already.
        /// </summary>
        private bool PutOnRecentList(string? folder)
        {
            if (RecentFolders.Normalize(folder).Length == 0)
                return false;

            var before = LoadRecentFolders();
            _globalConfig.RecentFolders = RecentFolders.Remember(before, folder);
            return !_globalConfig.RecentFolders.SequenceEqual(before, StringComparer.Ordinal);
        }

        /// <summary>Takes the folder off the list and off the Jump List (it is not there any more, or the person said so).</summary>
        internal void ForgetRecentFolder(string folder)
        {
            _globalConfig.RecentFolders = RecentFolders.Forget(LoadRecentFolders(), folder);
            _globalConfig.Save();
            SyncRecentJumpList();
        }

        /// <summary>Writes the list to the Jump List. What the person removed there (Remove from this list) is taken off the list of the program too.</summary>
        internal void SyncRecentJumpList()
        {
            if (IsDisposed || Disposing)
                return;

            var result = RecentJumpList.Update(LoadRecentFolders(), RecentStrings.JumpListCategory);
            if (result.Removed.Count == 0)
                return;

            _globalConfig.RecentFolders = RecentFolders.Remember(_globalConfig.RecentFolders.Where(folder => !result.Removed.Any(gone => RecentFolders.Same(gone, folder))), null);
            _globalConfig.Save();
        }

        private void ShowRecentMenu(Point screen)
        {
            _recentMenu?.Dispose();
            _recentMenu = new ContextMenuStrip { Name = "recentFoldersMenu", ShowItemToolTips = true };
            ApplyMenuTheme(_recentMenu);
            FillRecentMenu(_recentMenu);
            _recentMenu.Show(screen);
        }

        private void FillRecentMenu(ContextMenuStrip menu)
        {
            var folders = LoadRecentFolders();
            var current = _currentConfig.CurrentFolder;

            if (folders.Count == 0)
                menu.Items.Add(new ToolStripMenuItem(RecentStrings.MenuNone) { Enabled = false, Name = "recentNoneMenuItem" });

            for (var i = 0; i < folders.Count; i++)
            {
                var folder = folders[i];
                var number = i + 1;
                var item = new ToolStripMenuItem((number < 10 ? "&" : string.Empty) + number + "  " + RecentFolders.Shorten(folder, RecentMenuLength).Replace("&", "&&"))
                {
                    Name = "recentFolderMenuItem" + number,
                    Tag = folder,
                    ToolTipText = folder,
                    Checked = RecentFolders.Same(folder, current),
                };
                item.Click += (_, _) => OpenRecentFolder(folder);
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            var clear = new ToolStripMenuItem(RecentStrings.MenuClear) { Name = "recentClearMenuItem", Enabled = folders.Count > 0 };
            clear.Click += (_, _) =>
            {
                _globalConfig.RecentFolders = [];
                _globalConfig.Save();
                SyncRecentJumpList();
            };
            menu.Items.Add(clear);
        }

        /// <summary>Opens a folder of the list; one that is not there any more is taken off it.</summary>
        internal void OpenRecentFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                SetStatus(string.Format(RecentStrings.StatusFolderGoneFormat, folder));
                ForgetRecentFolder(folder);
                return;
            }

            SwitchToFolder(folder);
        }

        /// <summary>Shows the folder: its own settings are read, and it goes first on the list of the folders opened last and is the one the program starts on next time.</summary>
        private void SwitchToFolder(string folder)
        {
            SaveSettingsToJson();
            ClearDisplay();
            LoadFolderSettings(folder);
            _globalConfig.LastFolder = folder;
            PutOnRecentList(folder);
            _globalConfig.Save(); // the folder the program starts on next time, and the list
            SyncRecentJumpList();
            settingsPropertyGrid.SelectedObject = _currentConfig;
            LoadFolder();

            RestoreWindowPosition();
            UpdateContextMenuButton();

            AdjustRulerWidth();
            ApplyTheme(_currentConfig.IsDarkTheme);
        }
    }
}
