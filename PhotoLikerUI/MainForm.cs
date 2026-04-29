using System.Collections.Concurrent;

namespace PhotoLikerUI
{
    public partial class MainForm : Form
    {
        private Settings _currentConfig = new();
        private readonly GlobalConfig _globalConfig = GlobalConfig.Load();

        private readonly ConcurrentDictionary<string, Image> imageCache = [];
        private readonly ConcurrentDictionary<string, Image> _thumbCache = [];
        private CancellationTokenSource _fillCts = new();
        private CancellationTokenSource _thumbCts = new();

        // Zoom / pan
        private float zoomFactor = 1.0f;
        private bool _panning;
        private Point _panStart;    // mouse position when drag started (screen coords)
        private Point _scrollStart; // AutoScrollPosition when drag started

        // Thumbnail strip
        private const int PreviewSiblingCount = 10; // siblings on each side
        private int PreviewThumbSize => _currentConfig.ThumbnailSize;
        private readonly Dictionary<string, (PictureBox Thumb, Label Label)> _thumbControls = [];

        // JSON serialization options
        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public MainForm(string? initialFolder = null)
        {
            InitializeComponent();

            scrollPanel.AutoScroll = true;

            scrollPanel.MouseWheel += Panel2_MouseWheel;
            pictureBox1.MouseWheel += Panel2_MouseWheel;
            previewFlowPanel.MouseWheel += PreviewFlowPanel_MouseWheel;

            pictureBox1.Paint += PictureBox1_Paint;
            pictureBox1.MouseDown += PictureBox1_MouseDown;
            pictureBox1.MouseMove += PictureBox1_MouseMove;
            pictureBox1.MouseUp += PictureBox1_MouseUp;
            splitContainer3.Resize += (_, _) => AdjustRulerWidth();

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
            //Draw the start at left corner if file alrady copied to liked folder
            if (pictureBox1.Tag is string && File.Exists(_currentConfig.LikedFile))
            {
                using var startBrush = new SolidBrush(Color.FromArgb(MainFormConstants.LikedOverlayAlpha, Color.Green));
                using var font = new Font(this.Font.FontFamily, MainFormConstants.LikedCheckmarkFontSize, FontStyle.Bold);
                var msg = MainFormConstants.LikedCheckmark;

                var msgSize = e.Graphics.MeasureString(msg, font);

                e.Graphics.FillRectangle(startBrush, 0, 0, msgSize.Width, msgSize.Height);
                e.Graphics.DrawString(msg, font, Brushes.White, MainFormConstants.LikedCheckmarkOffset, MainFormConstants.LikedCheckmarkOffset);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveWindowPosition();
            SaveSettingsToJson();
            _globalConfig.LastFolder = _currentConfig.CurrentFolder;
            _globalConfig.Save();
            base.OnClosed(e);
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

        private static string GetSettingsFilePath(string folder) =>
            Path.Combine(
                string.IsNullOrWhiteSpace(folder) ? Environment.CurrentDirectory : folder,
                MainFormStrings.SettingsFileName);

        private void LoadSettingsFromJson(string? folder = null)
        {
            var settingsFilePath = GetSettingsFilePath(folder ?? Environment.CurrentDirectory);
            if (File.Exists(settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(settingsFilePath);
                    var loadedSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);
                    if (loadedSettings is not null)
                    {
                        _currentConfig = loadedSettings;
                        SetStatus(string.Format(MainFormStrings.SettingsLoaded, settingsFilePath));
                    }
                }
                catch (Exception ex)
                {
                    SetStatus(string.Format(MainFormStrings.SettingsLoadError, ex.Message));
                }
            }
        }

        private void LoadFolderSettings(string newFolder)
        {
            // Preserve window/theme settings that are not folder-specific
            var windowLeft = _currentConfig.WindowLeft;
            var windowTop = _currentConfig.WindowTop;
            var windowWidth = _currentConfig.WindowWidth;
            var windowHeight = _currentConfig.WindowHeight;
            var windowState = _currentConfig.WindowState;
            var screenIndex = _currentConfig.ScreenIndex;
            var isDarkTheme = _currentConfig.IsDarkTheme;
            var thumbSize = _currentConfig.ThumbnailSize;

            _currentConfig = new Settings { CurrentFolder = newFolder };
            LoadSettingsFromJson(newFolder);

            //// Always keep window and theme settings from the previous session
            //_currentConfig.CurrentFolder = newFolder;
            //_currentConfig.WindowLeft    = windowLeft;
            //_currentConfig.WindowTop     = windowTop;
            //_currentConfig.WindowWidth   = windowWidth;
            //_currentConfig.WindowHeight  = windowHeight;
            //_currentConfig.WindowState   = windowState;
            //_currentConfig.ScreenIndex   = screenIndex;
            //_currentConfig.IsDarkTheme   = isDarkTheme;
            //_currentConfig.ThumbnailSize = thumbSize;
        }

        private void SaveSettingsToJson()
        {
            var settingsFilePath = GetSettingsFilePath(_currentConfig.CurrentFolder);
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_currentConfig, _jsonOptions);
                File.WriteAllText(settingsFilePath, json);
                SetStatus(string.Format(MainFormStrings.SettingsSaved, settingsFilePath));
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
            _currentConfig.WindowState = WindowState == FormWindowState.Minimized
                ? FormWindowState.Normal
                : WindowState;
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
            WindowState = _currentConfig.WindowState;
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
            _thumbCache.Clear();
            ClearThumbnailControls();
            AdjustRulerWidth();
            settingsPropertyGrid.Refresh();

            var currentFile = pictureBox1.Tag as string;
            if (currentFile is not null)
                UpdateSiblingPreviews(currentFile);
        }

