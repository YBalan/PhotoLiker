namespace PhotoLikerUI
{
    internal sealed class VideoGenerationDialog : Form
    {
        private readonly Func<VideoGenerationOptions, VideoSequence, Action<string>, Action<int>, Task> _generateVideoAsync;
        private readonly string _likedFolder;
        private readonly IReadOnlyCollection<string> _extensions;

        private int _sourceGeneration;

        // The latest cover picture asked for wins: a preview that is decoded after the box changed is thrown away
        private int _coverGeneration;

        // The picture in the preview box; kept here to be disposed of, the box does not
        private Bitmap? _coverBitmap;

        // The random times of the photos, as positions in the range (see VideoGenerationService.DrawRandomSamples), drawn for the picked
        // source when "Use random" is ticked and kept until it is unticked or another source is picked: the length shown and the video use
        // these same times, and changing Start or End only moves them along.
        private double[]? _randomSamples;
        private Source? _randomSamplesSource;

        // Whether the cover is one of the photos of the source (which counts it once, as the cover), worked out again only when it could change
        private (Source? Source, int Count, string? Cover) _coverCheckKey;
        private bool _coverIsOnePhoto;

        // How long the audio is, once ffmpeg has told (null: no audio, not told yet, or not to be told); the latest file asked for wins
        private TimeSpan? _audioLength;
        private bool _readingAudioLength;
        private int _audioLengthGeneration;
        private CancellationTokenSource? _audioLengthCancellation;

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
        private readonly Label _audioLengthLabel = new();
        private readonly Label _videoLengthLabel = new();
        private readonly AudioPreview _audioPreview = new();
        private readonly TextBox _coverTextBox = new();
        private readonly NumericUpDown _coverSecondsNumeric = new();
        private readonly Button _coverClearButton = new();
        private readonly PictureBox _coverPreviewBox = new();
        private readonly Label _coverInfoLabel = new();
        private readonly NumericUpDown _widthNumeric = new();
        private readonly NumericUpDown _heightNumeric = new();
        private readonly NumericUpDown _frameTimeStartNumeric = new();
        private readonly NumericUpDown _frameTimeEndNumeric = new();
        private readonly CheckBox _randomFrameTimesCheckBox = new();
        private readonly ComboBox _playModeCombo = new();
        private readonly ComboBox _audioPlayModeCombo = new();
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
        /// <param name="generateVideoAsync">Makes the video from the pictures and their times (the cover first, when the options have one).</param>
        public VideoGenerationDialog(
            string defaultOutputFolder,
            IReadOnlyList<PhotoFile> currentPhotos,
            string likedFolder,
            IReadOnlyCollection<string> extensions,
            PhotoSortOrder defaultSortOrder,
            bool dark,
            Func<VideoGenerationOptions, VideoSequence, Action<string>, Action<int>, Task> generateVideoAsync)
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
            ClientSize = new Size(DialogWidth, 600); // the height is set to what the rows need, below

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 3,
                RowCount = 18
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ButtonColumnWidth));
            for (var row = 0; row < 16; row++)
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
                new(PhotoSortOrder.Random, "Random"),
                new(PhotoSortOrder.None, "As listed (no sorting)")
            ];
            _sortOrderCombo.Items.AddRange(sortOrders);
            _sortOrderCombo.SelectedIndex = Math.Max(0, Array.FindIndex(sortOrders, c => c.Value == defaultSortOrder));

            _outputFolderTextBox.Text = liked.OutputFolder;
            _outputFolderTextBox.Dock = DockStyle.Fill;
            _outputFileNameTextBox.Text = $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            _outputFileNameTextBox.Dock = DockStyle.Fill; // it spans the two columns, as wide as the folder box and its button together

            _audioTextBox.Dock = DockStyle.Fill;
            var audioBrowseButton = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            audioBrowseButton.Click += (_, _) => BrowseAudio();
            _audioPlayButton.Text = "Play audio";
            _audioStopButton.Text = "Stop";
            _audioClearButton.Text = "Clear";
            _audioStatusLabel.AutoSize = true;
            _audioStatusLabel.Margin = new Padding(8, 6, 0, 0);
            _audioLengthLabel.AutoSize = true;
            _audioLengthLabel.Anchor = AnchorStyles.Left;

            // Two lines always (how long it will be, and what becomes of the audio), so that the dialog does not move when the second comes and goes
            _videoLengthLabel.AutoSize = true;
            _videoLengthLabel.Dock = DockStyle.Fill;
            _videoLengthLabel.Margin = new Padding(0, 3, 0, 3);
            _videoLengthLabel.Text = "\n ";

            var toolTip = new ToolTip();
            toolTip.SetToolTip(_audioTextBox, AudioHint);
            toolTip.SetToolTip(audioBrowseButton, AudioHint);

            _coverTextBox.Dock = DockStyle.Fill;
            var coverBrowseButton = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            coverBrowseButton.Click += (_, _) => BrowseCover();
            toolTip.SetToolTip(_coverTextBox, CoverHint);
            toolTip.SetToolTip(coverBrowseButton, CoverHint);

            _coverSecondsNumeric.DecimalPlaces = 2;
            _coverSecondsNumeric.Increment = 0.5M;
            _coverSecondsNumeric.Minimum = 0.04M;
            _coverSecondsNumeric.Maximum = 60M;
            _coverSecondsNumeric.Value = 3.00M;
            _coverSecondsNumeric.Width = 70;
            _coverClearButton.Text = "Clear";

            _coverPreviewBox.Size = CoverPreviewSize;
            _coverPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
            _coverPreviewBox.BorderStyle = BorderStyle.FixedSingle;
            _coverPreviewBox.Margin = new Padding(0, 3, 12, 3);
            _coverPreviewBox.Paint += PaintCoverPlaceholder;
            _coverInfoLabel.AutoSize = true;
            _coverInfoLabel.MaximumSize = new Size(190, 0);

            _widthNumeric.Minimum = 320;
            _widthNumeric.Maximum = 7680;
            _widthNumeric.Value = 1920;

            _heightNumeric.Minimum = 240;
            _heightNumeric.Maximum = 4320;
            _heightNumeric.Value = 1080;

            // Narrower than usual, so that the box for random times fits in the row
            _frameTimeStartNumeric.DecimalPlaces = 2;
            _frameTimeStartNumeric.Increment = 0.05M;
            _frameTimeStartNumeric.Minimum = 0.04M;
            _frameTimeStartNumeric.Maximum = 60M;
            _frameTimeStartNumeric.Value = 0.50M;
            _frameTimeStartNumeric.Width = 70;

            _frameTimeEndNumeric.DecimalPlaces = 2;
            _frameTimeEndNumeric.Increment = 0.05M;
            _frameTimeEndNumeric.Minimum = 0.04M;
            _frameTimeEndNumeric.Maximum = 60M;
            _frameTimeEndNumeric.Value = 0.50M;
            _frameTimeEndNumeric.Width = 70;

            _randomFrameTimesCheckBox.Text = "Use random";
            _randomFrameTimesCheckBox.AutoSize = true;
            _randomFrameTimesCheckBox.Margin = new Padding(16, 4, 0, 0);
            toolTip.SetToolTip(_randomFrameTimesCheckBox, RandomFrameTimesHint);

            // The photos play once, or over and over until the audio ends; the audio plays over and over until the photos end, or once
            _playModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _playModeCombo.Width = 90;
            Choice<bool>[] playModes = [new(false, "Once"), new(true, "Repeat")];
            _playModeCombo.Items.AddRange(playModes);
            _playModeCombo.SelectedIndex = 0;
            toolTip.SetToolTip(_playModeCombo, PlayModeHint);

            _audioPlayModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _audioPlayModeCombo.Width = 90;
            Choice<bool>[] audioPlayModes = [new(true, "Repeat"), new(false, "Once")];
            _audioPlayModeCombo.Items.AddRange(audioPlayModes);
            _audioPlayModeCombo.SelectedIndex = 0;
            toolTip.SetToolTip(_audioPlayModeCombo, AudioPlayModeHint);

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
            layout.Controls.Add(_audioLengthLabel, 0, 5);
            layout.Controls.Add(audioPanel, 1, 5);
            layout.SetColumnSpan(audioPanel, 2);

            layout.Controls.Add(new Label { Text = "Cover (optional):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 6);
            layout.Controls.Add(_coverTextBox, 1, 6);
            layout.Controls.Add(coverBrowseButton, 2, 6);

            // The picture on the left; on its right how long it is shown, and the button to take it away
            var coverTimePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
            coverTimePanel.Controls.Add(new Label { Text = "Shown for (sec):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            coverTimePanel.Controls.Add(_coverSecondsNumeric);
            var coverSidePanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 0)
            };
            coverSidePanel.Controls.Add(coverTimePanel);
            coverSidePanel.Controls.Add(_coverClearButton);
            coverSidePanel.Controls.Add(_coverInfoLabel);
            var coverPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            coverPanel.Controls.Add(_coverPreviewBox);
            coverPanel.Controls.Add(coverSidePanel);
            layout.Controls.Add(coverPanel, 1, 7);
            layout.SetColumnSpan(coverPanel, 2);

            layout.Controls.Add(new Label { Text = "Resolution:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 8);
            var resolutionPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            resolutionPanel.Controls.Add(_widthNumeric);
            resolutionPanel.Controls.Add(new Label { Text = "x", AutoSize = true, Margin = new Padding(8, 6, 8, 0) });
            resolutionPanel.Controls.Add(_heightNumeric);
            layout.Controls.Add(resolutionPanel, 1, 8);

            // Not AutoSize: that only ever grows, and the first layout pass of the table measures it in a narrow cell (a button 96 px tall)
            _firstImageButton.Text = "Use first image";
            _firstImageButton.Dock = DockStyle.Fill;
            _firstImageButton.Click += (_, _) => ApplyFirstImageResolution();
            layout.Controls.Add(_firstImageButton, 2, 8);

            layout.Controls.Add(new Label { Text = "Frame time range (sec):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 9);
            var frameRangePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            frameRangePanel.Controls.Add(new Label { Text = "Start", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeStartNumeric);
            frameRangePanel.Controls.Add(new Label { Text = "End", AutoSize = true, Margin = new Padding(12, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeEndNumeric);
            frameRangePanel.Controls.Add(_randomFrameTimesCheckBox);
            layout.Controls.Add(frameRangePanel, 1, 9);
            layout.SetColumnSpan(frameRangePanel, 2);

            layout.Controls.Add(new Label { Text = "Repeat or once:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 10);
            var playPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            playPanel.Controls.Add(new Label { Text = "Photos", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            playPanel.Controls.Add(_playModeCombo);
            playPanel.Controls.Add(new Label { Text = "Audio", AutoSize = true, Margin = new Padding(16, 6, 6, 0) });
            playPanel.Controls.Add(_audioPlayModeCombo);
            layout.Controls.Add(playPanel, 1, 10);
            layout.SetColumnSpan(playPanel, 2);

            layout.Controls.Add(new Label { Text = "Video length:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 11);
            layout.Controls.Add(_videoLengthLabel, 1, 11);
            layout.SetColumnSpan(_videoLengthLabel, 2);

            layout.Controls.Add(new Label { Text = "Video codec:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 12);
            layout.Controls.Add(_codecCombo, 1, 12);
            layout.SetColumnSpan(_codecCombo, 2);

            layout.Controls.Add(new Label { Text = "Quality (CRF):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 13);
            layout.Controls.Add(_crfNumeric, 1, 13);
            layout.SetColumnSpan(_crfNumeric, 2);

            var hintLabel = new Label
            {
                Text = "Lower CRF = better quality and larger file size.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(hintLabel, 0, 14);
            layout.SetColumnSpan(hintLabel, 3);

            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Visible = false;
            layout.Controls.Add(_progressBar, 0, 15);
            layout.SetColumnSpan(_progressBar, 3);

            _outputLogTextBox.Multiline = true;
            _outputLogTextBox.ReadOnly = true;
            _outputLogTextBox.WordWrap = false;
            _outputLogTextBox.ScrollBars = ScrollBars.Both;
            _outputLogTextBox.Dock = DockStyle.Fill;
            _outputLogTextBox.Visible = false;
            layout.Controls.Add(_outputLogTextBox, 0, 16);
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

            layout.Controls.Add(buttonsPanel, 0, 17);
            layout.SetColumnSpan(buttonsPanel, 3);

            Controls.Add(layout);
            AcceptButton = _generateButton;
            CancelButton = _cancelButton;

            // As tall as the rows need (the log and the progress bar are not there until generating, which makes room for them)
            _collapsedHeight = layout.GetPreferredSize(new Size(DialogWidth, 0)).Height;
            ClientSize = new Size(DialogWidth, _collapsedHeight);

            _inputControls.AddRange([
                _sourceCombo,
                _sortOrderCombo,
                _outputFolderTextBox,
                _outputFileNameTextBox,
                _audioTextBox,
                audioBrowseButton,
                _coverTextBox,
                coverBrowseButton,
                _widthNumeric,
                _heightNumeric,
                _frameTimeStartNumeric,
                _frameTimeEndNumeric,
                _randomFrameTimesCheckBox,
                _codecCombo,
                _crfNumeric,
                browseButton
            ]);

            FormClosing += (_, e) =>
            {
                if (_isGenerating)
                    e.Cancel = true;
            };

            // Listening to the audio: a new file or a cleared box ends the one that plays, and its length is read again
            _audioTextBox.TextChanged += async (_, _) =>
            {
                _audioPreview.Stop();
                UpdateAudioButtons();
                await ShowAudioLengthAsync();
            };
            _audioPlayButton.Click += async (_, _) => await PlayAudioAsync();
            _audioStopButton.Click += (_, _) => _audioPreview.Stop();
            _audioClearButton.Click += (_, _) => _audioTextBox.Clear();
            _audioPreview.Changed += (_, _) => UpdateAudioButtons();
            Disposed += (_, _) =>
            {
                _audioPreview.Dispose();
                _audioLengthCancellation?.Cancel();
            };
            UpdateAudioButtons();

            // What the length of the video goes by
            _frameTimeStartNumeric.ValueChanged += (_, _) => UpdateVideoLength();
            _frameTimeEndNumeric.ValueChanged += (_, _) => UpdateVideoLength();
            _randomFrameTimesCheckBox.CheckedChanged += (_, _) =>
            {
                _randomSamples = null; // ticked or unticked, a new draw
                UpdateVideoLength();
            };
            _playModeCombo.SelectedIndexChanged += (_, _) => UpdateVideoLength();
            _audioPlayModeCombo.SelectedIndexChanged += (_, _) => UpdateVideoLength();
            UpdatePlayModeCombo();
            _coverSecondsNumeric.ValueChanged += (_, _) => UpdateVideoLength();

            // The cover: a new file or a cleared box brings its preview up to date
            _coverTextBox.TextChanged += async (_, _) => await ShowCoverAsync();
            _coverClearButton.Click += (_, _) => _coverTextBox.Clear();
            Disposed += (_, _) =>
            {
                _coverBitmap?.Dispose();
                _coverBitmap = null;
            };
            UpdateCoverControls();

            ThemeManager.Apply(this, dark);

            // The theme paints a label (and a check box) in the colour of a control; here they sit on the panels and show their colour.
            // The hint stays dimmed.
            foreach (var label in Flatten(layout).Where(control => control is Label or CheckBox))
                label.BackColor = Color.Transparent;
            hintLabel.ForeColor = SystemColors.GrayText;

            // An input shows the colour of an input (white in the light theme), so that an empty one is not taken for a disabled one
            var inputBack = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            foreach (var input in Flatten(layout).Where(control => control is TextBox or NumericUpDown or ComboBox))
                input.BackColor = inputBack;
            _coverPreviewBox.BackColor = inputBack;

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
                UpdateVideoLength();

                try
                {
                    await ListAsync(source);
                }
                catch (Exception ex)
                {
                    if (!IsDisposed && generation == _sourceGeneration)
                    {
                        _countLabel.Text = $"Cannot list the photos: {ex.Message}";
                        UpdateVideoLength();
                    }
                    return;
                }

                // Closed, or another source picked, while the folder was being listed
                if (IsDisposed || generation != _sourceGeneration)
                    return;
            }

            _countLabel.Text = source.Photos!.Count > 0 ? string.Empty : source.EmptyText;
            UpdateAvailability();
            UpdateVideoLength();
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

                // The cover is the first picture of the video, whatever the order of the photos. A photo that is the cover as well is
                // taken out of the photos, or it would be seen twice.
                var cover = options.CoverPath;
                if (cover is not null)
                {
                    var same = files.RemoveAll(file => IsSameFile(file, cover));
                    if (same > 0)
                        AppendOutput("The cover is one of the photos as well: it is shown first only.");
                    AppendOutput($"Cover: {cover} (shown for {options.CoverSeconds:0.##} sec)");
                }

                // ffmpeg is told not to turn the photos by their EXIF orientation (it gets it wrong for the photos next to a turned one),
                // so the ones that have to be turned are copied upright first, as the app shows them (the cover goes with them, first)
                AppendOutput("Looking for photos that have to be turned upright (EXIF orientation)...");
                IReadOnlyList<string> toTurn = cover is null ? files : [cover, .. files];
                using var upright = await UprightPhotos.CreateAsync(toTurn, options.WidthPixels, options.HeightPixels, SetProgress);
                AppendOutput(upright.Turned > 0
                    ? $"{Plural.Count(upright.Turned, "photo", "photos")} turned upright."
                    : "No photo has to be turned.");
                if (upright.Converted > 0)
                    AppendOutput($"{Plural.Count(upright.Converted, "picture", "pictures")} saved as JPEG: they are not all of one kind (JPEG, PNG...), which ffmpeg cannot join.");
                if (upright.Failed > 0)
                    AppendOutput($"{Plural.Count(upright.Failed, "picture", "pictures")} could not be turned or saved as JPEG and come out as stored.");
                SetProgress(0);

                // The times: the random ones are the ones drawn for the length that is shown, so the video is as long as it says
                var samples = options.RandomFrameTimes ? RandomSamples() : null;
                var repeat = options.RepeatToAudio && options.AudioPath is not null;
                var audioSeconds = repeat ? await KnownAudioSecondsAsync(options.AudioPath!) : null;
                if (repeat && audioSeconds is null)
                    AppendOutput("The length of the audio is not known, so the photos play once.");

                var uprightPhotos = cover is null ? upright.Files : [.. upright.Files.Skip(1)];
                var sequence = VideoGenerationService.BuildSequence(
                    uprightPhotos,
                    cover is null ? null : upright.Files[0],
                    options.CoverSeconds,
                    options.StartFrameTimeSeconds,
                    options.EndFrameTimeSeconds,
                    samples,
                    repeat,
                    options.RepeatAudio,
                    audioSeconds);

                if (samples is not null)
                {
                    AppendOutput(
                        $"Random time for each photo, {Math.Min(options.StartFrameTimeSeconds, options.EndFrameTimeSeconds):0.##} to " +
                        $"{Math.Max(options.StartFrameTimeSeconds, options.EndFrameTimeSeconds):0.##} sec.");
                }

                if (options.AudioPath is not null)
                {
                    AppendOutput(sequence.Audio switch
                    {
                        AudioPlayback.OnceEndsVideo =>
                            $"Audio: {options.AudioPath} (played once; the photos play {PassesText(sequence, options, samples, uprightPhotos.Count)} times, until it ends)",
                        AudioPlayback.OnceThenSilence =>
                            $"Audio: {options.AudioPath} (played once: silence after it if it is shorter than the video, cut where the video ends)",
                        _ =>
                            $"Audio: {options.AudioPath} (played over and over if it is shorter than the video, cut where the video ends)",
                    });
                }

                AppendOutput($"The video will be about {DurationText.Format(sequence.ExpectedSeconds)} long.");
                AppendOutput($"Starting ffmpeg: {options.OutputPath}");
                await _generateVideoAsync(options, sequence, AppendOutput, SetProgress);

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
                _randomFrameTimesCheckBox.Checked,
                _codecCombo.SelectedItem?.ToString() ?? "libx264",
                (int)_crfNumeric.Value,
                AudioPath,
                CoverPath,
                (double)_coverSecondsNumeric.Value,
                RepeatPhotosSelected,
                RepeatAudioSelected);
        }

        /// <summary>
        /// The length of the audio, from what was read when it was chosen, or (if that did not tell) read now; null if it cannot be told.
        /// </summary>
        private async Task<double?> KnownAudioSecondsAsync(string audioPath)
        {
            if (_audioLength is { } known)
                return known.TotalSeconds;

            try
            {
                return (await AudioDecoder.ReadDurationAsync(audioPath, CancellationToken.None))?.TotalSeconds;
            }
            catch (InvalidOperationException)
            {
                return null; // ffmpeg is not there: the video says so when it is made
            }
        }

        /// <summary>How many times the photos play when they repeat for the audio, as text ("3.4").</summary>
        private static string PassesText(VideoSequence sequence, VideoGenerationOptions options, double[]? samples, int photoCount)
        {
            var pass = VideoGenerationService.PhotoSeconds(photoCount, options.StartFrameTimeSeconds, options.EndFrameTimeSeconds, samples);
            var cover = options.CoverPath is null ? 0 : options.CoverSeconds;
            return pass > 0 ? TimesText((sequence.ExpectedSeconds - cover) / pass) : "0";
        }

        /// <summary>True: the photos play over and over until the audio ends (when it is longer); false: once.</summary>
        private bool RepeatPhotosSelected => ((Choice<bool>)_playModeCombo.SelectedItem!).Value;

        /// <summary>True: the audio plays over and over until the photos end (when it is shorter); false: once, and silence after it.</summary>
        private bool RepeatAudioSelected => ((Choice<bool>)_audioPlayModeCombo.SelectedItem!).Value;

        /// <summary>The cover picture, or null for none.</summary>
        private string? CoverPath => string.IsNullOrWhiteSpace(_coverTextBox.Text) ? null : _coverTextBox.Text.Trim();

        private static bool IsSameFile(string path, string other)
        {
            try
            {
                return string.Equals(Path.GetFullPath(path), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
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
            ClientSize = new Size(840, _collapsedHeight + LogHeight);
        }

        private void SetBusyState(bool busy)
        {
            _isGenerating = busy;
            foreach (var control in _inputControls)
                control.Enabled = !busy;

            UpdateAvailability();
            UpdateAudioButtons();
            UpdateCoverControls();
            UpdatePlayModeCombo();
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

        /// <summary>The audio file, or null for none.</summary>
        private string? AudioPath => string.IsNullOrWhiteSpace(_audioTextBox.Text) ? null : _audioTextBox.Text.Trim();

        /// <summary>
        /// Shows how long the audio is, as ffmpeg reads it (ffmpeg is started for the file only when it is there, so that a path that is
        /// being typed costs nothing). Only the latest file asked for is shown; the one before it is stopped.
        /// </summary>
        private async Task ShowAudioLengthAsync()
        {
            _audioLengthCancellation?.Cancel();
            var generation = ++_audioLengthGeneration;
            var path = AudioPath;

            _audioLength = null;
            _readingAudioLength = path is not null;
            _audioLengthLabel.Text = path is null ? string.Empty : "Length: ...";
            UpdatePlayModeCombo();
            UpdateVideoLength();
            if (path is null)
                return;

            var cancellation = _audioLengthCancellation = new CancellationTokenSource();
            string text;
            TimeSpan? length = null;
            try
            {
                if (!await Task.Run(() => File.Exists(path), cancellation.Token))
                {
                    text = "File not found";
                }
                else
                {
                    length = await AudioDecoder.ReadDurationAsync(path, cancellation.Token);
                    text = length is { } read ? $"Length: {DurationText.Format(read.TotalSeconds)}" : "Length: unknown";
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // ffmpeg is not there, say: the video cannot be made either, and says so when it is tried
                text = "Length: unknown";
            }

            // Closed, or another file asked for, while this one was read
            if (IsDisposed || generation != _audioLengthGeneration)
                return;

            _audioLength = length;
            _readingAudioLength = false;
            _audioLengthLabel.Text = text;
            UpdateVideoLength();
        }

        /// <summary>
        /// Says how long the video will be with the settings as they are (the photos of the picked source, the time of each, the cover,
        /// whether the photos repeat for the audio), and what becomes of the audio. With random times it is the sum of the times drawn,
        /// which are the ones the video gets. A second line is always there, even if blank, so that the dialog keeps its size.
        /// </summary>
        private void UpdateVideoLength()
        {
            if (IsDisposed || Disposing)
                return;

            var line = " ";
            string text;
            if (SelectedSource.Photos is not { } photos)
            {
                text = SelectedSource.Failed ? "-" : "Counting the photos...";
            }
            else
            {
                var cover = CoverPath;

                // A photo that is the cover as well is shown once, as the cover
                var photoCount = photos.Count - (CoverIsOnePhoto(photos, cover) ? 1 : 0);
                if (photoCount <= 0 && cover is null)
                {
                    text = "-";
                }
                else
                {
                    var start = (double)_frameTimeStartNumeric.Value;
                    var end = (double)_frameTimeEndNumeric.Value;
                    double? coverSeconds = cover is null ? null : (double)_coverSecondsNumeric.Value;
                    var samples = RandomSamples();
                    var repeat = RepeatPhotosSelected;

                    var length = VideoGenerationService.EstimateLength(photoCount, start, end, samples, coverSeconds, repeat, _audioLength?.TotalSeconds);
                    text = $"About {DurationText.Format(length)}";
                    line = AudioLine(photoCount, start, end, samples, coverSeconds, repeat);
                }
            }

            _videoLengthLabel.Text = text + "\n" + line;
        }

        /// <summary>The second line under the video length: what becomes of the audio (or why "Repeat" is not doing anything).</summary>
        private string AudioLine(int photoCount, double start, double end, double[]? samples, double? coverSeconds, bool repeat)
        {
            if (AudioPath is null)
                return repeat ? "Repeat needs an audio file: the photos play once." : " ";

            if (_audioLength is not { } audio)
            {
                // Still being read, or not to be told
                return repeat && !_readingAudioLength ? "The length of the audio is not known: the photos play once." : " ";
            }

            var photoPass = VideoGenerationService.PhotoSeconds(photoCount, start, end, samples);
            var cover = coverSeconds ?? 0;
            var once = cover + photoPass;
            if (repeat && photoPass > 0 && audio.TotalSeconds > once)
                return $"The photos play {TimesText((audio.TotalSeconds - cover) / photoPass)} times, until the audio ends.";

            return VideoGenerationService.FitOfAudio(once, audio.TotalSeconds) switch
            {
                AudioFit.Cut => "The audio is cut where the video ends.",
                AudioFit.Repeated => RepeatAudioSelected ? "The audio is played over and over." : "The audio is played once, then the video is silent.",
                _ => "The audio is about as long as the video.",
            };
        }

        private static string TimesText(double times) => times.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The random times of the photos of the picked source, as positions in the range, or null when "Use random" is not ticked. They
        /// are drawn when there are none yet or they were drawn for another source (picking another one, or ticking the box, draws new
        /// ones), and kept, so that the length shown and the video go by the same times.
        /// </summary>
        private double[]? RandomSamples()
        {
            if (!_randomFrameTimesCheckBox.Checked)
            {
                _randomSamples = null;
                return null;
            }

            if (SelectedSource.Photos is not { } photos)
                return null;

            if (_randomSamples is null || !ReferenceEquals(_randomSamplesSource, SelectedSource) || _randomSamples.Length < photos.Count)
            {
                _randomSamples = VideoGenerationService.DrawRandomSamples(photos.Count);
                _randomSamplesSource = SelectedSource;
            }

            return _randomSamples;
        }

        /// <summary>
        /// Whether the cover is one of the photos (they are listed as full paths). Looked at again only when the source or the cover
        /// changes, not at every change of a setting. Generating takes such a photo out for good, whatever this says.
        /// </summary>
        private bool CoverIsOnePhoto(List<PhotoFile> photos, string? cover)
        {
            if (cover is null)
                return false;

            var key = (Source: (Source?)SelectedSource, photos.Count, Cover: (string?)cover);
            if (key != _coverCheckKey)
            {
                string? coverFull;
                try
                {
                    coverFull = Path.GetFullPath(cover);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    coverFull = null; // not a path (yet, when it is being typed)
                }

                _coverIsOnePhoto = coverFull is not null && photos.Any(photo => string.Equals(photo.OriginalFilePath, coverFull, StringComparison.OrdinalIgnoreCase));
                _coverCheckKey = key;
            }

            return _coverIsOnePhoto;
        }

        /// <summary>The repeat or once boxes are about the audio and the photos going together: they are off without an audio file, and while generating.</summary>
        private void UpdatePlayModeCombo()
        {
            if (IsDisposed || Disposing)
                return;

            var enabled = !_isGenerating && AudioPath is not null;
            _playModeCombo.Enabled = enabled;
            _audioPlayModeCombo.Enabled = enabled;
        }

        private const int DialogWidth = 534;

        /// <summary>Wide enough for "Use first image".</summary>
        private const int ButtonColumnWidth = 104;

        /// <summary>The height that makes room for the log and the progress bar, on top of the one of the rows.</summary>
        private const int LogHeight = 150;

        private int _collapsedHeight;

        private static readonly Size CoverPreviewSize = new(160, 90);

        private const string RandomFrameTimesHint =
            "Each photo is shown for a time of its own, picked at random between Start and End (whichever is the smaller), instead of going from Start to End in steps. The cover keeps its own time. The times are drawn when you tick this and when you pick another folder, and the video length shown is the length of the video.";

        private const string PlayModeHint =
            "Photos. Once: they play once. Repeat: when the audio is longer than they are, they play over and over until it ends (instead of the audio being cut).";

        private const string AudioPlayModeHint =
            "Audio. Repeat: when it is shorter than the video, it is played over and over until the video ends. Once: it is played once, and the rest of the video is silent. A longer audio is cut where the video ends (unless the photos are set to repeat).";

        private const string CoverHint =
            "A picture that opens the video, before the photos, whatever their sort order. It is shown for the time set here, and not as part of the frame time range.";

        private void BrowseCover()
        {
            var extensions = _extensions.Select(extension => extension.StartsWith('.') ? "*" + extension : "*." + extension).ToList();
            using var dialog = new OpenFileDialog
            {
                Title = "Choose the cover image of the video",
                Filter = (extensions.Count > 0 ? $"Pictures|{string.Join(';', extensions)}|" : string.Empty) + "All files|*.*",
            };

            var current = CoverPath;
            if (current is not null && Path.GetDirectoryName(current) is { Length: > 0 } folder && Directory.Exists(folder))
            {
                dialog.InitialDirectory = folder;
                dialog.FileName = Path.GetFileName(current);
            }
            else if (SelectedSource.Folder is { } sourceFolder && Directory.Exists(sourceFolder))
            {
                dialog.InitialDirectory = sourceFolder;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
                _coverTextBox.Text = dialog.FileName;
        }

        /// <summary>
        /// Shows the picture in the cover box, small and upright. It is decoded on another thread (a photo is big, and may be on a slow
        /// disk); only the latest one asked for is shown.
        /// </summary>
        private async Task ShowCoverAsync()
        {
            var path = CoverPath;
            var generation = ++_coverGeneration;

            SetCoverPreview(null);
            _coverInfoLabel.Text = string.Empty;
            UpdateCoverControls();
            UpdateVideoLength();
            if (path is null)
                return;

            // Twice the box, so that it stays sharp on a screen with a larger scale
            var maxSize = new Size(CoverPreviewSize.Width * 2, CoverPreviewSize.Height * 2);
            try
            {
                var (preview, fullSize) = await Task.Run(() => ImageHelper.LoadPreview(path, maxSize));

                // Closed, or another file asked for, while this one was decoded
                if (IsDisposed || generation != _coverGeneration)
                {
                    preview.Dispose();
                    return;
                }

                SetCoverPreview(preview);
                _coverInfoLabel.Text = $"{fullSize.Width} x {fullSize.Height} px";
            }
            catch (Exception ex)
            {
                // Nobody waits for this (it is an event handler), so a failed preview is told here and not thrown
                if (!IsDisposed && generation == _coverGeneration)
                {
                    _coverInfoLabel.Text = ex is FileNotFoundException or DirectoryNotFoundException
                        ? "This file is not there."
                        : "No preview for this file.";
                }
            }
        }

        private void SetCoverPreview(Bitmap? preview)
        {
            _coverPreviewBox.Image = preview;
            _coverBitmap?.Dispose();
            _coverBitmap = preview;
        }

        /// <summary>The time and Clear need a cover, and nothing is touched while generating.</summary>
        private void UpdateCoverControls()
        {
            if (IsDisposed || Disposing)
                return;

            var hasCover = CoverPath is not null;
            _coverSecondsNumeric.Enabled = !_isGenerating && hasCover;
            _coverClearButton.Enabled = !_isGenerating && hasCover;
        }

        /// <summary>An empty box says what it is for.</summary>
        private void PaintCoverPlaceholder(object? sender, PaintEventArgs e)
        {
            if (_coverPreviewBox.Image is not null)
                return;

            TextRenderer.DrawText(e.Graphics, "No cover image", Font, _coverPreviewBox.ClientRectangle, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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

            if (CoverPath is { } cover && !File.Exists(cover))
            {
                MessageBox.Show(this, "The cover image is not there.", MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        bool RandomFrameTimes,
        string VideoCodec,
        int Crf,
        string? AudioPath,
        string? CoverPath,
        double CoverSeconds,
        bool RepeatToAudio,
        bool RepeatAudio);
}
