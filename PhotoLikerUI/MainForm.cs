
using System.Collections.Concurrent;

namespace PhotoLikerUI
{
    public partial class MainForm : Form
    {
        private Settings settings = new();

        private readonly ConcurrentDictionary<string, Image> imageCache = [];
        public MainForm()
        {
            InitializeComponent();

            scrollPanel.AutoScroll = true;

            scrollPanel.MouseWheel += Panel2_MouseWheel;
            pictureBox1.MouseWheel += Panel2_MouseWheel;

            pictureBox1.Paint += PictureBox1_Paint;

            LoadSettingsFromJson();
            LoadFolder();

            settingsPropertyGrid.SelectedObject = settings;
        }

        private void PictureBox1_Paint(object? sender, PaintEventArgs e)
        {
            //Draw the start at left corner if file alrady copied to liked folder
            if (pictureBox1.Tag is string && File.Exists(settings.CopiedFile))
            {
                var startBrush = new SolidBrush(Color.FromArgb(128, Color.Green));
                var font = new Font(this.Font.FontFamily, 25, FontStyle.Bold);
                var msg = "✓";

                var msgSize = e.Graphics.MeasureString(msg, font);

                e.Graphics.FillRectangle(startBrush, 0, 0, msgSize.Width, msgSize.Height);
                
                e.Graphics.DrawString(msg, font, Brushes.White, 2, 2);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSettingsToJson();
            base.OnClosed(e);
        }

        private void LoadSettingsFromJson()
        {
            
            // Implement loading settings from JSON file if needed
            var settingsFilePath = Path.Combine(Environment.CurrentDirectory, "settings.json");
            if (File.Exists(settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(settingsFilePath);
                    var loadedSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);
                    if (loadedSettings is not null)
                    {
                        settings = loadedSettings;
                        SetStatus($"Settings loaded from {settingsFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Error loading settings: {ex.Message}");
                }
            }
        }

        private void SaveSettingsToJson()
        {            
            // Implement saving settings to JSON file if needed
            var settingsFilePath = Path.Combine(Environment.CurrentDirectory, "settings.json");
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, json);
                SetStatus($"Settings saved to {settingsFilePath}");
            }
            catch (Exception ex)
            {
                SetStatus($"Error saving settings: {ex.Message}");
            }
        }

        private float zoomFactor = 1.0f;

        private void Panel2_MouseWheel(object? sender, MouseEventArgs e)
        {
            //pictureBox1.SizeMode = PictureBoxSizeMode.Normal;
            float zoomStep = 0.1f;
            float oldZoom = zoomFactor;

            if (e.Delta > 0)
                zoomFactor += zoomStep;
            else if (e.Delta < 0)
                zoomFactor = Math.Max(zoomFactor - zoomStep, 0.1f);

            var mousePosInImage = pictureBox1.PointToClient(Cursor.Position);

            float relX = (float)mousePosInImage.X / pictureBox1.Width;
            float relY = (float)mousePosInImage.Y / pictureBox1.Height;

            int newWidth = (int)(pictureBox1.Image.Width * zoomFactor);
            int newHeight = (int)(pictureBox1.Image.Height * zoomFactor);

            pictureBox1.Width = newWidth;
            pictureBox1.Height = newHeight;

            //scrollPanel.Height = newHeight;
            //scrollPanel.Width = newWidth;

            // adjust scroll to keep zoom centered around mouse
            scrollPanel.AutoScrollPosition = new Point(
                (int)(newWidth * relX - scrollPanel.ClientSize.Width / 2),
                (int)(newHeight * relY - scrollPanel.ClientSize.Height / 2)
                );
        }

