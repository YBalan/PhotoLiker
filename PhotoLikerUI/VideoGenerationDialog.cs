namespace PhotoLikerUI
{
    internal sealed class VideoGenerationDialog : Form
    {
        private readonly Func<VideoGenerationOptions, IReadOnlyList<string>, Action<string>, Action<int>, Task> _generateVideoAsync;
        private readonly string _likedFolder;
        private readonly IReadOnlyCollection<string> _extensions;

        private int _sourceGeneration;

        // True while an item of the source combo gets its new text (a count), which is not a pick
        private bool _refreshingSourceItem;

        // The output folder the dialog put in the box last: while the box still holds it, picking another source moves it along
        private string _automaticOutputFolder;

        private readonly ComboBox _sourceCombo = new();
        private readonly Label _countLabel = new();
        private readonly ComboBox _sortOrderCombo = new();
        private readonly TextBox _outputFolderTextBox = new();
        private readonly TextBox _outputFileNameTextBox = new();
        private readonly TextBox _audioTextBox = new();
        private readonly Button _audioPlayButton = new();
        private readonly Button _audioStopButton = new();
        private readonly Button _audioClearButton = new();
        private readonly Label _audioStatusLabel = new();
        private readonly AudioPreview _audioPreview = new();
        private readonly NumericUpDown _widthNumeric = new();
        private readonly NumericUpDown _heightNumeric = new();
        private readonly NumericUpDown _frameTimeStartNumeric = new();
        private readonly NumericUpDown _frameTimeEndNumeric = new();
        private readonly ComboBox _codecCombo = new();
        private readonly NumericUpDown _crfNumeric = new();
        private readonly ProgressBar _progressBar = new();
        private readonly TextBox _outputLogTextBox = new();
        private readonly List<Control> _inputControls = [];
        private readonly Button _firstImageButton = new();
        private readonly Button _generateButton = new();
        private readonly Button _runButton = new();
        private readonly Button _cancelButton = new();

        private bool _isGenerating;

        public string? GeneratedOutputPath { get; private set; }

        /// <param name="defaultOutputFolder">The folder of the loaded photos: where the video goes while the loaded photos are the source.</param>
        /// <param name="currentPhotos">
        /// The photos that are loaded. The other sources, the liked folder and each group folder that is there, are looked up and counted
        /// in the background, so the dialog opens at once; the liked folder is the one that is picked.
        /// </param>
        /// <param name="defaultSortOrder">The order that is picked when the dialog opens.</param>
        /// <param name="dark">True: the dialog is in the dark theme.</param>
        public VideoGenerationDialog(
            string defaultOutputFolder,
            IReadOnlyList<PhotoFile> currentPhotos,
            string likedFolder,
            IReadOnlyCollection<string> extensions,
            PhotoSortOrder defaultSortOrder,
            bool dark,
            Func<VideoGenerationOptions, IReadOnlyList<string>, Action<string>, Action<int>, Task> generateVideoAsync)
        {
            var current = new Source("Current", null, defaultOutputFolder, "No photos loaded") { Photos = [.. currentPhotos] };
            var liked = new Source("Liked", likedFolder, likedFolder, EmptyFolderText);
            _automaticOutputFolder = liked.OutputFolder;
            _likedFolder = likedFolder;
            _extensions = extensions;
            _generateVideoAsync = generateVideoAsync;

            Text = MainFormStrings.VideoDialogTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 372);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 3,
                RowCount = 14
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            for (var row = 0; row < 12; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _sourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _sourceCombo.Width = 220;
            _sourceCombo.DropDownWidth = 420; // a group folder can carry the search it was made for in its name
            _sourceCombo.Items.Add(current);
            _sourceCombo.Items.Add(liked);
            _sourceCombo.SelectedItem = liked;

            _countLabel.AutoSize = true;
            _countLabel.Margin = new Padding(12, 6, 0, 0);

            _sortOrderCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _sortOrderCombo.Width = 220;
            Choice<PhotoSortOrder>[] sortOrders = [
                new(PhotoSortOrder.OldestFirst, "Oldest first"),
                new(PhotoSortOrder.NewestFirst, "Newest first"),
                new(PhotoSortOrder.ByName, "By name"),
                new(PhotoSortOrder.None, "As listed (no sorting)")
            ];
            _sortOrderCombo.Items.AddRange(sortOrders);
            _sortOrderCombo.SelectedIndex = Math.Max(0, Array.FindIndex(sortOrders, c => c.Value == defaultSortOrder));

            _outputFolderTextBox.Text = liked.OutputFolder;
            _outputFolderTextBox.Dock = DockStyle.Fill;
            _outputFileNameTextBox.Text = $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";

            _audioTextBox.Dock = DockStyle.Fill;
            var audioBrowseButton = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            audioBrowseButton.Click += (_, _) => BrowseAudio();
            _audioPlayButton.Text = "Play audio";
            _audioStopButton.Text = "Stop";
            _audioClearButton.Text = "Clear";
            _audioStatusLabel.AutoSize = true;
            _audioStatusLabel.Margin = new Padding(8, 6, 0, 0);

            var toolTip = new ToolTip();
            toolTip.SetToolTip(_audioTextBox, AudioHint);
            toolTip.SetToolTip(audioBrowseButton, AudioHint);

            _widthNumeric.Minimum = 320;
            _widthNumeric.Maximum = 7680;
            _widthNumeric.Value = 1920;

            _heightNumeric.Minimum = 240;
            _heightNumeric.Maximum = 4320;
            _heightNumeric.Value = 1080;

            _frameTimeStartNumeric.DecimalPlaces = 2;
            _frameTimeStartNumeric.Increment = 0.05M;
            _frameTimeStartNumeric.Minimum = 0.04M;
            _frameTimeStartNumeric.Maximum = 60M;
            _frameTimeStartNumeric.Value = 0.50M;

            _frameTimeEndNumeric.DecimalPlaces = 2;
            _frameTimeEndNumeric.Increment = 0.05M;
            _frameTimeEndNumeric.Minimum = 0.04M;
            _frameTimeEndNumeric.Maximum = 60M;
            _frameTimeEndNumeric.Value = 0.50M;

            _codecCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _codecCombo.Items.AddRange(["libx264", "libx265"]);
            _codecCombo.SelectedIndex = 0;

            _crfNumeric.Minimum = 0;
            _crfNumeric.Maximum = 51;
            _crfNumeric.Value = 23;

            var browseButton = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            browseButton.Click += (_, _) => BrowseOutputFolder();

            layout.Controls.Add(new Label { Text = "Photos from:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var sourcePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            sourcePanel.Controls.Add(_sourceCombo);
            sourcePanel.Controls.Add(_countLabel);
            layout.Controls.Add(sourcePanel, 1, 0);
            layout.SetColumnSpan(sourcePanel, 2);

            layout.Controls.Add(new Label { Text = "Sort order:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(_sortOrderCombo, 1, 1);
            layout.SetColumnSpan(_sortOrderCombo, 2);

            layout.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            layout.Controls.Add(_outputFolderTextBox, 1, 2);
            layout.Controls.Add(browseButton, 2, 2);

            layout.Controls.Add(new Label { Text = "Output file name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            layout.Controls.Add(_outputFileNameTextBox, 1, 3);
            layout.SetColumnSpan(_outputFileNameTextBox, 2);

            layout.Controls.Add(new Label { Text = "Audio (optional):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            layout.Controls.Add(_audioTextBox, 1, 4);
            layout.Controls.Add(audioBrowseButton, 2, 4);

            var audioPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            audioPanel.Controls.Add(_audioPlayButton);
            audioPanel.Controls.Add(_audioStopButton);
            audioPanel.Controls.Add(_audioClearButton);
            audioPanel.Controls.Add(_audioStatusLabel);
            layout.Controls.Add(audioPanel, 1, 5);
            layout.SetColumnSpan(audioPanel, 2);

            layout.Controls.Add(new Label { Text = "Resolution:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 6);
            var resolutionPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            resolutionPanel.Controls.Add(_widthNumeric);
            resolutionPanel.Controls.Add(new Label { Text = "x", AutoSize = true, Margin = new Padding(8, 6, 8, 0) });
            resolutionPanel.Controls.Add(_heightNumeric);
            layout.Controls.Add(resolutionPanel, 1, 6);

            _firstImageButton.Text = "Use first image";
            _firstImageButton.AutoSize = true;
            _firstImageButton.Dock = DockStyle.Fill;
            _firstImageButton.Click += (_, _) => ApplyFirstImageResolution();
            layout.Controls.Add(_firstImageButton, 2, 6);

            layout.Controls.Add(new Label { Text = "Frame time range (sec):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
            var frameRangePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            frameRangePanel.Controls.Add(new Label { Text = "Start", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeStartNumeric);
            frameRangePanel.Controls.Add(new Label { Text = "End", AutoSize = true, Margin = new Padding(12, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeEndNumeric);
            layout.Controls.Add(frameRangePanel, 1, 7);
            layout.SetColumnSpan(frameRangePanel, 2);

            layout.Controls.Add(new Label { Text = "Video codec:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 8);
            layout.Controls.Add(_codecCombo, 1, 8);
            layout.SetColumnSpan(_codecCombo, 2);

            layout.Controls.Add(new Label { Text = "Quality (CRF):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 9);
            layout.Controls.Add(_crfNumeric, 1, 9);
            layout.SetColumnSpan(_crfNumeric, 2);

            var hintLabel = new Label
            {
                Text = "Lower CRF = better quality and larger file size.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(hintLabel, 0, 10);
            layout.SetColumnSpan(hintLabel, 3);

            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Visible = false;
            layout.Controls.Add(_progressBar, 0, 11);
            layout.SetColumnSpan(_progressBar, 3);

            _outputLogTextBox.Multiline = true;
            _outputLogTextBox.ReadOnly = true;
            _outputLogTextBox.WordWrap = false;
            _outputLogTextBox.ScrollBars = ScrollBars.Both;
            _outputLogTextBox.Dock = DockStyle.Fill;
            _outputLogTextBox.Visible = false;
            layout.Controls.Add(_outputLogTextBox, 0, 12);
            layout.SetColumnSpan(_outputLogTextBox, 3);

            var buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                WrapContents = false,
                Margin = new Padding(0, 8, 0, 0)
            };

            _generateButton.Text = "Generate";
            _generateButton.AutoSize = true;
            _generateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _generateButton.Click += async (_, _) => await GenerateAsync();

            _runButton.Text = "Play";
            _runButton.AutoSize = true;
            _runButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _runButton.Enabled = false;
            _runButton.Visible = false;
            _runButton.Click += (_, _) => RunGeneratedVideo();

            _cancelButton.Text = "Cancel";
            _cancelButton.AutoSize = true;
            _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelButton.DialogResult = DialogResult.Cancel;

            buttonsPanel.Controls.Add(_generateButton);
            buttonsPanel.Controls.Add(_runButton);
            buttonsPanel.Controls.Add(_cancelButton);

            layout.Controls.Add(buttonsPanel, 0, 13);
            layout.SetColumnSpan(buttonsPanel, 3);

            Controls.Add(layout);
            AcceptButton = _generateButton;
            CancelButton = _cancelButton;

            _inputControls.AddRange([
                _sourceCombo,
                _sortOrderCombo,
                _outputFolderTextBox,
                _outputFileNameTextBox,
                _audioTextBox,
                audioBrowseButton,
                _widthNumeric,
                _heightNumeric,
                _frameTimeStartNumeric,
                _frameTimeEndNumeric,
                _codecCombo,
                _crfNumeric,
                browseButton
            ]);

            FormClosing += (_, e) =>
            {
                if (_isGenerating)
                    e.Cancel = true;
            };

            // Listening to the audio: a new file or a cleared box ends the one that plays
            _audioTextBox.TextChanged += (_, _) =>
            {
                _audioPreview.Stop();
                UpdateAudioButtons();
            };
            _audioPlayButton.Click += async (_, _) => await PlayAudioAsync();
            _audioStopButton.Click += (_, _) => _audioPreview.Stop();
            _audioClearButton.Click += (_, _) => _audioTextBox.Clear();
            _audioPreview.Changed += (_, _) => UpdateAudioButtons();
            Disposed += (_, _) => _audioPreview.Dispose();
            UpdateAudioButtons();

            ThemeManager.Apply(this, dark);

            // The theme paints a label in the colour of a control; here they sit on the panels and show their colour. The hint stays dimmed.
            foreach (var label in Flatten(layout).OfType<Label>())
                label.BackColor = Color.Transparent;
            hintLabel.ForeColor = SystemColors.GrayText;

            // An input shows the colour of an input (white in the light theme), so that an empty one is not taken for a disabled one
            var inputBack = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            foreach (var input in Flatten(layout).Where(control => control is TextBox or NumericUpDown or ComboBox))
                input.BackColor = inputBack;

            // The loaded photos are known at once; the folders are listed in the background and counted one by one
            _sourceCombo.SelectedIndexChanged += async (_, _) =>
            {
                if (_refreshingSourceItem)
                    return;

                FollowOutputFolder();
                await ShowSourceAsync();
            };
            _ = ShowSourceAsync();
            _ = CountFoldersAsync();
        }

        private static IEnumerable<Control> Flatten(Control root) => new[] { root }.Concat(root.Controls.Cast<Control>().SelectMany(Flatten));

        private Source SelectedSource => (Source)_sourceCombo.SelectedItem!;

        private PhotoSortOrder SelectedSortOrder => ((Choice<PhotoSortOrder>)_sortOrderCombo.SelectedItem!).Value;

        /// <summary>
        /// Says what the picked source needs saying (it is being counted, has no photos, could not be listed), waiting for its
        /// folder to be listed if that is not done yet. How many photos there are is in the name of the source in the combo.
        /// </summary>
        private async Task ShowSourceAsync()
        {
            var source = SelectedSource;
            var generation = ++_sourceGeneration;

            if (source.Photos is null)
            {
                _countLabel.Text = "Counting...";
                UpdateAvailability();

                try
                {
                    await ListAsync(source);
                }
                catch (Exception ex)
                {
                    if (!IsDisposed && generation == _sourceGeneration)
                        _countLabel.Text = $"Cannot list the photos: {ex.Message}";
                    return;
                }

                // Closed, or another source picked, while the folder was being listed
                if (IsDisposed || generation != _sourceGeneration)
                    return;
            }

            _countLabel.Text = source.Photos!.Count > 0 ? string.Empty : source.EmptyText;
            UpdateAvailability();
        }

        /// <summary>
        /// Looks up the group folders that are in the liked folder, offers each as a source and counts the photos of the liked folder and
        /// of the groups, one folder after the other (a slow disk is not asked for everything at once). Done with the dialog open, so
        /// that it opens at once. A folder that cannot be read keeps a "?" for its count; picking it tries again and says why it failed.
        /// </summary>
        private async Task CountFoldersAsync()
        {
            IReadOnlyList<GroupFolder> groups = [];
            var likedCount = CountQuietlyAsync(SelectedSource);
            try
            {
                groups = await Task.Run(() => PhotoGroups.FindFolders(_likedFolder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // No group folders to offer
            }

            if (IsDisposed)
                return;

            var sources = groups.Select(group => new Source(Path.GetFileName(group.Path), group.Path, group.Path, EmptyFolderText)).ToList();
            foreach (var source in sources)
                _sourceCombo.Items.Add(source);

            await likedCount;
            foreach (var source in sources)
            {
                if (IsDisposed)
                    return;

                await CountQuietlyAsync(source);
            }
        }

        private async Task CountQuietlyAsync(Source source)
        {
            try
            {
                await ListAsync(source);
            }
            catch (Exception)
            {
                // Shown as "?" in the combo, and as the reason when the source is picked
            }
        }

        /// <summary>
        /// Lists the photos of a source folder on another thread, once: a source that is being listed, or is listed, is not listed again,
        /// and one that failed is tried again. The count in the name of the source in the combo is brought up to date.
        /// </summary>
        private Task<List<PhotoFile>> ListAsync(Source source) => source.Listing ??= ListCoreAsync(source);

        private async Task<List<PhotoFile>> ListCoreAsync(Source source)
        {
            try
            {
                source.Photos = await Task.Run(() => ListPhotos(source));
                source.Failed = false;
            }
            catch
            {
                source.Failed = true;
                source.Listing = null;
                throw;
            }
            finally
            {
                if (!IsDisposed)
                    RefreshSourceItem(source);
            }

            return source.Photos;
        }

        /// <summary>Shows the name of the source with its new count in the combo, without that being a pick.</summary>
        private void RefreshSourceItem(Source source)
        {
            var index = _sourceCombo.Items.IndexOf(source);
            if (index < 0)
                return;

            _refreshingSourceItem = true;
            try
            {
                _sourceCombo.Items[index] = source;
            }
            finally
            {
                _refreshingSourceItem = false;
            }
        }

        /// <summary>The video goes to the folder its photos are in, unless the output folder was changed by hand (typed, or browsed to).</summary>
        private void FollowOutputFolder()
        {
            var folder = SelectedSource.OutputFolder;
            if (string.Equals(_outputFolderTextBox.Text.Trim(), _automaticOutputFolder.Trim(), StringComparison.OrdinalIgnoreCase))
                _outputFolderTextBox.Text = folder;

            _automaticOutputFolder = folder;
        }

        private List<PhotoFile> ListPhotos(Source source) =>
            VideoSources.Copies(source.Folder ?? throw new InvalidOperationException("The loaded photos are not listed from a folder."), _extensions);

        /// <summary>Generating and taking the size of the first image need photos: the picked source has to be listed and not empty.</summary>
        private void UpdateAvailability()
        {
            var ready = !_isGenerating && SelectedSource.Photos is { Count: > 0 };
            _generateButton.Enabled = ready;
            _firstImageButton.Enabled = ready;
        }

        private async Task GenerateAsync()
        {
            if (_isGenerating || !ValidateInputs() || SelectedSource.Photos is not { Count: > 0 } photos)
                return;

            var options = CreateOptions();
            var sortOrder = SelectedSortOrder;

            _audioPreview.Stop();
            ExpandForProgress();
            SetBusyState(true);
            SetProgress(0);

            try
            {
                // Which photos are still there is found out here and not when the dialog opens, which would keep it from opening on a slow disk
                AppendOutput($"Checking {Plural.Count(photos.Count, "photo", "photos")}...");
                var (files, missing) = await Task.Run(() => FindFiles(photos, sortOrder));
                if (missing > 0)
                    AppendOutput($"{Plural.Count(missing, "photo is", "photos are")} not there any more and left out.");
                if (files.Count == 0)
                    throw new FileNotFoundException("None of the photos is there any more.");

                // ffmpeg is told not to turn the photos by their EXIF orientation (it gets it wrong for the photos next to a turned one),
                // so the ones that have to be turned are copied upright first, as the app shows them
                AppendOutput("Looking for photos that have to be turned upright (EXIF orientation)...");
                using var upright = await UprightPhotos.CreateAsync(files, options.WidthPixels, options.HeightPixels, SetProgress);
                AppendOutput(upright.Turned > 0
                    ? $"{Plural.Count(upright.Turned, "photo", "photos")} turned upright."
                    : "No photo has to be turned.");
                if (upright.Failed > 0)
                    AppendOutput($"{Plural.Count(upright.Failed, "photo", "photos")} could not be turned and come out as stored.");
                SetProgress(0);

                if (options.AudioPath is not null)
                    AppendOutput($"Audio: {options.AudioPath} (played over and over if it is shorter than the video, cut where the video ends)");

                AppendOutput($"Starting ffmpeg: {options.OutputPath}");
                await _generateVideoAsync(options, upright.Files, AppendOutput, SetProgress);

                if (!File.Exists(options.OutputPath))
                    throw new FileNotFoundException($"Generated file not found: {options.OutputPath}", options.OutputPath);

                GeneratedOutputPath = options.OutputPath;
                SetProgress(100);
                AppendOutput($"Video generation completed: {options.OutputPath}");
                _runButton.Visible = true;
                _runButton.Enabled = true;
                SetBusyState(false);
            }
            catch (Exception ex)
            {
                AppendOutput(ex.Message);
                MessageBox.Show(this, ex.Message, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetBusyState(false);
            }
        }

        private VideoGenerationOptions CreateOptions()
        {
            if (!_outputFileNameTextBox.Text.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                _outputFileNameTextBox.Text += ".mp4";

            var outputFolder = _outputFolderTextBox.Text.Trim();
            var outputFileName = _outputFileNameTextBox.Text.Trim();

            return new VideoGenerationOptions(
                outputFolder,
                outputFileName,
                Path.Combine(outputFolder, outputFileName),
                (int)_widthNumeric.Value,
                (int)_heightNumeric.Value,
                (double)_frameTimeStartNumeric.Value,
                (double)_frameTimeEndNumeric.Value,
                _codecCombo.SelectedItem?.ToString() ?? "libx264",
                (int)_crfNumeric.Value,
                string.IsNullOrWhiteSpace(_audioTextBox.Text) ? null : _audioTextBox.Text.Trim());
        }

        /// <summary>The photos in the order of the video, leaving out the ones that are gone since they were listed.</summary>
        private static (List<string> Files, int Missing) FindFiles(List<PhotoFile> photos, PhotoSortOrder sortOrder)
        {
            var files = VideoGenerationService.SortSourceFiles(photos, sortOrder)
                .Select(photo => photo.OriginalFilePath)
                .Where(File.Exists)
                .ToList();

            return (files, photos.Count - files.Count);
        }

        private void ExpandForProgress()
        {
            if (_progressBar.Visible)
                return;

            _progressBar.Visible = true;
            _outputLogTextBox.Visible = true;
            ClientSize = new Size(840, 560);
        }

        private void SetBusyState(bool busy)
        {
            _isGenerating = busy;
            foreach (var control in _inputControls)
                control.Enabled = !busy;

            UpdateAvailability();
            UpdateAudioButtons();
            _cancelButton.Enabled = !busy;
            _runButton.Enabled = !busy && !string.IsNullOrWhiteSpace(GeneratedOutputPath) && File.Exists(GeneratedOutputPath);
        }

        private void SetProgress(int percent)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => SetProgress(percent));
                return;
            }

            _progressBar.Value = Math.Clamp(percent, _progressBar.Minimum, _progressBar.Maximum);
        }

        private void AppendOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (InvokeRequired)
            {
                BeginInvoke(() => AppendOutput(text));
                return;
            }

            _outputLogTextBox.AppendText(text + Environment.NewLine);
            _outputLogTextBox.SelectionStart = _outputLogTextBox.TextLength;
            _outputLogTextBox.ScrollToCaret();
        }

        private void BrowseOutputFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = MainFormStrings.FolderBrowserSelectVideoOutput,
                SelectedPath = _outputFolderTextBox.Text
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
                _outputFolderTextBox.Text = dialog.SelectedPath;
        }

        private const string AudioHint =
            "The sound of the video. Played over and over if it is shorter than the video, and cut where the video ends. Anything ffmpeg can read: mp3, m4a, wav, flac, ogg...";

        private void BrowseAudio()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose the audio of the video",
                Filter = "Audio files|*.mp3;*.m4a;*.aac;*.wav;*.flac;*.ogg;*.opus;*.wma|All files|*.*",
            };

            var current = _audioTextBox.Text.Trim();
            if (current.Length > 0 && Path.GetDirectoryName(current) is { Length: > 0 } folder && Directory.Exists(folder))
            {
                dialog.InitialDirectory = folder;
                dialog.FileName = Path.GetFileName(current);
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
                _audioTextBox.Text = dialog.FileName;
        }

        private async Task PlayAudioAsync()
        {
            try
            {
                await _audioPreview.PlayAsync(_audioTextBox.Text.Trim());
            }
            catch (Exception ex) when (!IsDisposed)
            {
                MessageBox.Show(this, ex.Message, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Play needs a file and nothing playing, Stop needs something that plays or is made ready to, and nothing is touched while generating.</summary>
        private void UpdateAudioButtons()
        {
            if (IsDisposed || Disposing)
                return;

            var hasAudio = !string.IsNullOrWhiteSpace(_audioTextBox.Text);
            var state = _audioPreview.State;
            _audioPlayButton.Enabled = !_isGenerating && hasAudio && state == AudioPreviewState.Idle;
            _audioStopButton.Enabled = state != AudioPreviewState.Idle;
            _audioClearButton.Enabled = !_isGenerating && hasAudio;
            _audioStatusLabel.Text = state switch
            {
                AudioPreviewState.Preparing => "Preparing...",
                AudioPreviewState.Playing => "Playing...",
                _ => string.Empty,
            };
        }

        private void ApplyFirstImageResolution()
        {
            var firstImagePath = GetFirstImagePath();
            if (string.IsNullOrWhiteSpace(firstImagePath) || !File.Exists(firstImagePath))
            {
                MessageBox.Show(this, "No source image is available.", MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                using var image = ImageHelper.LoadImageWithCorrectOrientation(firstImagePath);
                _widthNumeric.Value = Math.Clamp(image.Width, (int)_widthNumeric.Minimum, (int)_widthNumeric.Maximum);
                _heightNumeric.Value = Math.Clamp(image.Height, (int)_heightNumeric.Minimum, (int)_heightNumeric.Maximum);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string? GetFirstImagePath() =>
            SelectedSource.Photos is { } photos
                ? VideoGenerationService.SortSourceFiles(photos, SelectedSortOrder).FirstOrDefault()?.OriginalFilePath
                : null;

        private void RunGeneratedVideo()
        {
            if (string.IsNullOrWhiteSpace(GeneratedOutputPath) || !File.Exists(GeneratedOutputPath))
            {
                MessageBox.Show(this, "Generated video file is not available.", MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _runButton.Enabled = false;
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(GeneratedOutputPath)
            {
                UseShellExecute = true
            });
        }

        private const string EmptyFolderText = "No photos in this folder";

        /// <summary>
        /// Where the photos of the video come from: the loaded ones (no folder), or the copies in a folder. Shown in the combo as its
        /// name and the number of its photos, "Liked (123)".
        /// </summary>
        private sealed class Source(string name, string? folder, string outputFolder, string emptyText)
        {
            public string? Folder { get; } = folder;

            /// <summary>Where the video is made while this is the source: the folder the photos are in.</summary>
            public string OutputFolder { get; } = outputFolder;

            public string EmptyText { get; } = emptyText;

            /// <summary>The photos, once they are known (the loaded ones from the start, a folder when it is listed).</summary>
            public List<PhotoFile>? Photos { get; set; }

            /// <summary>The folder is being listed or is listed; null if that was not started, or failed.</summary>
            public Task<List<PhotoFile>>? Listing { get; set; }

            /// <summary>The last attempt to list the folder failed.</summary>
            public bool Failed { get; set; }

            public override string ToString() =>
                $"{name} ({(Photos is { } photos ? photos.Count.ToString("N0") : Failed ? "?" : "...")})";
        }

        /// <summary>An item of a combo box: the value, shown as its text.</summary>
        private sealed class Choice<T>(T value, string text)
        {
            public T Value { get; } = value;

            public override string ToString() => text;
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(_outputFolderTextBox.Text))
            {
                MessageBox.Show(this, "Please select an output folder.", MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_outputFileNameTextBox.Text))
            {
                MessageBox.Show(this, "Please enter an output file name.", MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_outputFileNameTextBox.Text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "Output file name contains invalid characters.", MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_audioTextBox.Text) && !File.Exists(_audioTextBox.Text.Trim()))
            {
                MessageBox.Show(this, "The audio file is not there.", MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }
    }

    internal sealed record VideoGenerationOptions(
        string OutputFolder,
        string OutputFileName,
        string OutputPath,
        int WidthPixels,
        int HeightPixels,
        double StartFrameTimeSeconds,
        double EndFrameTimeSeconds,
        string VideoCodec,
        int Crf,
        string? AudioPath);
}