        private void OpenToolStripButton_Click(object sender, EventArgs e)
        {
            using var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = MainFormStrings.FolderBrowserSelectPhotos;
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

        private void ClearDisplay()
        {
            // Cancel any background cache operations
            _fillCts.Cancel();
            _fillCts = new CancellationTokenSource();
            _thumbCts.Cancel();
            _thumbCts = new CancellationTokenSource();

            // Detach images from controls BEFORE disposing cache entries so
            // GDI+ never tries to access a disposed Image (e.g. FrameDimensionsList).
            pictureBox1.Image = null;
            pictureBox1.Tag = null;

            foreach (var (thumb, _) in _thumbControls.Values)
                thumb.Image = null;

            // Now safe to dispose and clear the caches
            foreach (var img in imageCache.Values) img.Dispose();
            imageCache.Clear();
            foreach (var img in _thumbCache.Values) img.Dispose();
            _thumbCache.Clear();

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

        private void LoadFolder()
        {
            try
            {
                ClearThumbnailControls();

                var isAutoLiked = string.IsNullOrWhiteSpace(_currentConfig.LikedFolder) ||
                    string.Equals(Path.GetFileName(_currentConfig.LikedFolder), MainFormStrings.DefaultLikedFolderName, StringComparison.OrdinalIgnoreCase);
                _currentConfig.LikedFolder = isAutoLiked
                    ? Path.Combine(_currentConfig.CurrentFolder, MainFormStrings.DefaultLikedFolderName)
                    : _currentConfig.LikedFolder;
                _currentConfig.Files = GetAllFiles(_currentConfig.CurrentFolder, _currentConfig.GoThroughtSubFolders)
                    .Select(f => new PhotoFile(f, string.Empty))
                    .ToList();
                LoadFirstPhoto(_currentConfig.CurrentFilePath);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusFolderLoadError, ex.Message));
            }
            finally
            {
                settingsPropertyGrid.Refresh();
                SetStatus(string.Format(MainFormStrings.StatusFolderLoaded, _currentConfig.TotalFiles, _currentConfig.CurrentFolder));
                Text = string.Format(MainFormStrings.TitleFormat, _currentConfig.CurrentFolder);
            }
        }

        private void SetStatus(string statusMsg)
        {
            toolStripStatusLabel1.Text = statusMsg;
        }