        private void openToolStripButton_Click(object sender, EventArgs e)
        {
            using var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = "Select a folder containing photos";
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                settings.CurrentFolder = folderBrowserDialog.SelectedPath;
                LoadFolder();
            }
        }

        private void LoadFolder()
        {
            try
            {
                settings.LikedFolder = Directory.Exists(settings.LikedFolder) ? settings.LikedFolder : Path.Combine(settings.CurrentFolder, "Liked");
                settings.Files = GetAllFiles(settings.CurrentFolder, settings.GoThroughtSubFolders)
                    .Select(f => new PhotoFile(f, string.Empty))
                    .ToList();
                LoadFirstPhoto(settings.CurrentFilePath);
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading folder: {ex.Message}");
            }
            finally
            {
                settingsPropertyGrid.Refresh();
                SetStatus($"Loaded {settings.TotalFiles} photos from '{settings.CurrentFolder}'");
            }
        }

        private void SetStatus(string statusMsg)
        {
            toolStripStatusLabel1.Text = statusMsg;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            SetStatus($"Key pressed: {keyData}");
            if (keyData == Keys.Space) // like and move to liked folder
            {
                var currentFile = pictureBox1.Tag as string;
                if (currentFile is not null)
                {
                    try
                    {
                        var likedFolder = settings.LikedFolder;
                        if (string.IsNullOrWhiteSpace(likedFolder))
                        {
                            settings.LikedFolder = LikedFolderSelectFolderDialog();
                        }

                        if (!Directory.Exists(likedFolder))
                        {
                            try
                            {
                                Directory.CreateDirectory(likedFolder);
                            }
                            catch (Exception ex)
                            {
                                SetStatus($"Error creating liked folder: {ex.Message}");
                                likedFolder = settings.LikedFolder = LikedFolderSelectFolderDialog();
                            }
                        }

                        var fileName = Path.GetFileName(currentFile);
                        var destinationPath = Path.Combine(likedFolder, fileName);
                        if (File.Exists(destinationPath))
                        {
                            var dlgRes = MessageBox.Show($"File '{fileName}' already exists in the liked folder. Do you want It will be overwritten?", "File Exists", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                            if (dlgRes == DialogResult.No)
                            {
                                var saveFileDialog = new SaveFileDialog
                                {
                                    FileName = fileName,
                                    InitialDirectory = likedFolder,
                                    Title = "Save Liked Photo",
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
                                    SetStatus($"File '{fileName}' has been saved to '{destinationPath}'.");
                                }
                                else
                                {
                                    SetStatus("No file selected for saving liked photo.");
                                    destinationPath = string.Empty;
                                }
                            }
                            else
                            {
                                File.Copy(currentFile, destinationPath, true);
                                SetStatus($"File '{fileName}' has been overwritten in the liked folder.");
                            }
                        }
                        else
                        {
                            File.Copy(currentFile, destinationPath, true);
                            SetStatus($"File '{fileName}' has been copied to the liked folder.");
                        }

                        var currIdx = settings.Files.IndexOf(currentFile);
                        if (currIdx >= 0)
                        {
                            settings.Files[currIdx].LikedFilePath = destinationPath;
                        }

                        settings.CopiedFile = destinationPath;

                        settingsPropertyGrid.Refresh();
                        pictureBox1.Invalidate(); // Redraw the PictureBox to show the new state
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Error liking photo: {ex.Message}");
                    }
                }
                return true;
            }
            else
            if (keyData == Keys.Right)
            {
                // Logic to go to the next photo
                var currentFile = pictureBox1.Tag as string;
                if (currentFile is not null)
                {
                    var files = settings.Files;
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
                    var files = settings.Files;
                    var currentIndex = files.IndexOf(currentFile);
                    if (currentIndex > 0)
                    {
                        LoadImage(files[currentIndex - 1].OriginalFilePath);
                    }
                }
                return true;
            }
            else
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        private string LikedFolderSelectFolderDialog()
        {
            using var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = "Select a folder to save liked photos";
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                return folderBrowserDialog.SelectedPath;
            }
            else
            {
                SetStatus("No folder selected for liked photos.");
                return null!;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
        }

        private void LoadFirstPhoto(string currentFile)
        {
            var file = settings.Files.FirstOrDefault(f => string.Equals(f.OriginalFilePath, currentFile, StringComparison.Ordinal)) ?? settings.Files.FirstOrDefault();
            if (file is not null)
            {
                LoadImage(file.OriginalFilePath);
            }
            else
            {
                SetStatus("No photos found in the selected folder.");
            }
        }

        private void LoadImage(string file)
        {
            try
            {
                pictureBox1.Image = imageCache.GetOrAdd(file, ImageHelper.LoadImageWithCorrectOrientation(file));
                pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // Set the size mode to zoom
                pictureBox1.Tag = file; // Store the file path in the Tag property
                settings.CurrentFilePath = file;
                settings.CurrentFileName = Path.GetFileName(file);
                settings.CurrentIndex = settings.Files.IndexOf(file);
                if (settings.CurrentIndex >= 0)
                {
                    var photoFile = settings.Files[settings.CurrentIndex];
                    var suggestedLikedFilePath = Path.Combine(settings.LikedFolder, Path.GetFileName(photoFile.OriginalFilePath));
                    settings.CopiedFile = File.Exists(photoFile.LikedFilePath)
                        ? photoFile.LikedFilePath
                        : File.Exists(suggestedLikedFilePath) ? suggestedLikedFilePath : string.Empty;
                }
                settingsPropertyGrid.Refresh(); // Refresh the property grid to show the new image properties
                imageMetaPropertyGrid.SelectedObject = new MetadataWrapper(new FriendlyImageMetadata(pictureBox1.Image).Properties);
                FillCacheAsync(file);
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading image: {ex.Message}");
            }
        }

        /// <summary>
        /// Asynchronously populates the cache with Images from files near the currentFile left on cache size and right on cache size.
        /// </summary>        
        /// <param name="currentFile">The path to the file whose data will be used to populate the cache. Cannot be null or empty.</param>
        private void FillCacheAsync(string currentFile)
        {
            Task.Run(() => FillCache(currentFile));
        }

        private void FillCache(string currentFile)
        {
            if (string.IsNullOrEmpty(currentFile))
            {
                throw new ArgumentException("Current file cannot be null or empty.", nameof(currentFile));
            }
            var files = settings.Files;
            int currentIndex = files.IndexOf(currentFile);
            if (currentIndex < 0)
            {
                throw new ArgumentException("Current file is not in the list of files.", nameof(currentFile));
            }
            // Clear the cache
            imageCache.Clear();
            // Load images around the current file index
            for (int i = Math.Max(0, currentIndex - settings.CacheSize); i <= Math.Min(files.Count - 1, currentIndex + settings.CacheSize); i++)
            {
                string file = files[i].OriginalFilePath;
                if (!imageCache.ContainsKey(file))
                {
                    try
                    {
                        var image = ImageHelper.LoadImageWithCorrectOrientation(file);
                        imageCache[file] = image;
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Error loading image {file}: {ex.Message}");
                    }
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            //ZoomPhoto(pictureBox1, e.Delta > 0 ? 1.1f : 0.9f);
        }

        private void ZoomPhoto(PictureBox pBox, float mouseWheelDelta)
        {

            if (pBox.Image is not null)
            {
                // Calculate the new size based on the mouse wheel delta
                var newWidth = (int)(pBox.Image.Width * mouseWheelDelta);
                var newHeight = (int)(pBox.Image.Height * mouseWheelDelta);
                // Set the new size to the PictureBox
                pBox.Size = new Size(newWidth, newHeight);
                // Optionally, you can also adjust the location to keep it centered
                pBox.Location = new Point((this.ClientSize.Width - pBox.Width) / 2, (this.ClientSize.Height - pBox.Height) / 2);
            }
        }

        private IEnumerable<string> GetAllFiles(string currentFolder, bool goSubFolders)
        {
            return new DirectoryInfo(currentFolder).GetFiles("*.*",
                goSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(fi => settings.PhotoExts.Contains(fi.Extension.ToLowerInvariant()))
                .Select(fi => fi.FullName);

        }
    }
}
