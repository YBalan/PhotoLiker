namespace PhotoLikerUI
{
    public partial class MainForm : Form
    {
        private Settings _currentConfig = new();
        private readonly GlobalConfig _globalConfig = GlobalConfig.Load();

        // Decoded photos and thumbnails. Everything below is used on the UI thread only; see PhotoCache.
        private readonly PhotoCache _photoCache = new(MainFormConstants.MaxCachedImageBytes);
        private Dictionary<string, int> _indexByPath = new(StringComparer.Ordinal);
        private LikedLibrary? _liked;
        private GroupLibrary? _groups;
        private IReadOnlyList<string> _savedInNames = []; // folders holding a copy of the current photo, drawn on it
        private IReadOnlyList<GroupLegendEntry> _groupLegend = []; // all group folders, drawn as the legend
        private bool _likeInProgress; // a like / group copy is running
        private int _folderGeneration;
        private bool _jumpedFromGrid; // the photo being shown was chosen by typing its index in the settings grid
        private string _imageMessage = string.Empty; // shown in the viewer while there is no image (loading / failed)

        // Zoom / pan
        private float zoomFactor = 1.0f;
        private bool _panning;
        private Point _panStart;    // mouse position when drag started (screen coords)
        private Point _scrollStart; // AutoScrollPosition when drag started

        // Thumbnail strip
        private const int PreviewSiblingCount = 10; // siblings on each side
        private int PreviewThumbSize => _currentConfig.ThumbnailSize;
        private readonly Dictionary<string, (PictureBox Thumb, Label Label)> _thumbControls = [];

        public MainForm(string? initialFolder = null)
        {
            InitializeComponent();
            InitializeSearch();
            Text = WithDebugPrefix(Text);

            // Set application icon programmatically
            Icon = AppIcons.AppIcon();

            scrollPanel.AutoScroll = true;

            scrollPanel.MouseWheel += Panel2_MouseWheel;
            pictureBox1.MouseWheel += Panel2_MouseWheel;
            previewFlowPanel.MouseWheel += PreviewFlowPanel_MouseWheel;

            pictureBox1.Paint += PictureBox1_Paint;
            scrollPanel.MouseDown += (_, _) => FocusViewer();
            previewFlowPanel.MouseDown += (_, _) => FocusViewer();
            pictureBox1.MouseDown += PictureBox1_MouseDown;
            pictureBox1.MouseMove += PictureBox1_MouseMove;
            pictureBox1.MouseUp += PictureBox1_MouseUp;
            splitContainer3.Resize += (_, _) => AdjustRulerWidth();

            _photoCache.Loaded += PhotoCache_Loaded;
            _photoCache.ThumbReady += PhotoCache_ThumbReady;
            _photoCache.LoadFailed += PhotoCache_LoadFailed;
            _photoCache.Evicting += PhotoCache_Evicting;

            settingsPropertyGrid.PropertyValueChanged += SettingsPropertyGrid_PropertyValueChanged;

            LoadSettingsFromJson();

            if (!string.IsNullOrWhiteSpace(initialFolder))
                _currentConfig.CurrentFolder = initialFolder;

            // Fall back to the last opened folder from the global config
            if (string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder) &&
                !string.IsNullOrWhiteSpace(_globalConfig.LastFolder))
                _currentConfig.CurrentFolder = _globalConfig.LastFolder;

            // If a folder is known, also try loading its folder-specific config
            if (!string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder))
                LoadFolderSettings(_currentConfig.CurrentFolder);

            RestoreWindowPosition();
            LoadFolder();

            settingsPropertyGrid.SelectedObject = _currentConfig;
            UpdateContextMenuButton();

            // Recalculate ruler width once the form layout is finalised
            Load += (_, _) =>
            {
                AdjustRulerWidth();
                ApplyTheme(_currentConfig.IsDarkTheme);
            };
        }

        private void PictureBox1_Paint(object? sender, PaintEventArgs e)
        {
            if (pictureBox1.Image is null && !string.IsNullOrEmpty(_imageMessage))
            {
                TextRenderer.DrawText(e.Graphics, _imageMessage, Font, pictureBox1.ClientRectangle, SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }

            // Draw the check at the left corner if the file is saved anywhere: in the liked folder or in a group
            if (_savedInNames.Count > 0)
            {
                using var startBrush = new SolidBrush(Color.FromArgb(MainFormConstants.LikedOverlayAlpha, Color.Green));
                using var font = new Font(this.Font.FontFamily, MainFormConstants.LikedCheckmarkFontSize, FontStyle.Bold);
                var msg = AppStrings.LikedCheckmark;

                var msgSize = e.Graphics.MeasureString(msg, font);

                e.Graphics.FillRectangle(startBrush, 0, 0, msgSize.Width, msgSize.Height);
                e.Graphics.DrawString(msg, font, Brushes.White, MainFormConstants.LikedCheckmarkOffset, MainFormConstants.LikedCheckmarkOffset);

                DrawSavedInFolders(e.Graphics, msgSize);
            }

            DrawGroupLegend(e.Graphics);
            DrawFaces(e.Graphics);
        }

        /// <summary>
        /// Lists every group folder in the bottom-left corner of the viewer, so it is clear which key saves where; the
        /// ones that hold a copy of the current photo are marked. A long list continues in a further column.
        /// </summary>
        private void DrawGroupLegend(Graphics g)
        {
            var legend = _groupLegend;
            if (legend.Count == 0)
                return;

            const float pad = MainFormConstants.SavedInPadding;
            const float margin = MainFormConstants.GroupLegendMargin;
            const float columnGap = MainFormConstants.GroupLegendColumnGap;
            var format = StringFormat.GenericTypographic; // shared instance, must not be disposed

            using var font = new Font(this.Font.FontFamily, MainFormConstants.GroupLegendFontSize);
            using var markedFont = new Font(font, FontStyle.Bold);
            using var bgBrush = new SolidBrush(Color.FromArgb(MainFormConstants.SavedInBgAlpha, Color.Black));
            using var markBrush = new SolidBrush(Color.LightGreen);
            using var textBrush = new SolidBrush(Color.Gainsboro);

            float TextWidth(string text, Font f) => g.MeasureString(text, f, PointF.Empty, format).Width;

            var viewer = pictureBox1.ClientSize;
            var lineHeight = font.GetHeight(g) + 2;
            var markWidth = TextWidth(AppStrings.LikedCheckmark, markedFont) + 4;

            // As many rows as fit next to each other, spread evenly over the columns
            var fitting = Math.Max(1, (int)((viewer.Height - margin * 2 - pad * 2) / lineHeight));
            var columnCount = (int)Math.Ceiling(legend.Count / (double)fitting);
            var rows = (int)Math.Ceiling(legend.Count / (double)columnCount);

            var columnWidths = new float[columnCount];
            for (var i = 0; i < legend.Count; i++)
            {
                var entry = legend[i];
                var width = markWidth + TextWidth(entry.Name, entry.HasPhoto ? markedFont : font);
                columnWidths[i / rows] = Math.Max(columnWidths[i / rows], width);
            }

            var boxWidth = columnWidths.Sum() + columnGap * (columnCount - 1) + pad * 2;
            var boxHeight = rows * lineHeight + pad * 2;
            var left = margin;
            var top = viewer.Height - margin - boxHeight;

            g.FillRectangle(bgBrush, left, top, boxWidth, boxHeight);

            for (var i = 0; i < legend.Count; i++)
            {
                var entry = legend[i];
                var column = i / rows;
                var x = left + pad + columnWidths.Take(column).Sum() + columnGap * column;
                var y = top + pad + (i % rows) * lineHeight;

                if (entry.HasPhoto)
                {
                    g.DrawString(AppStrings.LikedCheckmark, markedFont, markBrush, x, y, format);
                    g.DrawString(entry.Name, markedFont, markBrush, x + markWidth, y, format);
                }
                else
                {
                    g.DrawString(entry.Name, font, textBrush, x + markWidth, y, format);
                }
            }
        }

        /// <summary>Writes the folders holding a copy of the current photo, each in brackets, right of the check.</summary>
        private void DrawSavedInFolders(Graphics g, SizeF checkSize)
        {
            const float pad = MainFormConstants.SavedInPadding;
            var maxTextWidth = pictureBox1.ClientSize.Width - checkSize.Width - pad * 2;
            if (maxTextWidth < 1)
                return;

            using var font = new Font(this.Font.FontFamily, MainFormConstants.SavedInFontSize, FontStyle.Bold);
            using var bgBrush = new SolidBrush(Color.FromArgb(MainFormConstants.SavedInBgAlpha, Color.Black));
            var text = GroupLibrary.FormatFolderList(_savedInNames, keepNamesTogether: true);

            // Long lists wrap to the width of the viewer
            var textSize = g.MeasureString(text, font, (int)maxTextWidth);
            var boxHeight = Math.Max(checkSize.Height, textSize.Height + pad * 2);

            g.FillRectangle(bgBrush, checkSize.Width, 0, textSize.Width + pad * 2, boxHeight);
            g.DrawString(text, font, Brushes.White,
                new RectangleF(checkSize.Width + pad, (boxHeight - textSize.Height) / 2, textSize.Width + 2, textSize.Height));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _folderGeneration++;
            _photoCache.Dispose();
            ShutDownSearch();
            SaveWindowPosition();
            SaveSettingsToJson();
            _globalConfig.LastFolder = _currentConfig.CurrentFolder;
            _globalConfig.Save();
            base.OnFormClosed(e);
        }

        private void ThemeToggleToolStripButton_Click(object? sender, EventArgs e)
        {
            _currentConfig.IsDarkTheme = !_currentConfig.IsDarkTheme;
            ApplyTheme(_currentConfig.IsDarkTheme);
        }

        private void ApplyTheme(bool dark)
        {
            ThemeManager.Apply(this, dark);
            themeToggleToolStripButton.Text = dark ? MainFormStrings.ThemeLightLabel : MainFormStrings.ThemeDarkLabel;
        }

        private void LoadSettingsFromJson(string? folder = null)
        {
            var directory = folder ?? Environment.CurrentDirectory;
            try
            {
                // The paths in the file are relative to the folder it is in; a photo folder opens the same
                // from any machine, whatever the drive letter or share it is reached through there
                if (Settings.Read(directory, pinCurrentFolder: folder is not null) is { } loadedSettings)
                {
                    _currentConfig = loadedSettings;
                    SetStatus(string.Format(MainFormStrings.SettingsLoaded, Settings.GetFilePath(directory)));
                }
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.SettingsLoadError, ex.Message));
            }
        }

        private void LoadFolderSettings(string newFolder)
        {
            _currentConfig = new Settings { CurrentFolder = newFolder };
            LoadSettingsFromJson(newFolder);
        }

        private void SaveSettingsToJson()
        {
            try
            {
                _currentConfig.Write();
                SetStatus(string.Format(MainFormStrings.SettingsSaved, Settings.GetFilePath(_currentConfig.CurrentFolder)));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.SettingsSaveError, ex.Message));
            }
        }

        private void SaveWindowPosition()
        {
            if (WindowState == FormWindowState.Normal)
            {
                _currentConfig.WindowLeft = Left;
                _currentConfig.WindowTop = Top;
                _currentConfig.WindowWidth = Width;
                _currentConfig.WindowHeight = Height;
            }
            else
            {
                _currentConfig.WindowLeft = RestoreBounds.Left;
                _currentConfig.WindowTop = RestoreBounds.Top;
                _currentConfig.WindowWidth = RestoreBounds.Width;
                _currentConfig.WindowHeight = RestoreBounds.Height;
            }
            _currentConfig.WindowState = (int)(WindowState == FormWindowState.Minimized
                ? FormWindowState.Normal
                : WindowState);
            _currentConfig.ScreenIndex = Array.IndexOf(Screen.AllScreens, Screen.FromControl(this));
        }

        private void RestoreWindowPosition()
        {
            var screens = Screen.AllScreens;
            var screen = _currentConfig.ScreenIndex >= 0 && _currentConfig.ScreenIndex < screens.Length
                ? screens[_currentConfig.ScreenIndex]
                : Screen.PrimaryScreen!;

            var bounds = new Rectangle(
                _currentConfig.WindowLeft, _currentConfig.WindowTop,
                Math.Max(MainFormConstants.MinWindowWidth, _currentConfig.WindowWidth),
                Math.Max(MainFormConstants.MinWindowHeight, _currentConfig.WindowHeight));

            // Make sure the window is actually visible on the target screen
            if (!screen.WorkingArea.IntersectsWith(bounds))
            {
                bounds.Location = new Point(
                    screen.WorkingArea.Left + MainFormConstants.WindowOffScreenOffset,
                    screen.WorkingArea.Top + MainFormConstants.WindowOffScreenOffset);
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            WindowState = (FormWindowState)_currentConfig.WindowState;
        }

        private void Panel2_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (pictureBox1.Image is null) return;

            if ((ModifierKeys & Keys.Control) == 0)
                return; // zoom only when Ctrl is held; let the panel scroll normally otherwise

            const float zoomStep = 0.01f;
            float oldZoom = zoomFactor;

            if (e.Delta > 0)
                zoomFactor += zoomStep;
            else if (e.Delta < 0)
                zoomFactor = Math.Max(zoomFactor - zoomStep, zoomStep);

            var mousePosInImage = pictureBox1.PointToClient(Cursor.Position);

            float relX = (float)mousePosInImage.X / pictureBox1.Width;
            float relY = (float)mousePosInImage.Y / pictureBox1.Height;

            int newWidth = (int)(pictureBox1.Image.Width * zoomFactor);
            int newHeight = (int)(pictureBox1.Image.Height * zoomFactor);

            pictureBox1.Size = new Size(newWidth, newHeight);

            pictureBox1.Dock = DockStyle.None; // Reset dock to allow manual resizing

            // adjust scroll to keep zoom centered around mouse
            scrollPanel.AutoScrollPosition = new Point(
                (int)(newWidth * relX - scrollPanel.ClientSize.Width / 2),
                (int)(newHeight * relY - scrollPanel.ClientSize.Height / 2)
                );

            //pictureBox1.Location = new Point((scrollPanel.ClientSize.Width - pictureBox1.Width) / 2, (scrollPanel.ClientSize.Height - pictureBox1.Height) / 2);

            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
        }

        private void PreviewFlowPanel_MouseWheel(object? sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0)
                return;

            const int sizeStep = 50;
            const int minSize = 50;
            const int maxSize = 600;

            if (e.Delta > 0)
                _currentConfig.ThumbnailSize = Math.Min(_currentConfig.ThumbnailSize + sizeStep, maxSize);
            else if (e.Delta < 0)
                _currentConfig.ThumbnailSize = Math.Max(_currentConfig.ThumbnailSize - sizeStep, minSize);
            else
                return;

            // Clear thumb controls so they are recreated with the new size
            ClearThumbnailControls();
            AdjustRulerWidth();
            settingsPropertyGrid.Refresh();

            var currentFile = pictureBox1.Tag as string;
            if (currentFile is not null)
                RefreshWindow(currentFile);
        }

        private void OpenToolStripButton_Click(object sender, EventArgs e)
        {
            using var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = AppStrings.FolderBrowserSelectPhotos;
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                SaveSettingsToJson();
                ClearDisplay();
                LoadFolderSettings(folderBrowserDialog.SelectedPath);
                _globalConfig.LastFolder = folderBrowserDialog.SelectedPath;
                _globalConfig.Save();
                settingsPropertyGrid.SelectedObject = _currentConfig;
                LoadFolder();

                RestoreWindowPosition();
                UpdateContextMenuButton();

                AdjustRulerWidth();
                ApplyTheme(_currentConfig.IsDarkTheme);
            }
        }

        // CurrentIndex jumps to that photo; the settings that decide which photos are listed, and in what order, relist the current folder
        private void SettingsPropertyGrid_PropertyValueChanged(object? s, PropertyValueChangedEventArgs e)
        {
            var name = e.ChangedItem?.PropertyDescriptor?.Name;
            if (name == nameof(Settings.CurrentIndex))
            {
                ShowTypedIndex();
                return;
            }

            if (name != nameof(Settings.GoThroughSubFolders) && name != nameof(Settings.Extensions) && name != nameof(Settings.SortOrder))
                return;

            if (string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder))
                return;

            SaveSettingsToJson();
            ClearDisplay();
            LoadFolder();
        }

        private void OpenSourceFolderToolStripButton_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder))
            {
                SetStatus(MainFormStrings.StatusSourceFolderNotSet);
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_currentConfig.CurrentFolder) { UseShellExecute = true });
            SetStatus(MainFormStrings.StatusOpenSourceFolder);
        }

        private void OpenLikedFolderToolStripButton_Click(object? sender, EventArgs e)
        {
            var liked = _currentConfig.LikedFolder;
            if (string.IsNullOrWhiteSpace(liked) || !Directory.Exists(liked))
            {
                SetStatus(MainFormStrings.StatusLikedFolderNotSet);
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(liked) { UseShellExecute = true });
            SetStatus(MainFormStrings.StatusOpenLikedFolder);
        }

        private void LoadLikedFolderToolStripButton_Click(object? sender, EventArgs e)
        {
            var likedFolder = _currentConfig.LikedFolder;
            if (string.IsNullOrWhiteSpace(likedFolder) || !Directory.Exists(likedFolder))
            {
                // Fall back to the default "Liked" subfolder under the current folder
                likedFolder = Path.Combine(_currentConfig.CurrentFolder, AppStrings.DefaultLikedFolderName);
            }

            if (!Directory.Exists(likedFolder))
            {
                SetStatus(MainFormStrings.StatusLikedFolderNotSet);
                return;
            }

            SaveSettingsToJson();
            ClearDisplay();
            LoadFolderSettings(likedFolder);
            _globalConfig.LastFolder = likedFolder;
            _globalConfig.Save();
            settingsPropertyGrid.SelectedObject = _currentConfig;
            LoadFolder();

            RestoreWindowPosition();
            UpdateContextMenuButton();

            AdjustRulerWidth();
            ApplyTheme(_currentConfig.IsDarkTheme);
        }

        private void GenerateVideoToolStripButton_Click(object? sender, EventArgs e)
        {
            var sourceFiles = _currentConfig.Files
                .Select(f => f.OriginalFilePath)
                .Where(File.Exists)
                .ToList();

            if (sourceFiles.Count == 0)
            {
                SetStatus(MainFormStrings.StatusVideoNoPhotos);
                return;
            }

            SetStatus(MainFormStrings.StatusVideoGenerating);
            generateVideoToolStripButton.Enabled = false;

            try
            {
                using var dialog = new VideoGenerationDialog(
                    _currentConfig.CurrentFolder,
                    sourceFiles,
                    (options, appendOutput, reportProgress) => VideoGenerationService.GenerateAsync(
                        VideoGenerationService.SortSourceFiles(sourceFiles, options.ReverseSourceFiles),
                        options.OutputPath,
                        options.WidthPixels,
                        options.HeightPixels,
                        options.StartFrameTimeSeconds,
                        options.EndFrameTimeSeconds,
                        options.VideoCodec,
                        options.Crf,
                        appendOutput,
                        reportProgress));

                dialog.ShowDialog(this);

                if (!string.IsNullOrWhiteSpace(dialog.GeneratedOutputPath) && File.Exists(dialog.GeneratedOutputPath))
                    SetStatus(string.Format(MainFormStrings.StatusVideoGenerated, dialog.GeneratedOutputPath));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusVideoGenerationError, ex.Message));
                MessageBox.Show(this, ex.Message, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                generateVideoToolStripButton.Enabled = true;
            }
        }

        private void ClearDisplay()
        {
            // Abandon a folder scan that is still running
            _folderGeneration++;

            // Detach images from controls BEFORE disposing cache entries so
            // GDI+ never tries to access a disposed Image (e.g. FrameDimensionsList).
            pictureBox1.Image = null;
            pictureBox1.Tag = null;
            _imageMessage = string.Empty;

            foreach (var (thumb, _) in _thumbControls.Values)
                thumb.Image = null;

            // Now safe to dispose the cache; loads still in flight are abandoned
            _photoCache.Clear();
            _indexByPath = new Dictionary<string, int>(StringComparer.Ordinal);
            _liked = null;
            _groups = null;
            _savedInNames = [];
            _groupLegend = [];
            _faces = [];
            _hoverFace = null;

            // Reset zoom/pan state
            zoomFactor = 1.0f;
            _panning = false;
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            scrollPanel.AutoScrollPosition = Point.Empty;

            // Clear metadata and GPS link
            imageMetaPropertyGrid.SelectedObject = null;
            toolStripStatusLabelGpsLink.Visible = false;
            toolStripStatusLabelGpsLink.Tag = null;

            // Clear thumbnail strip
            ClearThumbnailControls();
        }

        private void ClearThumbnailControls()
        {
            previewFlowPanel.SuspendLayout();
            foreach (var (thumb, label) in _thumbControls.Values)
            {
                previewFlowPanel.Controls.Remove(thumb);
                previewFlowPanel.Controls.Remove(label);
                thumb.Image = null;
                thumb.Dispose();
                label.Dispose();
            }
            _thumbControls.Clear();
            previewFlowPanel.ResumeLayout();
        }

        private async void LoadFolder()
        {
            var generation = ++_folderGeneration;
            var loaded = false;
            AttachFolderSearch();
            try
            {
                ClearThumbnailControls();

                _currentConfig.ApplyDefaultLikedFolder();

                var folder = _currentConfig.CurrentFolder;
                var recursive = _currentConfig.GoThroughSubFolders;
                var extensions = _currentConfig.Extensions;
                var sortOrder = _currentConfig.SortOrder;
                var likedFolder = _currentConfig.LikedFolder;
                SetStatus(string.Format(MainFormStrings.StatusFolderLoading, folder));

                // Listing a network folder can take a long time, so keep it off the UI thread
                var (files, liked, groups) = await Task.Run(() =>
                {
                    var found = PhotoScanner.Scan(folder, recursive, extensions, likedFolder, sortOrder);
                    return (found, LikedLibrary.Scan(likedFolder, found), GroupLibrary.Scan(likedFolder, found));
                });

                // Another folder was opened while this one was being listed
                if (generation != _folderGeneration)
                    return;

                liked.Apply(files);
                _liked = liked;
                _groups = groups;
                _currentConfig.Files = files;
                _indexByPath = new Dictionary<string, int>(files.Count, StringComparer.Ordinal);
                for (var i = 0; i < files.Count; i++)
                    _indexByPath[files[i].OriginalFilePath] = i;

                LoadFirstPhoto(_currentConfig.CurrentFilePath);
                loaded = true;
            }
            catch (Exception ex)
            {
                if (generation == _folderGeneration)
                    SetStatus(string.Format(AppStrings.StatusFolderLoadError, ex.Message));
            }
            finally
            {
                if (generation == _folderGeneration)
                {
                    settingsPropertyGrid.Refresh();
                    if (loaded)
                        SetStatus(string.Format(AppStrings.StatusFolderLoaded, _currentConfig.TotalFiles, _currentConfig.CurrentFolder));
                    UpdateWindowTitle(_currentConfig.CurrentFilePath);
                }
            }
        }

        private void SetStatus(string statusMsg)
        {
            toolStripStatusLabel1.Text = statusMsg;
        }

        private void UpdateWindowTitle(string? currentFilePath)
        {
            var titleFolder = _currentConfig.CurrentFolder;

            if (_resultsActive)
            {
                Text = WithDebugPrefix(string.Format(AppStrings.TitleFormat, $"{_resultsTitle} ({titleFolder})"));
                return;
            }

            if (_currentConfig.GoThroughSubFolders &&
                !string.IsNullOrWhiteSpace(currentFilePath) &&
                !string.IsNullOrWhiteSpace(titleFolder))
            {
                var currentFileFolder = Path.GetDirectoryName(currentFilePath);
                if (!string.IsNullOrWhiteSpace(currentFileFolder))
                {
                    var relativeFolder = Path.GetRelativePath(titleFolder, currentFileFolder);
                    if (!string.IsNullOrWhiteSpace(relativeFolder) &&
                        !string.Equals(relativeFolder, ".", StringComparison.Ordinal) &&
                        !relativeFolder.StartsWith("..", StringComparison.Ordinal))
                    {
                        titleFolder = Path.Combine(titleFolder, relativeFolder);
                    }
                }
            }

            Text = WithDebugPrefix(string.Format(AppStrings.TitleFormat, titleFolder));
        }

        private static string WithDebugPrefix(string title)
        {
#if DEBUG
            return MainFormStrings.DebugTitlePrefix + title;
#else
            return title;
#endif
        }

        private void HelpToolStripButton_Click(object? sender, EventArgs e)
        {
            MessageBox.Show(
                this,
                string.Format(MainFormStrings.AboutMessageFormat, AppVersionInfo.Version, AppVersionInfo.BuildDate),
                MainFormStrings.AboutTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void UpdateGpsLink(MetadataWrapper metadata)
        {
            var mapLink = metadata.MapLink;
            if (!string.IsNullOrEmpty(mapLink))
            {
                toolStripStatusLabelGpsLink.Text = AppStrings.GpsMapLinkLabel;
                toolStripStatusLabelGpsLink.Tag = mapLink;
                toolStripStatusLabelGpsLink.Visible = true;
            }
            else
            {
                toolStripStatusLabelGpsLink.Visible = false;
                toolStripStatusLabelGpsLink.Tag = null;
            }
        }

        private void ToolStripStatusLabelGpsLink_Click(object? sender, EventArgs e)
        {
            if (toolStripStatusLabelGpsLink.Tag is string url && !string.IsNullOrEmpty(url))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _resultsActive)
            {
                CloseSearchResults();
                return true;
            }

            // Typing in the search box (or any text box) is not a command for the viewer: Space, the arrows and the letters are its own
            if (IsTextEntry(msg.HWnd))
                return base.ProcessCmdKey(ref msg, keyData);

            SetStatus(string.Format(MainFormStrings.StatusKeyPressed, keyData));
            if (keyData == (Keys.Control | Keys.C) && !IsTextEntry(msg.HWnd)) // a text box keeps copying its own text
            {
                CopyCurrentPhotoToClipboard();
                return true;
            }
            else if (keyData == Keys.Space)
            {
                var currentFile = pictureBox1.Tag as string;
                if (currentFile is not null)
                    _ = ToggleLikeAsync(currentFile);
                return true;
            }
            else
                if (keyData == Keys.Right)
                {
                    // Logic to go to the next photo
                    var currentFile = pictureBox1.Tag as string;
                    if (currentFile is not null)
                    {
                        var files = _currentConfig.Files;
                        var currentIndex = files.IndexOf(currentFile);
                        if (currentIndex < files.Count - 1)
                        {
                            LoadImage(files[currentIndex + 1].OriginalFilePath);
                        }
                    }
                    return true;
                }
                else if (keyData == Keys.Left)
                {
                    // Logic to go to the previous photo
                    var currentFile = pictureBox1.Tag as string;
                    if (currentFile is not null)
                    {
                        var files = _currentConfig.Files;
                        var currentIndex = files.IndexOf(currentFile);
                        if (currentIndex > 0)
                        {
                            LoadImage(files[currentIndex - 1].OriginalFilePath);
                        }
                    }
                    return true;
                }
                else if (keyData == Keys.Home)
                {
                    ShowEdgePhoto(last: false);
                    return true;
                }
                else if (keyData == Keys.End)
                {
                    ShowEdgePhoto(last: true);
                    return true;
                }
                else if (keyData == Keys.PageDown)
                {
                    ShowNeighbourMonth(forward: true);
                    return true;
                }
                else if (keyData == Keys.PageUp)
                {
                    ShowNeighbourMonth(forward: false);
                    return true;
                }
                else if (keyData is Keys.D0 or Keys.NumPad0) // fit image to screen (0 is not a group key, group keys are 1-9 and A-Z)
                {
                    FitImageToScreen();
                    return true;
                }
                else if (TryGetGroupKey(keyData, out var groupKey) && !IsTextEntry(msg.HWnd))
                {
                    var currentFile = pictureBox1.Tag as string;
                    if (currentFile is not null)
                        _ = ToggleGroupAsync(currentFile, groupKey);
                    return true;
                }
                else
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }
        }

        /// <summary>
        /// A number was typed into the CurrentIndex row: shows that photo, or the nearest end of the list if the number
        /// is outside it. The row stays selected, so the next number can be typed straight away.
        /// </summary>
        private void ShowTypedIndex()
        {
            var files = _currentConfig.Files;
            if (files.Count == 0)
                return;

            var index = Math.Clamp(_currentConfig.CurrentIndex, 0, files.Count - 1);
            var target = files[index].OriginalFilePath;
            if (!string.Equals(target, pictureBox1.Tag as string, StringComparison.Ordinal))
            {
                _jumpedFromGrid = true;
                try
                {
                    LoadImage(target);
                }
                finally
                {
                    _jumpedFromGrid = false;
                }
            }

            _currentConfig.CurrentIndex = index; // the typed number may have been outside the list
            settingsPropertyGrid.Refresh();
        }

        /// <summary>
        /// PageDown / PageUp: shows the first photo of the next / the previous month of the list (see <see cref="MonthNavigation"/>).
        /// A photo's month is the one of its date in the index (the EXIF date, see PhotoDates) when the folder is indexed, otherwise
        /// the one of its file.
        /// </summary>
        private void ShowNeighbourMonth(bool forward)
        {
            var files = _currentConfig.Files;
            if (pictureBox1.Tag is not string current)
                return;

            var index = files.IndexOf(current);
            if (index < 0)
                return;

            var now = DateTime.Now;
            var target = MonthNavigation.FindMonthStart(files, index, photo => MonthOf(photo, now), forward);
            if (target < 0)
            {
                SetStatus(forward ? MainFormStrings.StatusNoNextMonth : MainFormStrings.StatusNoPreviousMonth);
                return;
            }

            LoadImage(files[target].OriginalFilePath);
            SetStatus(string.Format(MainFormStrings.StatusMonthShown, MonthNavigation.Describe(MonthOf(files[target], now)), target + 1, files.Count));
        }

        private int? MonthOf(PhotoFile photo, DateTime now) =>
            MonthNavigation.MonthOf(
                _folderSearch?.Index.FindFullPath(photo.OriginalFilePath)?.Date.Taken
                ?? PhotoLiker.Search.PhotoDates.FromFile(photo, now));

        /// <summary>Home / End: shows the first / the last photo of the list.</summary>
        private void ShowEdgePhoto(bool last)
        {
            var files = _currentConfig.Files;
            if (files.Count == 0)
                return;

            var target = files[last ? files.Count - 1 : 0].OriginalFilePath;
            if (!string.Equals(target, pictureBox1.Tag as string, StringComparison.Ordinal))
                LoadImage(target);
        }

        private void CopyToolStripButton_Click(object? sender, EventArgs e) => CopyCurrentPhotoToClipboard();

        /// <summary>Puts the shown photo on the clipboard as a file and as a picture, see <see cref="PhotoClipboard"/>.</summary>
        private void CopyCurrentPhotoToClipboard()
        {
            if (pictureBox1.Tag is not string file)
            {
                SetStatus(MainFormStrings.StatusCopyNoPhoto);
                return;
            }

            try
            {
                if (!File.Exists(file))
                    throw new FileNotFoundException(null, file);

                Clipboard.SetDataObject(PhotoClipboard.CreateDataObject(file, pictureBox1.Image), copy: true);
                SetStatus(string.Format(MainFormStrings.StatusPhotoCopied, Path.GetFileName(file)));
            }
            catch (Exception ex) // e.g. another program is holding the clipboard
            {
                SetStatus(string.Format(MainFormStrings.StatusCopyError, ex.Message));
            }
        }

        /// <summary>1-9 (also on the numpad) and A-Z, without Ctrl / Alt / Shift.</summary>
        private static bool TryGetGroupKey(Keys keyData, out char key)
        {
            key = keyData switch
            {
                >= Keys.D1 and <= Keys.D9 => (char)('1' + (keyData - Keys.D1)),
                >= Keys.NumPad1 and <= Keys.NumPad9 => (char)('1' + (keyData - Keys.NumPad1)),
                >= Keys.A and <= Keys.Z => (char)('A' + (keyData - Keys.A)),
                _ => default,
            };
            return key != default;
        }

        // Typing into a text box (e.g. a path in the settings grid) must not trigger the shortcuts
        private static bool IsTextEntry(IntPtr focusedWindow) =>
            Control.FromChildHandle(focusedWindow) is TextBoxBase or ComboBox or UpDownBase;

        /// <summary>
        /// Gives the keyboard focus to the photo. A text box (the edit of a grid row, the search box) keeps the keys for its own
        /// typing until the focus leaves it (see <see cref="IsTextEntry"/>), and a click on a PictureBox doesn't move the focus by itself.
        /// </summary>
        private void FocusViewer() => pictureBox1.Focus();

        #region Panning

        private bool IsEnlarged => pictureBox1.Dock != DockStyle.Fill;

        private void PictureBox1_MouseDown(object? sender, MouseEventArgs e)
        {
            FocusViewer();

            if (HandleFaceMouseDown(e))
                return;

            if (e.Button == MouseButtons.Left && IsEnlarged)
            {
                _panning = true;
                _panStart = Cursor.Position;
                _scrollStart = new Point(
                    -scrollPanel.AutoScrollPosition.X,
                    -scrollPanel.AutoScrollPosition.Y);
                pictureBox1.Cursor = Cursors.SizeAll;
            }
        }

        private void PictureBox1_MouseMove(object? sender, MouseEventArgs e)
        {
            if (HandleFaceMouseMove(e))
                return;

            if (!_panning) return;

            var delta = new Point(
                Cursor.Position.X - _panStart.X,
                Cursor.Position.Y - _panStart.Y);

            scrollPanel.AutoScrollPosition = new Point(
                _scrollStart.X - delta.X,
                _scrollStart.Y - delta.Y);
        }

        private void PictureBox1_MouseUp(object? sender, MouseEventArgs e)
        {
            if (HandleFaceMouseUp(e))
                return;

            if (_panning)
            {
                _panning = false;
                pictureBox1.Cursor = Cursors.Default;
            }
        }

        #endregion

        private void FitImageToScreen()
        {
            if (pictureBox1.Image is null) return;

            zoomFactor = 1.0f;
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            scrollPanel.AutoScrollPosition = Point.Empty;
            SetStatus(MainFormStrings.StatusImageFitted);
        }

        /// <summary>True if there is a copy of the photo in the liked folder or in any group folder.</summary>
        private bool IsSaved(string file) =>
            _indexByPath.TryGetValue(file, out var index) &&
            index < _currentConfig.Files.Count &&
            IsSaved(_currentConfig.Files[index]);

        private bool IsSaved(PhotoFile photo) =>
            !string.IsNullOrEmpty(photo.LikedFilePath) || _groups?.IsInAnyGroup(photo) == true;

        /// <summary>
        /// Copies the current photo to the liked folder, or removes the copy again. The file operations run in the
        /// background (the folder is usually on a network share); a second press while one is running is ignored.
        /// </summary>
        private Task ToggleLikeAsync(string currentFile) =>
            RunFileOperationAsync(currentFile, photo =>
                string.IsNullOrEmpty(photo.LikedFilePath) ? LikePhotoAsync(photo) : UnlikePhotoAsync(photo));

        /// <summary>
        /// Saves the current photo into the folder of a group (1-9, A-Z) under the liked folder, or removes it from
        /// there again. Same rules as <see cref="ToggleLikeAsync"/>.
        /// </summary>
        private Task ToggleGroupAsync(string currentFile, char key) =>
            RunFileOperationAsync(currentFile, photo => ToggleGroupPhotoAsync(photo, key));

        private async Task RunFileOperationAsync(string currentFile, Func<PhotoFile, Task> operation)
        {
            if (_likeInProgress)
                return;

            var index = _currentConfig.Files.IndexOf(currentFile);
            if (index < 0)
                return;

            var photo = _currentConfig.Files[index];
            _likeInProgress = true;
            try
            {
                await operation(photo);
            }
            finally
            {
                _likeInProgress = false;
            }
        }

        private async Task ToggleGroupPhotoAsync(PhotoFile photo, char key)
        {
            try
            {
                var likedFolder = await PrepareLikedFolderAsync();
                if (likedFolder is null)
                    return;

                var files = _currentConfig.Files;
                var groups = await EnsureGroupLibraryAsync(likedFolder);
                var result = await Task.Run(() => groups.Toggle(key, photo, files));

                // Another folder was opened while the copy was running: the result belongs to a list that is gone
                if (ReferenceEquals(files, _currentConfig.Files))
                    groups.Update(result);

                var fileName = Path.GetFileName(photo.OriginalFilePath);
                var groupName = Path.GetFileName(result.Folder);
                SetStatus(string.Format(
                    result.Added ? MainFormStrings.StatusFileSaved : MainFormStrings.StatusFileRemovedFromGroup,
                    fileName, groupName));
                RefreshLikeState();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusGroupError, ex.Message));
            }
        }

        private async Task LikePhotoAsync(PhotoFile photo)
        {
            try
            {
                var likedFolder = await PrepareLikedFolderAsync();
                if (likedFolder is null)
                    return;

                var library = await EnsureLikedLibraryAsync(likedFolder);

                // The liked folder was changed to one that already holds a copy: same as pressing Space on a liked photo
                if (!string.IsNullOrEmpty(photo.LikedFilePath))
                {
                    await UnlikePhotoAsync(photo);
                    return;
                }

                var source = photo.OriginalFilePath;
                var destination = library.GetNewCopyPath(photo);
                await Task.Run(() => LikedLibrary.CopyPhoto(source, destination));

                photo.LikedFilePath = destination;
                library.Add(destination, photo);
                if (IsCurrent(photo))
                    _currentConfig.LikedFile = destination;

                SetStatus(string.Format(MainFormStrings.StatusFileCopied, Path.GetFileName(source)));
                RefreshLikeState();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(AppStrings.StatusLikeError, ex.Message));
            }
        }

        private async Task UnlikePhotoAsync(PhotoFile photo)
        {
            try
            {
                var likedPath = photo.LikedFilePath;
                await Task.Run(() => File.Delete(likedPath));

                photo.LikedFilePath = string.Empty;
                _liked?.Remove(likedPath);
                if (IsCurrent(photo))
                    _currentConfig.LikedFile = string.Empty;

                SetStatus(string.Format(AppStrings.StatusFileRemoved, Path.GetFileName(likedPath)));
                RefreshLikeState();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(AppStrings.StatusUnlikeError, ex.Message));
            }
        }

        private bool IsCurrent(PhotoFile photo) =>
            string.Equals(pictureBox1.Tag as string, photo.OriginalFilePath, StringComparison.Ordinal);

        /// <summary>Makes sure the liked folder is set and exists; asks for one if not. Null if the user cancelled.</summary>
        private async Task<string?> PrepareLikedFolderAsync()
        {
            var likedFolder = _currentConfig.LikedFolder;
            if (string.IsNullOrWhiteSpace(likedFolder))
                likedFolder = ChooseLikedFolder();

            if (string.IsNullOrWhiteSpace(likedFolder))
                return null;

            try
            {
                var folder = likedFolder;
                await Task.Run(() => Directory.CreateDirectory(folder));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusCreateLikedFolderError, ex.Message));
                likedFolder = ChooseLikedFolder();
            }

            return string.IsNullOrWhiteSpace(likedFolder) ? null : likedFolder;
        }

        private string? ChooseLikedFolder()
        {
            var selected = LikedFolderSelectFolderDialog();
            if (selected is not null)
                _currentConfig.LikedFolder = selected;
            return selected;
        }

        /// <summary>The liked-folder index, rebuilt when the liked folder was changed in the settings grid.</summary>
        private async Task<LikedLibrary> EnsureLikedLibraryAsync(string likedFolder)
        {
            if (_liked is { } current && string.Equals(current.Folder, likedFolder, StringComparison.OrdinalIgnoreCase))
                return current;

            var files = _currentConfig.Files;
            var library = await Task.Run(() => LikedLibrary.Scan(likedFolder, files));

            if (ReferenceEquals(files, _currentConfig.Files))
            {
                library.Apply(files);
                _liked = library;
            }
            return library;
        }

        /// <summary>The group-folder index, rebuilt when the liked folder was changed in the settings grid.</summary>
        private async Task<GroupLibrary> EnsureGroupLibraryAsync(string likedFolder)
        {
            if (_groups is { } current && string.Equals(current.LikedFolder, likedFolder, StringComparison.OrdinalIgnoreCase))
                return current;

            var files = _currentConfig.Files;
            var library = await Task.Run(() => GroupLibrary.Scan(likedFolder, files));

            if (ReferenceEquals(files, _currentConfig.Files))
                _groups = library;
            return library;
        }

        /// <summary>
        /// Works out where the current photo is saved: the "Current File" row, the list next to the check and the
        /// legend of group folders. Everything comes from the in-memory index, so it is cheap enough for every photo.
        /// </summary>
        private void UpdateSavedInInfo()
        {
            var index = pictureBox1.Tag is string file ? _currentConfig.Files.IndexOf(file) : -1;
            if (index >= 0 && _groups is { } groups)
            {
                var photo = _currentConfig.Files[index];
                _savedInNames = groups.GetSavedInNames(photo);
                _groupLegend = groups.GetLegend(photo);
            }
            else
            {
                _savedInNames = [];
                _groupLegend = [];
            }

            _currentConfig.SavedInFolders = [.. _savedInNames];
        }

        private void RefreshLikeState()
        {
            UpdateSavedInInfo();
            settingsPropertyGrid.Refresh();
            pictureBox1.Invalidate();

            // Re-colour labels and badges of the thumbnail strip
            if (pictureBox1.Tag is string currentFile)
                UpdateSiblingPreviews(currentFile);
        }

        private string? LikedFolderSelectFolderDialog()
        {
            using var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = AppStrings.FolderBrowserSelectLiked;
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                return folderBrowserDialog.SelectedPath;
            }
            else
            {
                SetStatus(MainFormStrings.StatusNoFolderSelected);
                return null;
            }
        }

        private void LoadFirstPhoto(string currentFile)
        {
            var file = _currentConfig.Files.FirstOrDefault(f => string.Equals(f.OriginalFilePath, currentFile, StringComparison.Ordinal)) ?? _currentConfig.Files.FirstOrDefault();
            if (file is not null)
            {
                LoadImage(file.OriginalFilePath);
            }
            else
            {
                SetStatus(MainFormStrings.StatusNoPhotosFound);
            }
        }

        private void LoadImage(string file)
        {
            try
            {
                // Navigation state is updated right away, so arrow keys keep working while the image is still loading
                var index = _currentConfig.Files.IndexOf(file);
                pictureBox1.Tag = file; // Store the file path in the Tag property
                _currentConfig.CurrentFilePath = file;
                _currentConfig.CurrentFileName = Path.GetFileName(file);
                _currentConfig.CurrentIndex = index;
                _currentConfig.LikedFile = index >= 0 ? _currentConfig.Files[index].LikedFilePath : string.Empty;
                UpdateSavedInInfo();
                UpdateFaces();
                settingsPropertyGrid.Refresh(); // Refresh the property grid to show the new image properties
                if (!_jumpedFromGrid)
                    ScrollPropertyGridToTop(settingsPropertyGrid); // not when the photo was chosen in the grid: its row stays selected
                UpdateWindowTitle(file);

                if (_photoCache.TryGet(file, out var entry))
                    ShowPhoto(entry);
                else
                    ShowLoading();

                RefreshWindow(file);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(AppStrings.StatusImageLoadError, ex.Message));
            }
        }

        private void ShowPhoto(PhotoEntry entry)
        {
            _imageMessage = string.Empty;
            pictureBox1.Image = entry.Full;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // Set the size mode to zoom

            var metadataWrapper = new MetadataWrapper(entry.Metadata);
            imageMetaPropertyGrid.SelectedObject = metadataWrapper;
            ScrollPropertyGridToTop(imageMetaPropertyGrid);
            UpdateGpsLink(metadataWrapper);
        }

        private static void ScrollPropertyGridToTop(PropertyGrid propertyGrid)
        {
            var root = propertyGrid.SelectedGridItem;
            while (root?.Parent is not null)
                root = root.Parent;

            if (root is not null && root.GridItems.Count > 0)
                propertyGrid.SelectedGridItem = root.GridItems[0];
        }

        private void ShowLoading()
        {
            _imageMessage = MainFormStrings.ImageLoading;
            pictureBox1.Image = null;
            pictureBox1.Invalidate();
            imageMetaPropertyGrid.SelectedObject = null;
            toolStripStatusLabelGpsLink.Visible = false;
            toolStripStatusLabelGpsLink.Tag = null;
        }

        private void PhotoCache_Loaded(string file, PhotoEntry entry)
        {
            AssignThumbnail(file, entry);

            if (string.Equals(pictureBox1.Tag as string, file, StringComparison.Ordinal))
                ShowPhoto(entry);
        }

        private void PhotoCache_ThumbReady(string file, PhotoEntry entry) => AssignThumbnail(file, entry);

        private void AssignThumbnail(string file, PhotoEntry entry)
        {
            if (entry.Thumb is not null && _thumbControls.TryGetValue(file, out var pair))
                pair.Thumb.Image = entry.Thumb;
        }

        private void PhotoCache_LoadFailed(string file, Exception ex)
        {
            if (string.Equals(pictureBox1.Tag as string, file, StringComparison.Ordinal))
            {
                _imageMessage = string.Format(AppStrings.StatusImageLoadError, ex.Message);
                pictureBox1.Invalidate();
                SetStatus(_imageMessage);
            }
            else
            {
                SetStatus(string.Format(MainFormStrings.StatusCacheLoadError, file, ex.Message));
            }
        }

        // The cache is about to dispose the entry: let go of its images first so nothing paints a disposed Image.
        private void PhotoCache_Evicting(string file, PhotoEntry entry)
        {
            if (ReferenceEquals(pictureBox1.Image, entry.Full))
                pictureBox1.Image = null;

            if (_thumbControls.TryGetValue(file, out var pair) && ReferenceEquals(pair.Thumb.Image, entry.Thumb))
                pair.Thumb.Image = null;
        }

        private void RefreshWindow(string currentFile)
        {
            PrefetchAround(currentFile);
            UpdateSiblingPreviews(currentFile);
        }

        /// <summary>
        /// Tells the cache which photos to keep: the current one and its neighbours, nearest first.
        /// </summary>
        private void PrefetchAround(string currentFile)
        {
            var files = _currentConfig.Files;
            int currentIndex = files.IndexOf(currentFile);
            if (currentIndex < 0)
                return;

            if (_photoCache.ThumbSize != PreviewThumbSize)
            {
                ClearThumbnailControls(); // the controls still show thumbnails of the old size
                _photoCache.SetThumbSize(PreviewThumbSize);
            }

            // The thumbnail strip shows PreviewSiblingCount photos on each side, so cache at least those
            int radius = Math.Max(_currentConfig.CacheSize, PreviewSiblingCount);
            var window = new List<string> { currentFile };
            for (int distance = 1; distance <= radius; distance++)
            {
                if (currentIndex + distance < files.Count)
                    window.Add(files[currentIndex + distance].OriginalFilePath);
                if (currentIndex - distance >= 0)
                    window.Add(files[currentIndex - distance].OriginalFilePath);
            }

            _photoCache.SetWindow(window);
        }

        private void AdjustRulerWidth()
        {
            int panelWidth = PreviewThumbSize
                + previewFlowPanel.Padding.Horizontal
                + SystemInformation.VerticalScrollBarWidth
                + MainFormConstants.PreviewPanelBorderWidth;
            int splitterDist = splitContainer3.Width - panelWidth - splitContainer3.SplitterWidth;
            splitContainer3.SplitterDistance = Math.Max(splitContainer3.Panel1MinSize, splitterDist);
        }

        private void UpdateSiblingPreviews(string currentFile)
        {
            var files = _currentConfig.Files;
            int idx = files.IndexOf(currentFile);
            if (idx < 0) return;

            int from = Math.Max(0, idx - PreviewSiblingCount);
            int to = Math.Min(files.Count - 1, idx + PreviewSiblingCount);

            // Build set of file paths that should be visible now
            var visiblePaths = new HashSet<string>();
            for (int i = from; i <= to; i++)
                visiblePaths.Add(files[i].OriginalFilePath);

            previewFlowPanel.SuspendLayout();

            // Remove controls that scrolled out of the visible window
            var toRemove = _thumbControls.Keys.Where(p => !visiblePaths.Contains(p)).ToList();
            foreach (var path in toRemove)
            {
                var (thumb, label) = _thumbControls[path];
                previewFlowPanel.Controls.Remove(thumb);
                previewFlowPanel.Controls.Remove(label);
                thumb.Image = null; // don't dispose — image is owned by the cache
                thumb.Dispose();
                label.Dispose();
                _thumbControls.Remove(path);
            }

            for (int i = from; i <= to; i++)
            {
                string filePath = files[i].OriginalFilePath;
                bool isCurrent = i == idx;
                bool isLiked = IsSaved(files[i]);

                if (_thumbControls.TryGetValue(filePath, out var existing))
                {
                    // Reuse — just update selection highlight
                    existing.Thumb.BackColor = isCurrent ? Color.CornflowerBlue : MainFormConstants.ThumbBackColor;
                    existing.Thumb.BorderStyle = isCurrent ? BorderStyle.Fixed3D : BorderStyle.None;
                    existing.Label.ForeColor = isCurrent ? Color.White : (isLiked ? Color.LightGreen : Color.LightGray);
                    existing.Thumb.Invalidate(); // the liked badge may have changed
                    continue;
                }

                // Calculate the correct insert position so existing controls never need reordering.
                // Count how many controls from [from..i-1] are already in the panel.
                int insertAt = 0;
                for (int j = from; j < i; j++)
                    if (_thumbControls.ContainsKey(files[j].OriginalFilePath))
                        insertAt++;

                // New control needed
                var thumb = new PictureBox
                {
                    Size = new Size(PreviewThumbSize, PreviewThumbSize),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = isCurrent ? Color.CornflowerBlue : MainFormConstants.ThumbBackColor,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(4, 4, 4, 0),
                    Tag = filePath,
                    BorderStyle = isCurrent ? BorderStyle.Fixed3D : BorderStyle.None,
                };

                thumb.Paint += ThumbPaint_LikedBadge;

                var label = new Label
                {
                    Text = Path.GetFileName(filePath),
                    ForeColor = isCurrent ? Color.White : (isLiked ? Color.LightGreen : Color.LightGray),
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Width = PreviewThumbSize,
                    Height = MainFormConstants.PreviewLabelHeight,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(4, 0, 4, 4),
                    Cursor = Cursors.Hand,
                    Tag = filePath,
                };

                thumb.Click += PreviewThumb_Click;
                label.Click += PreviewThumb_Click;

                // Not cached yet: the cache assigns the thumbnail once it has been loaded
                if (_photoCache.TryGet(filePath, out var cached) && cached.Thumb is not null)
                    thumb.Image = cached.Thumb;

                _thumbControls[filePath] = (thumb, label);
                previewFlowPanel.Controls.Add(thumb);
                previewFlowPanel.Controls.Add(label);
                // Place the new pair at the correct position — only 2 SetChildIndex calls
                // instead of reordering all controls after the loop.
                previewFlowPanel.Controls.SetChildIndex(thumb, insertAt * 2);
                previewFlowPanel.Controls.SetChildIndex(label, insertAt * 2 + 1);
            }

            previewFlowPanel.ResumeLayout();
            AdjustRulerWidth();

            // Defer scroll until after layout is finalised so previewFlowPanel.Height is valid
            void ScrollToCurrent()
            {
                int thumbHeight = PreviewThumbSize + MainFormConstants.PreviewLabelHeight + MainFormConstants.PreviewThumbTotalVerticalMargin;
                int scrollY = (idx - from) * thumbHeight - previewFlowPanel.Height / 2 + thumbHeight / 2;
                previewFlowPanel.AutoScrollPosition = new Point(0, Math.Max(0, scrollY));
            }

            if (IsHandleCreated)
                BeginInvoke(ScrollToCurrent);
            else
                Load += (_, _) => ScrollToCurrent();
        }

        private void ThumbPaint_LikedBadge(object? sender, PaintEventArgs e)
        {
            if (sender is not PictureBox { Tag: string filePath } pb || !IsSaved(filePath)) return;

            var badge = AppStrings.LikedCheckmark;
            using var font = new Font(SystemFonts.DefaultFont.FontFamily, MainFormConstants.LikedBadgeFontSize, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(MainFormConstants.LikedBadgeBrushAlpha, Color.Green));

            var size = e.Graphics.MeasureString(badge, font);
            float x = pb.Width - size.Width - MainFormConstants.LikedBadgePadding;
            float y = pb.Height - size.Height - MainFormConstants.LikedBadgePadding;

            using var bg = new SolidBrush(Color.FromArgb(MainFormConstants.LikedBadgeBgAlpha, Color.Black));
            e.Graphics.FillRectangle(bg, x - MainFormConstants.LikedBadgeBgPadding, y - MainFormConstants.LikedBadgeBgPadding, size.Width + MainFormConstants.LikedBadgeBgPadding * 2, size.Height + MainFormConstants.LikedBadgeBgPadding * 2);
            e.Graphics.DrawString(badge, font, brush, x, y);
        }

        private void PreviewThumb_Click(object? sender, EventArgs e)
        {
            FocusViewer();

            if (sender is Control c && c.Tag is string filePath)
                LoadImage(filePath);
        }

        private void UpdateContextMenuButton()
        {
            bool registered = ContextMenuRegistry.IsRegistered();
            contextMenuToolStripButton.Text = registered ? MainFormStrings.ContextMenuUnregister : MainFormStrings.ContextMenuRegister;
            contextMenuToolStripButton.ToolTipText = registered
                ? MainFormStrings.ContextMenuUnregisterTooltip
                : MainFormStrings.ContextMenuRegisterTooltip;
        }

        private void contextMenuToolStripButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (ContextMenuRegistry.IsRegistered())
                {
                    ContextMenuRegistry.Unregister();
                    SetStatus(MainFormStrings.StatusContextMenuRemoved);
                }
                else
                {
                    var exePath = Application.ExecutablePath;
                    ContextMenuRegistry.Register(exePath);
                    SetStatus(MainFormStrings.StatusContextMenuRegistered);
                }
                UpdateContextMenuButton();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusContextMenuError, ex.Message));
            }
        }

        private void newToolStripButton_Click(object sender, EventArgs e)
        {

        }

        private void saveToolStripButton_Click(object sender, EventArgs e)
        {

        }
    }
}