        private void UpdateGpsLink(MetadataWrapper metadata)
        {
            var mapLink = metadata.MapLink;
            if (!string.IsNullOrEmpty(mapLink))
            {
                toolStripStatusLabelGpsLink.Text = MainFormStrings.GpsMapLinkLabel;
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
            SetStatus(string.Format(MainFormStrings.StatusKeyPressed, keyData));
            if (keyData == Keys.Space)
            {
                var currentFile = pictureBox1.Tag as string;
                if (currentFile is not null)
                    ToggleLike(currentFile);
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
                else if (keyData == Keys.O) // fit image to screen
                {
                    FitImageToScreen();
                    return true;
                }
                else
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }
        }

        #region Panning

        private bool IsEnlarged => pictureBox1.Dock != DockStyle.Fill;

        private void PictureBox1_MouseDown(object? sender, MouseEventArgs e)
        {
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

        private void ToggleLike(string currentFile)
        {
            if (!string.IsNullOrEmpty(_currentConfig.LikedFile) && File.Exists(_currentConfig.LikedFile))
                UnlikePhoto(currentFile);
            else
                LikePhoto(currentFile);
        }

        private void LikePhoto(string currentFile)
        {
            try
            {
                var likedFolder = _currentConfig.LikedFolder;
                if (string.IsNullOrWhiteSpace(likedFolder))
                    likedFolder = _currentConfig.LikedFolder = LikedFolderSelectFolderDialog();

                if (!Directory.Exists(likedFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(likedFolder);
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Error creating liked folder: {ex.Message}");
                        likedFolder = _currentConfig.LikedFolder = LikedFolderSelectFolderDialog();
                    }
                }

                var fileName = Path.GetFileName(currentFile);
                var destinationPath = Path.Combine(likedFolder, fileName);

                if (File.Exists(destinationPath))
                {
                    UnlikePhoto(currentFile);
                    return;
                    //HandledDuplicateLikedFile(currentFile, likedFolder, fileName, ref destinationPath);
                    //return;
                }
                else
                {
                    File.Copy(currentFile, destinationPath, true);
                    SetStatus(string.Format(MainFormStrings.StatusFileCopied, fileName));
                }

                var idx = _currentConfig.Files.IndexOf(currentFile);
                if (idx >= 0)
                    _currentConfig.Files[idx].LikedFilePath = destinationPath;

                _currentConfig.LikedFile = destinationPath;
                RefreshLikeState(currentFile);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusLikeError, ex.Message));
            }
        }

        private void HandledDuplicateLikedFile(string currentFile, string likedFolder, string fileName, ref string destinationPath)
        {
            var dlgRes = MessageBox.Show(
                                    string.Format(MainFormStrings.DuplicateDialogText, fileName),
                                    MainFormStrings.DuplicateDialogTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

            if (dlgRes == DialogResult.Yes)
            {
                using var saveFileDialog = new SaveFileDialog
                {
                    FileName = fileName,
                    InitialDirectory = likedFolder,
                    Title = MainFormStrings.SaveLikedPhotoDialogTitle,
                    DefaultExt = Path.GetExtension(fileName),
                    AddExtension = false,
                    CheckPathExists = true,
                    CheckWriteAccess = true,
                    OverwritePrompt = true,
                    ValidateNames = true,
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tiff;*.webp"
                };
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    destinationPath = saveFileDialog.FileName;
                    File.Copy(currentFile, destinationPath, false);
                    SetStatus(string.Format(MainFormStrings.StatusFileSaved, fileName, destinationPath));
                }
                else
                {
                    SetStatus(MainFormStrings.StatusLikeCancelled);
                    return;
                }
            }
            else if (dlgRes == DialogResult.No)
            {
                UnlikePhoto(currentFile);
                return;
            }
            else // Cancel
            {
                SetStatus(MainFormStrings.StatusLikeCancelled);
                return;
            }
        }

        private void UnlikePhoto(string currentFile)
        {
            try
            {
                var likedPath = _currentConfig.LikedFile;
                File.Delete(likedPath);
                SetStatus(string.Format(MainFormStrings.StatusFileRemoved, Path.GetFileName(likedPath)));

                var idx = _currentConfig.Files.IndexOf(currentFile);
                if (idx >= 0)
                    _currentConfig.Files[idx].LikedFilePath = string.Empty;

                _currentConfig.LikedFile = string.Empty;
                RefreshLikeState(currentFile);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusUnlikeError, ex.Message));
            }
        }

        private void RefreshLikeState(string currentFile)
        {
            // Cancel any in-flight thumbnail loading before touching the cache,
            // so no queued BeginInvoke can assign a disposed image to a PictureBox.
            _thumbCts.Cancel();

            // Remove from cache but do NOT dispose here — a queued BeginInvoke on the
            // UI thread may still hold a reference to this image; disposing it now would
            // cause "Parameter is not valid" inside PictureBox.Animate.
            _thumbCache.TryRemove(currentFile, out _);

            if (_thumbControls.TryGetValue(currentFile, out var pair))
            {
                previewFlowPanel.Controls.Remove(pair.Thumb);
                previewFlowPanel.Controls.Remove(pair.Label);
                pair.Thumb.Image = null;
                pair.Thumb.Dispose();
                pair.Label.Dispose();
                _thumbControls.Remove(currentFile);
            }

            settingsPropertyGrid.Refresh();
            pictureBox1.Invalidate();
            UpdateSiblingPreviews(currentFile);
        }

        private string LikedFolderSelectFolderDialog()
        {
            using var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = MainFormStrings.FolderBrowserSelectLiked;
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                return folderBrowserDialog.SelectedPath;
            }
            else
            {
                SetStatus(MainFormStrings.StatusNoFolderSelected);
                return null!;
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
                pictureBox1.Image = imageCache.GetOrAdd(file, f => ImageHelper.LoadImageWithCorrectOrientation(f));
                pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // Set the size mode to zoom
                pictureBox1.Tag = file; // Store the file path in the Tag property
                _currentConfig.CurrentFilePath = file;
                _currentConfig.CurrentFileName = Path.GetFileName(file);
                _currentConfig.CurrentIndex = _currentConfig.Files.IndexOf(file);
                if (_currentConfig.CurrentIndex >= 0)
                {
                    var photoFile = _currentConfig.Files[_currentConfig.CurrentIndex];
                    var suggestedLikedFilePath = Path.Combine(_currentConfig.LikedFolder, Path.GetFileName(photoFile.OriginalFilePath));
                    _currentConfig.LikedFile = File.Exists(photoFile.LikedFilePath)
                        ? photoFile.LikedFilePath
                        : File.Exists(suggestedLikedFilePath) ? suggestedLikedFilePath : string.Empty;
                }
                settingsPropertyGrid.Refresh(); // Refresh the property grid to show the new image properties
                var friendlyMeta = new FriendlyImageMetadata(pictureBox1.Image);
                var metadataWrapper = new MetadataWrapper(friendlyMeta);
                imageMetaPropertyGrid.SelectedObject = metadataWrapper;
                UpdateGpsLink(metadataWrapper);
                FillCacheAsync(file);
                UpdateSiblingPreviews(file);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusImageLoadError, ex.Message));
            }
        }

        /// <summary>
        /// Asynchronously populates the cache with Images from files near the currentFile left on cache size and right on cache size.
        /// </summary>        
        /// <param name="currentFile">The path to the file whose data will be used to populate the cache. Cannot be null or empty.</param>
        private void FillCacheAsync(string currentFile)
        {
            _fillCts.Cancel();
            _fillCts = new CancellationTokenSource();
            var token = _fillCts.Token;
            Task.Run(() => FillCache(currentFile, token), token);
        }

        private void FillCache(string currentFile, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(currentFile))
                throw new ArgumentException(MainFormStrings.ExCurrentFileNullOrEmpty, nameof(currentFile));

            var files = _currentConfig.Files;
            int currentIndex = files.IndexOf(currentFile);
            if (currentIndex < 0)
                throw new ArgumentException(MainFormStrings.ExCurrentFileNotInList, nameof(currentFile));

            int from = Math.Max(0, currentIndex - _currentConfig.CacheSize);
            int to = Math.Min(files.Count - 1, currentIndex + _currentConfig.CacheSize);

            var keepPaths = new HashSet<string>(
                Enumerable.Range(from, to - from + 1).Select(i => files[i].OriginalFilePath));

            // Evict and dispose entries that are no longer in the window
            foreach (var key in imageCache.Keys.ToList())
            {
                if (!keepPaths.Contains(key) && imageCache.TryRemove(key, out var evicted))
                {
                    evicted.Dispose();
                    if (_thumbCache.TryRemove(key, out var evictedThumb))
                        evictedThumb.Dispose();
                }
            }

            // Load missing entries within the window
            for (int i = from; i <= to; i++)
            {
                if (cancellationToken.IsCancellationRequested) return;

                string file = files[i].OriginalFilePath;
                if (!imageCache.ContainsKey(file))
                {
                    try
                    {
                        var image = ImageHelper.LoadImageWithCorrectOrientation(file);
                        if (!cancellationToken.IsCancellationRequested)
                            imageCache[file] = image;
                        else
                        {
                            image.Dispose();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStatus(string.Format(MainFormStrings.StatusCacheLoadError, file, ex.Message));
                    }
                }
            }
        }

        private IEnumerable<string> GetAllFiles(string currentFolder, bool goSubFolders)
        {
            return new DirectoryInfo(currentFolder).GetFiles("*.*",
                goSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(fi => _currentConfig.Extensions.Contains(fi.Extension.ToLowerInvariant()))
                .Select(fi => fi.FullName);
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
            _thumbCts.Cancel();
            _thumbCts = new CancellationTokenSource();
            var token = _thumbCts.Token;

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
                thumb.Image = null; // don't dispose — image is owned by _thumbCache
                thumb.Dispose();
                label.Dispose();
                _thumbControls.Remove(path);
            }

            var thumbsToLoad = new List<(PictureBox Thumb, string Path)>();

            for (int i = from; i <= to; i++)
            {
                string filePath = files[i].OriginalFilePath;
                bool isCurrent = i == idx;
                string suggestedLiked = Path.Combine(_currentConfig.LikedFolder, Path.GetFileName(filePath));
                bool isLiked = !string.IsNullOrEmpty(files[i].LikedFilePath)
                                   ? File.Exists(files[i].LikedFilePath)
                                   : File.Exists(suggestedLiked);

                if (_thumbControls.TryGetValue(filePath, out var existing))
                {
                    // Reuse — just update selection highlight
                    existing.Thumb.BackColor = isCurrent ? Color.CornflowerBlue : MainFormConstants.ThumbBackColor;
                    existing.Thumb.BorderStyle = isCurrent ? BorderStyle.Fixed3D : BorderStyle.None;
                    existing.Label.ForeColor = isCurrent ? Color.White : (isLiked ? Color.LightGreen : Color.LightGray);
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

                if (isLiked)
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

                if (_thumbCache.TryGetValue(filePath, out var cached))
                    thumb.Image = cached;
                else
                    thumbsToLoad.Add((thumb, filePath));

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

            Task.Run(() => LoadThumbnailsAsync(thumbsToLoad, token), token);
        }

        private void LoadThumbnailsAsync(List<(PictureBox Thumb, string Path)> pairs, CancellationToken token)
        {
            foreach (var (thumb, path) in pairs)
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    // Generate and cache the thumbnail
                    var thumbnail = _thumbCache.GetOrAdd(path, p =>
                    {
                        if (imageCache.TryGetValue(p, out var full))
                            return CreateThumbnail(full, PreviewThumbSize);

                        using var loaded = ImageHelper.LoadImageWithCorrectOrientation(p);
                        return CreateThumbnail(loaded, PreviewThumbSize);
                    });

                    if (token.IsCancellationRequested) return;

                    AssignThumbnailImage(thumb, thumbnail);
                }
                catch { /* skip broken images */ }
            }
        }

        private static void AssignThumbnailImage(PictureBox thumb, Image img)
        {
            if (thumb.IsDisposed) return;

            void Apply() { if (!thumb.IsDisposed) thumb.Image = img; }

            if (thumb.IsHandleCreated)
                thumb.BeginInvoke(Apply);
            else
                thumb.HandleCreated += (_, _) => Apply();
        }

        private static Bitmap CreateThumbnail(Image source, int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            int w = (int)(source.Width * scale);
            int h = (int)(source.Height * scale);
            g.DrawImage(source, (size - w) / 2, (size - h) / 2, w, h);
            return bmp;
        }

        private static void ThumbPaint_LikedBadge(object? sender, PaintEventArgs e)
        {
            if (sender is not PictureBox pb) return;

            var badge = MainFormConstants.LikedCheckmark;
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
    }
}
