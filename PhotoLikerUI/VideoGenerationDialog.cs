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

        // The experimental cut on the beat. The rhythm is found when the box is ticked (not before: it takes a few seconds and a file read) for
        // the audio that is in the box; _rhythmFound is what the analysis gave, _rhythm that with the ÷2 and ×2 pressed (_tempoOctaves) put
        // on it. The latest audio asked for wins, as with its length.
        private RhythmAnalysis? _rhythmFound;
        private RhythmAnalysis? _rhythm;
        private int _tempoOctaves;
        private int _wantedTempoOctaves; // from the saved settings: put on the rhythm when it has been found
        private bool _analyzingRhythm;
        private string? _rhythmError;
        private int _rhythmGeneration;
        private CancellationTokenSource? _rhythmCancellation;
        private Task _rhythmTask = Task.CompletedTask;
        private RhythmCacheEntry? _rhythmCache;
        private ToolTip? _beatToolTip;

        // The latest folder asked for wins: settings read for the folder picked before it are thrown away
        private int _settingsGeneration;

        // While the photos are cut on the beat, Use random and Photos: Repeat do not apply: they are put off, and back to what they were after
        private bool _beatLocked;
        private bool _randomBeforeBeats;
        private bool _repeatPhotosBeforeBeats;

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
        private readonly NumericUpDown _coverStartSecondsNumeric = new();
        private readonly CheckBox _coverAtStartCheckBox = new();
        private readonly CheckBox _coverAtEndCheckBox = new();
        private readonly NumericUpDown _coverEndSecondsNumeric = new();
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
        private readonly CheckBox _beatCheckBox = new();
        private readonly Label _beatStatusLabel = new();
        private readonly Button _slowerBeatButton = new();
        private readonly Button _fasterBeatButton = new();
        private readonly RhythmView _rhythmView = new();
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
            var current = new Source(VideoStrings.SourceCurrent, null, defaultOutputFolder, VideoStrings.NoPhotosLoaded) { Photos = [.. currentPhotos] };
            var liked = new Source(VideoStrings.SourceLiked, likedFolder, likedFolder, VideoStrings.EmptyFolder);
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
            MeasureColumns();
            ClientSize = new Size(_dialogWidth, 600); // the height is set to what the rows need, below

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 3,
                RowCount = 20
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _labelColumnWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _buttonColumnWidth));
            for (var row = 0; row < 18; row++)
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
                new(PhotoSortOrder.OldestFirst, VideoStrings.SortOldestFirst),
                new(PhotoSortOrder.NewestFirst, VideoStrings.SortNewestFirst),
                new(PhotoSortOrder.ByName, VideoStrings.SortByName),
                new(PhotoSortOrder.Random, VideoStrings.SortRandom),
                new(PhotoSortOrder.None, VideoStrings.SortNone)
            ];
            _sortOrderCombo.Items.AddRange(sortOrders);
            _sortOrderCombo.SelectedIndex = Math.Max(0, Array.FindIndex(sortOrders, c => c.Value == defaultSortOrder));

            _outputFolderTextBox.Text = liked.OutputFolder;
            _outputFolderTextBox.Dock = DockStyle.Fill;
            _outputFileNameTextBox.Text = $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            _outputFileNameTextBox.Dock = DockStyle.Fill; // it spans the two columns, as wide as the folder box and its button together

            _audioTextBox.Dock = DockStyle.Fill;
            var audioBrowseButton = new Button { Text = VideoStrings.Browse, Dock = DockStyle.Fill };
            audioBrowseButton.Click += (_, _) => BrowseAudio();
            _audioPlayButton.Text = VideoStrings.PlayAudio;
            _audioStopButton.Text = VideoStrings.Stop;
            _audioClearButton.Text = VideoStrings.Clear;
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
            toolTip.SetToolTip(_audioTextBox, VideoStrings.AudioHint);
            toolTip.SetToolTip(audioBrowseButton, VideoStrings.AudioHint);

            _coverTextBox.Dock = DockStyle.Fill;
            var coverBrowseButton = new Button { Text = VideoStrings.Browse, Dock = DockStyle.Fill };
            coverBrowseButton.Click += (_, _) => BrowseCover();
            toolTip.SetToolTip(_coverTextBox, VideoStrings.CoverHint);
            toolTip.SetToolTip(coverBrowseButton, VideoStrings.CoverHint);

            // The cover is shown before the photos and/or after them, each for a time of its own; a tick and a time for each, one under the other
            _coverAtStartCheckBox.Text = VideoStrings.ShowAtStart;
            _coverAtStartCheckBox.Checked = true;
            _coverAtEndCheckBox.Text = VideoStrings.ShowAtEnd;
            foreach (var checkBox in new[] { _coverAtStartCheckBox, _coverAtEndCheckBox })
            {
                checkBox.AutoSize = true;
                checkBox.Anchor = AnchorStyles.Left;
                checkBox.Margin = new Padding(0, 0, CoverSideGap, 0);
            }

            foreach (var numeric in new[] { _coverStartSecondsNumeric, _coverEndSecondsNumeric })
            {
                numeric.DecimalPlaces = 2;
                numeric.Increment = 0.5M;
                numeric.Minimum = 0.04M;
                numeric.Maximum = 60M;
                numeric.Value = 3.00M;
                numeric.Width = CoverTimeBoxWidth;
                numeric.Anchor = AnchorStyles.Left;
                numeric.Margin = new Padding(0, 0, CoverSideGap, 0);
            }

            toolTip.SetToolTip(_coverAtStartCheckBox, VideoStrings.CoverAtStartHint);
            toolTip.SetToolTip(_coverStartSecondsNumeric, VideoStrings.CoverAtStartHint);
            toolTip.SetToolTip(_coverAtEndCheckBox, VideoStrings.CoverAtEndHint);
            toolTip.SetToolTip(_coverEndSecondsNumeric, VideoStrings.CoverAtEndHint);
            _coverClearButton.Text = VideoStrings.Clear;
            _coverClearButton.Margin = new Padding(3, 1, 3, 1); // the rows beside the preview have to fit its height, or the dialog grows

            _coverPreviewBox.Size = CoverPreviewSize;
            _coverPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
            _coverPreviewBox.BorderStyle = BorderStyle.FixedSingle;
            _coverPreviewBox.Margin = new Padding(0, 3, CoverPreviewGap, 3);
            _coverPreviewBox.Paint += PaintCoverPlaceholder;
            _coverInfoLabel.AutoSize = true;
            _coverInfoLabel.MaximumSize = new Size(190, 0);
            _coverInfoLabel.Margin = new Padding(3, 1, 3, 0);
            _coverInfoLabel.Text = " "; // never empty: an empty label is shorter than one with text, and the cover row (which is as tall as the rows beside the preview) would move

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

            _randomFrameTimesCheckBox.Text = VideoStrings.UseRandom;
            _randomFrameTimesCheckBox.AutoSize = true;
            _randomFrameTimesCheckBox.Margin = new Padding(16, 4, 0, 0);
            toolTip.SetToolTip(_randomFrameTimesCheckBox, VideoStrings.RandomFrameTimesHint);

            // The photos play once, or over and over until the audio ends; the audio plays over and over until the photos end, or once
            _playModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _playModeCombo.Width = 90;
            Choice<bool>[] playModes = [new(false, VideoStrings.Once), new(true, VideoStrings.Repeat)];
            _playModeCombo.Items.AddRange(playModes);
            _playModeCombo.SelectedIndex = 0;
            toolTip.SetToolTip(_playModeCombo, VideoStrings.PlayModeHint);

            _audioPlayModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _audioPlayModeCombo.Width = 90;
            Choice<bool>[] audioPlayModes = [new(true, VideoStrings.Repeat), new(false, VideoStrings.Once)];
            _audioPlayModeCombo.Items.AddRange(audioPlayModes);
            _audioPlayModeCombo.SelectedIndex = 0;
            toolTip.SetToolTip(_audioPlayModeCombo, VideoStrings.AudioPlayModeHint);

            _beatCheckBox.Text = VideoStrings.CutOnBeat;
            _beatCheckBox.AutoSize = true;
            _beatStatusLabel.AutoSize = true;
            _beatStatusLabel.Margin = new Padding(8, 6, 0, 0);
            _slowerBeatButton.Text = "÷2";
            _fasterBeatButton.Text = "×2";
            foreach (var button in new[] { _slowerBeatButton, _fasterBeatButton })
                button.Size = new Size(40, 25);
            toolTip.SetToolTip(_beatCheckBox, VideoStrings.CutOnBeatHint);
            toolTip.SetToolTip(_beatStatusLabel, VideoStrings.CutOnBeatHint);
            toolTip.SetToolTip(_slowerBeatButton, VideoStrings.RhythmSlowerHint);
            toolTip.SetToolTip(_fasterBeatButton, VideoStrings.RhythmFasterHint);
            toolTip.SetToolTip(_rhythmView, VideoStrings.RhythmViewHint);
            _beatToolTip = toolTip;

            _codecCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _codecCombo.Items.AddRange(["libx264", "libx265"]);
            _codecCombo.SelectedIndex = 0;

            _crfNumeric.Minimum = 0;
            _crfNumeric.Maximum = 51;
            _crfNumeric.Value = 23;

            var browseButton = new Button { Text = VideoStrings.Browse, Dock = DockStyle.Fill };
            browseButton.Click += (_, _) => BrowseOutputFolder();

            layout.Controls.Add(new Label { Text = VideoStrings.LabelPhotosFrom, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            // No margin of its own: the combo has its own, and a second one would put it 3 px right of the sort order combo below
            var sourcePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            sourcePanel.Controls.Add(_sourceCombo);
            sourcePanel.Controls.Add(_countLabel);
            layout.Controls.Add(sourcePanel, 1, 0);
            layout.SetColumnSpan(sourcePanel, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelSortOrder, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(_sortOrderCombo, 1, 1);
            layout.SetColumnSpan(_sortOrderCombo, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelOutputFolder, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            layout.Controls.Add(_outputFolderTextBox, 1, 2);
            layout.Controls.Add(browseButton, 2, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelOutputFileName, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            layout.Controls.Add(_outputFileNameTextBox, 1, 3);
            layout.SetColumnSpan(_outputFileNameTextBox, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelAudio, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
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

            // Experimental: cut the photos on the beat of the audio. A tick, what was found, and two buttons for when the tempo was found
            // as half or double of the beat. The bars of the rhythm are in a row of their own, under the hint (they open the dialog up).
            layout.Controls.Add(new Label { Text = VideoStrings.LabelRhythm, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 6);
            var beatPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            beatPanel.Controls.Add(_beatCheckBox);
            beatPanel.Controls.Add(_beatStatusLabel);
            layout.Controls.Add(beatPanel, 1, 6);
            var beatTempoPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            beatTempoPanel.Controls.Add(_slowerBeatButton);
            beatTempoPanel.Controls.Add(_fasterBeatButton);
            layout.Controls.Add(beatTempoPanel, 2, 6);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelCover, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
            layout.Controls.Add(_coverTextBox, 1, 7);
            layout.Controls.Add(coverBrowseButton, 2, 7);

            // The picture on the left; on its right when it is shown (at the start, at the end) and for how long, and the button to take it away.
            // A table, so that the boxes and the "(sec)" of the two rows stand one under the other.
            var coverTimesTable = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 1, 0, 1) };
            for (var column = 0; column < 3; column++)
                coverTimesTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            coverTimesTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            coverTimesTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            coverTimesTable.Controls.Add(_coverAtStartCheckBox, 0, 0);
            coverTimesTable.Controls.Add(_coverStartSecondsNumeric, 1, 0);
            coverTimesTable.Controls.Add(new Label { Text = VideoStrings.LabelSec, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) }, 2, 0);
            coverTimesTable.Controls.Add(_coverAtEndCheckBox, 0, 1);
            coverTimesTable.Controls.Add(_coverEndSecondsNumeric, 1, 1);
            coverTimesTable.Controls.Add(new Label { Text = VideoStrings.LabelSec, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) }, 2, 1);
            var coverSidePanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 0)
            };
            coverSidePanel.Controls.Add(coverTimesTable);
            coverSidePanel.Controls.Add(_coverClearButton);
            coverSidePanel.Controls.Add(_coverInfoLabel);
            var coverPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            coverPanel.Controls.Add(_coverPreviewBox);
            coverPanel.Controls.Add(coverSidePanel);
            layout.Controls.Add(coverPanel, 1, 8);
            layout.SetColumnSpan(coverPanel, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelResolution, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 9);
            var resolutionPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            resolutionPanel.Controls.Add(_widthNumeric);
            resolutionPanel.Controls.Add(new Label { Text = "x", AutoSize = true, Margin = new Padding(8, 6, 8, 0) });
            resolutionPanel.Controls.Add(_heightNumeric);
            layout.Controls.Add(resolutionPanel, 1, 9);

            // Not AutoSize: that only ever grows, and the first layout pass of the table measures it in a narrow cell (a button 96 px tall)
            _firstImageButton.Text = VideoStrings.UseFirstImage;
            _firstImageButton.Dock = DockStyle.Fill;
            _firstImageButton.Click += (_, _) => ApplyFirstImageResolution();
            layout.Controls.Add(_firstImageButton, 2, 9);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelFrameTimeRange, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 10);
            var frameRangePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            frameRangePanel.Controls.Add(new Label { Text = VideoStrings.LabelStart, AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeStartNumeric);
            frameRangePanel.Controls.Add(new Label { Text = VideoStrings.LabelEnd, AutoSize = true, Margin = new Padding(12, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeEndNumeric);
            frameRangePanel.Controls.Add(_randomFrameTimesCheckBox);
            layout.Controls.Add(frameRangePanel, 1, 10);
            layout.SetColumnSpan(frameRangePanel, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelRepeatOrOnce, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 11);
            var playPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            playPanel.Controls.Add(new Label { Text = VideoStrings.LabelPhotos, AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            playPanel.Controls.Add(_playModeCombo);
            playPanel.Controls.Add(new Label { Text = VideoStrings.LabelAudioShort, AutoSize = true, Margin = new Padding(16, 6, 6, 0) });
            playPanel.Controls.Add(_audioPlayModeCombo);
            layout.Controls.Add(playPanel, 1, 11);
            layout.SetColumnSpan(playPanel, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelVideoLength, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 12);
            layout.Controls.Add(_videoLengthLabel, 1, 12);
            layout.SetColumnSpan(_videoLengthLabel, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelCodec, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 13);
            layout.Controls.Add(_codecCombo, 1, 13);
            layout.SetColumnSpan(_codecCombo, 2);

            layout.Controls.Add(new Label { Text = VideoStrings.LabelQuality, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 14);
            layout.Controls.Add(_crfNumeric, 1, 14);
            layout.SetColumnSpan(_crfNumeric, 2);

            var hintLabel = new Label
            {
                Text = VideoStrings.QualityHint,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(hintLabel, 0, 15);
            layout.SetColumnSpan(hintLabel, 3);

            _rhythmView.Dock = DockStyle.Fill;
            _rhythmView.Height = RhythmViewHeight;
            _rhythmView.Margin = new Padding(0, 6, 0, 0);
            _rhythmView.Visible = false;
            layout.Controls.Add(_rhythmView, 0, 16);
            layout.SetColumnSpan(_rhythmView, 3);

            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Visible = false;
            TaskbarProgress.Watch(_progressBar, () => _isGenerating); // the bar stays on the dialog after the video is made
            layout.Controls.Add(_progressBar, 0, 17);
            layout.SetColumnSpan(_progressBar, 3);

            _outputLogTextBox.Multiline = true;
            _outputLogTextBox.ReadOnly = true;
            _outputLogTextBox.WordWrap = false;
            _outputLogTextBox.ScrollBars = ScrollBars.Both;
            _outputLogTextBox.Dock = DockStyle.Fill;
            _outputLogTextBox.Visible = false;
            layout.Controls.Add(_outputLogTextBox, 0, 18);
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

            _generateButton.Text = VideoStrings.GenerateButton;
            _generateButton.AutoSize = true;
            _generateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _generateButton.Click += async (_, _) => await GenerateAsync();

            _runButton.Text = VideoStrings.PlayButton;
            _runButton.AutoSize = true;
            _runButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _runButton.Enabled = false;
            _runButton.Visible = false;
            _runButton.Click += (_, _) => RunGeneratedVideo();

            _cancelButton.Text = VideoStrings.CancelButton;
            _cancelButton.AutoSize = true;
            _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelButton.DialogResult = DialogResult.Cancel;

            buttonsPanel.Controls.Add(_generateButton);
            buttonsPanel.Controls.Add(_runButton);
            buttonsPanel.Controls.Add(_cancelButton);

            layout.Controls.Add(buttonsPanel, 0, 19);
            layout.SetColumnSpan(buttonsPanel, 3);

            Controls.Add(layout);
            AcceptButton = _generateButton;
            CancelButton = _cancelButton;

            // As tall as the rows need (the log and the progress bar are not there until generating, which makes room for them)
            _collapsedHeight = layout.GetPreferredSize(new Size(_dialogWidth, 0)).Height;
            ClientSize = new Size(_dialogWidth, _collapsedHeight);

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
                StartRhythmAnalysis();
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
                _rhythmCancellation?.Cancel();
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

            // The experimental cut on the beat: ticking it finds the rhythm of the audio
            _beatCheckBox.CheckedChanged += (_, _) => StartRhythmAnalysis();
            _slowerBeatButton.Click += (_, _) => ChangeTempo(-1);
            _fasterBeatButton.Click += (_, _) => ChangeTempo(1);
            UpdateRhythmUi();
            _coverStartSecondsNumeric.ValueChanged += (_, _) => UpdateVideoLength();
            _coverEndSecondsNumeric.ValueChanged += (_, _) => UpdateVideoLength();
            foreach (var checkBox in new[] { _coverAtStartCheckBox, _coverAtEndCheckBox })
            {
                checkBox.CheckedChanged += (_, _) =>
                {
                    UpdateCoverControls();
                    UpdateVideoLength();
                };
            }

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
            _rhythmView.BackColor = inputBack;
            _rhythmView.TextColor = dark ? Color.FromArgb(160, 160, 160) : SystemColors.GrayText;

            // The loaded photos are known at once; the folders are listed in the background and counted one by one
            _sourceCombo.SelectedIndexChanged += async (_, _) =>
            {
                if (_refreshingSourceItem)
                    return;

                FollowOutputFolder();
                _ = LoadSettingsAsync(); // the settings the last video of this folder was made with, if there are any
                await ShowSourceAsync();
            };
            _ = ShowSourceAsync();
            _ = LoadSettingsAsync();
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
                _countLabel.Text = VideoStrings.Counting;
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
                        _countLabel.Text = string.Format(VideoStrings.CannotListFormat, ex.Message);
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

            var sources = groups.Select(group => new Source(Path.GetFileName(group.Path), group.Path, group.Path, VideoStrings.EmptyFolder)).ToList();
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

            // Kept with the photos, for the next video of this folder (the values as they are now, before generating changes anything)
            var settings = CurrentSettings();
            var settingsFolder = SelectedSource.OutputFolder;

            _audioPreview.Stop();
            ExpandForProgress();
            SetBusyState(true);
            SetProgress(0);

            try
            {
                var (saved, saveError) = await Task.Run(() => (VideoSettingsFile.TrySave(settingsFolder, settings, out var why), why));
                AppendOutput(saved
                    ? string.Format(VideoStrings.LogSettingsSavedFormat, VideoSettingsFile.GetPath(settingsFolder))
                    : string.Format(VideoStrings.LogSettingsNotSavedFormat, saveError));

                // Which photos are still there is found out here and not when the dialog opens, which would keep it from opening on a slow disk
                AppendOutput(string.Format(VideoStrings.LogCheckingFormat, Plural.Count(photos.Count, VideoStrings.Photo)));
                var (files, missing) = await Task.Run(() => FindFiles(photos, sortOrder));
                if (missing > 0)
                    AppendOutput(string.Format(VideoStrings.LogMissingFormat, Plural.Count(missing, VideoStrings.MissingPhoto)));
                if (files.Count == 0)
                    throw new FileNotFoundException(VideoStrings.NoneThere);

                // The cover is the first picture of the video (if it is shown at the start) and the last (if at the end), whatever the order
                // of the photos. A photo that is the cover as well is taken out of the photos, or it would be seen twice.
                var cover = options.CoverPath;
                if (cover is not null)
                {
                    var same = files.RemoveAll(file => IsSameFile(file, cover));
                    if (same > 0)
                        AppendOutput(VideoStrings.LogCoverIsPhoto);
                    AppendOutput(options.CoverSeconds is { } coverStartSeconds
                        ? string.Format(VideoStrings.LogCoverFormat, cover, coverStartSeconds)
                        : string.Format(VideoStrings.LogCoverNotAtStartFormat, cover));
                    if (options.CoverEndSeconds is { } coverEndSeconds)
                        AppendOutput(string.Format(VideoStrings.LogCoverEndFormat, coverEndSeconds));
                }
                else if (CoverPath is not null)
                {
                    AppendOutput(VideoStrings.LogCoverUnused);
                }

                // ffmpeg is told not to turn the photos by their EXIF orientation (it gets it wrong for the photos next to a turned one),
                // so the ones that have to be turned are copied upright first, as the app shows them (the cover goes with them, first)
                AppendOutput(VideoStrings.LogLookingUpright);
                IReadOnlyList<string> toTurn = cover is null ? files : [cover, .. files];
                using var upright = await UprightPhotos.CreateAsync(toTurn, options.WidthPixels, options.HeightPixels, SetProgress);
                AppendOutput(upright.Turned > 0
                    ? string.Format(VideoStrings.LogTurnedFormat, Plural.Count(upright.Turned, VideoStrings.Photo))
                    : VideoStrings.LogNoneTurned);
                if (upright.Converted > 0)
                    AppendOutput(string.Format(VideoStrings.LogConvertedFormat, Plural.Count(upright.Converted, VideoStrings.Picture)));
                if (upright.Failed > 0)
                    AppendOutput(string.Format(VideoStrings.LogFailedFormat, Plural.Count(upright.Failed, VideoStrings.Picture)));
                SetProgress(0);

                // Cut on the beat: the rhythm is the one found when the box was ticked (waited for, if that is still going on)
                RhythmAnalysis? rhythm = null;
                if (_beatCheckBox.Checked && options.AudioPath is not null)
                {
                    if (_analyzingRhythm)
                        AppendOutput(VideoStrings.RhythmAnalyzing);

                    await _rhythmTask;
                    rhythm = BeatRhythm;
                    if (rhythm is null)
                    {
                        AppendOutput(_rhythmError is null ? VideoStrings.LogBeatsNone : string.Format(VideoStrings.LogBeatsFailedFormat, _rhythmError));
                    }
                }

                // The times: the random ones are the ones drawn for the length that is shown, so the video is as long as it says
                var samples = options.RandomFrameTimes ? RandomSamples() : null;
                var repeat = options.RepeatToAudio && options.AudioPath is not null;
                var audioSeconds = repeat ? await KnownAudioSecondsAsync(options.AudioPath!) : null;
                if (repeat && audioSeconds is null)
                    AppendOutput(VideoStrings.LogAudioUnknown);

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
                    audioSeconds,
                    options.CoverEndSeconds,
                    rhythm);

                if (rhythm is not null)
                {
                    AppendOutput(string.Format(VideoStrings.LogBeatsFormat, rhythm.Bpm, rhythm.Beats.Count));
                    if (cover is not null && options.CoverSeconds is not null)
                        AppendOutput(string.Format(VideoStrings.LogBeatsCoverFormat, sequence.Durations[0]));
                }

                if (samples is not null)
                {
                    AppendOutput(string.Format(VideoStrings.LogRandomTimesFormat,
                        Math.Min(options.StartFrameTimeSeconds, options.EndFrameTimeSeconds), Math.Max(options.StartFrameTimeSeconds, options.EndFrameTimeSeconds)));
                }

                if (options.AudioPath is not null)
                {
                    AppendOutput(sequence.Audio switch
                    {
                        AudioPlayback.OnceEndsVideo =>
                            string.Format(VideoStrings.LogAudioOnceEndsFormat, options.AudioPath, PassesText(sequence, options, samples, uprightPhotos.Count)),
                        AudioPlayback.OnceThenSilence =>
                            string.Format(VideoStrings.LogAudioOnceSilenceFormat, options.AudioPath),
                        _ =>
                            string.Format(VideoStrings.LogAudioRepeatFormat, options.AudioPath),
                    });
                }

                AppendOutput(string.Format(VideoStrings.LogExpectedFormat, DurationText.Format(sequence.ExpectedSeconds)));
                AppendOutput(string.Format(VideoStrings.LogStartingFormat, options.OutputPath));
                await _generateVideoAsync(options, sequence, AppendOutput, SetProgress);

                if (!File.Exists(options.OutputPath))
                    throw new FileNotFoundException(string.Format(VideoStrings.GeneratedNotFoundFormat, options.OutputPath), options.OutputPath);

                GeneratedOutputPath = options.OutputPath;
                SetProgress(100);
                AppendOutput(string.Format(VideoStrings.LogCompletedFormat, options.OutputPath));
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
                UsedCoverPath,
                CoverStartSeconds,
                CoverEndSeconds,
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
            var cover = (options.CoverSeconds ?? 0) + (options.CoverEndSeconds ?? 0);
            return pass > 0 ? TimesText((sequence.ExpectedSeconds - cover) / pass) : "0";
        }

        /// <summary>True: the photos play over and over until the audio ends (when it is longer); false: once.</summary>
        private bool RepeatPhotosSelected => ((Choice<bool>)_playModeCombo.SelectedItem!).Value;

        /// <summary>True: the audio plays over and over until the photos end (when it is shorter); false: once, and silence after it.</summary>
        private bool RepeatAudioSelected => ((Choice<bool>)_audioPlayModeCombo.SelectedItem!).Value;

        /// <summary>The cover picture, or null for none.</summary>
        private string? CoverPath => string.IsNullOrWhiteSpace(_coverTextBox.Text) ? null : _coverTextBox.Text.Trim();

        /// <summary>How long the cover is shown before the photos; null when it is not (no cover, or "Show at the start" not ticked).</summary>
        private double? CoverStartSeconds => CoverPath is not null && _coverAtStartCheckBox.Checked ? (double)_coverStartSecondsNumeric.Value : null;

        /// <summary>How long the cover is shown after the photos; null when it is not (no cover, or "Show at the end" not ticked).</summary>
        private double? CoverEndSeconds => CoverPath is not null && _coverAtEndCheckBox.Checked ? (double)_coverEndSecondsNumeric.Value : null;

        /// <summary>The cover picture when it is in the video (shown at the start and/or at the end), otherwise null: with both ticks off it is not used.</summary>
        private string? UsedCoverPath => CoverStartSeconds is null && CoverEndSeconds is null ? null : CoverPath;

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
            UpdateDialogSize();
        }

        /// <summary>
        /// The dialog is as tall as its rows need, and taller by the bars of the rhythm when they are shown and by the log when the video is
        /// being made (which also makes it wider). It grows downwards, so it is moved up if that would put it off the screen.
        /// </summary>
        private void UpdateDialogSize()
        {
            if (_collapsedHeight == 0)
                return; // still being built

            var logShown = _progressBar.Visible;
            var width = logShown ? ExpandedWidth : _dialogWidth;
            var height = _collapsedHeight + (_rhythmView.Visible ? _rhythmView.Height + _rhythmView.Margin.Vertical : 0) + (logShown ? LogHeight : 0);
            if (ClientSize.Width == width && ClientSize.Height == height)
                return;

            ClientSize = new Size(width, height);
            if (!IsHandleCreated)
                return;

            var area = Screen.FromControl(this).WorkingArea;
            var left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            var top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            if (left != Left || top != Top)
                Location = new Point(left, top);
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
            UpdateRhythmUi(); // the box and the buttons of the rhythm, and the two settings it turns off (which the loop above has just enabled)
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

        private void BrowseAudio()
        {
            using var dialog = new OpenFileDialog
            {
                Title = VideoStrings.ChooseAudioTitle,
                Filter = $"{VideoStrings.FilterAudio}|*.mp3;*.m4a;*.aac;*.wav;*.flac;*.ogg;*.opus;*.wma|{VideoStrings.FilterAll}|*.*",
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
                AudioPreviewState.Preparing => VideoStrings.AudioPreparing,
                AudioPreviewState.Playing => VideoStrings.AudioPlaying,
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
            _audioLengthLabel.Text = path is null ? string.Empty : VideoStrings.LengthReading;
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
                    text = VideoStrings.FileNotFound;
                }
                else
                {
                    length = await AudioDecoder.ReadDurationAsync(path, cancellation.Token);
                    text = length is { } read ? string.Format(VideoStrings.LengthFormat, DurationText.Format(read.TotalSeconds)) : VideoStrings.LengthUnknown;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // ffmpeg is not there, say: the video cannot be made either, and says so when it is tried
                text = VideoStrings.LengthUnknown;
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
            List<double> cuts = [];
            if (SelectedSource.Photos is not { } photos)
            {
                text = SelectedSource.Failed ? "-" : VideoStrings.CountingPhotos;
            }
            else
            {
                var cover = UsedCoverPath;

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
                    var coverSeconds = CoverStartSeconds;
                    var samples = RandomSamples();
                    var repeat = RepeatPhotosSelected;

                    var coverEndSeconds = CoverEndSeconds;
                    var rhythm = BeatRhythm;
                    var plan = rhythm is null ? ((double? Cover, double[] Durations)?)null : VideoGenerationService.PlanOnBeats(photoCount, start, end, samples, coverSeconds, rhythm);
                    var length = VideoGenerationService.EstimateLength(photoCount, start, end, samples, coverSeconds, repeat, _audioLength?.TotalSeconds, coverEndSeconds, rhythm);
                    text = string.Format(VideoStrings.AboutLengthFormat, DurationText.Format(length));
                    line = AudioLine(photoCount, start, end, samples, coverSeconds, repeat, coverEndSeconds, plan);
                    if (plan is { } cutPlan && rhythm is not null)
                        cuts = CutTimes(cutPlan, rhythm.DurationSeconds);
                }
            }

            _videoLengthLabel.Text = text + "\n" + line;
            _rhythmView.SetCuts(cuts);
        }

        /// <summary>The second line under the video length: what becomes of the audio (or why "Repeat" is not doing anything).</summary>
        private string AudioLine(
            int photoCount,
            double start,
            double end,
            double[]? samples,
            double? coverSeconds,
            bool repeat,
            double? coverEndSeconds,
            (double? Cover, double[] Durations)? beatPlan)
        {
            if (AudioPath is null)
                return repeat ? VideoStrings.RepeatNeedsAudio : " ";

            if (_audioLength is not { } audio)
            {
                // Still being read, or not to be told
                return repeat && !_readingAudioLength ? VideoStrings.AudioLengthUnknown : " ";
            }

            // Cut on the beat: the times are the ones of the plan (the cover at the start is moved to a beat), and the photos play once
            var photoPass = beatPlan is { } plan ? plan.Durations.Sum() : VideoGenerationService.PhotoSeconds(photoCount, start, end, samples);
            var cover = ((beatPlan is { } moved ? moved.Cover : coverSeconds) ?? 0) + (coverEndSeconds ?? 0);
            var once = cover + photoPass;
            if (repeat && photoPass > 0 && audio.TotalSeconds > once)
                return string.Format(VideoStrings.PhotosPlayTimesFormat, TimesText((audio.TotalSeconds - cover) / photoPass));

            return VideoGenerationService.FitOfAudio(once, audio.TotalSeconds) switch
            {
                AudioFit.Cut => VideoStrings.AudioCut,
                AudioFit.Repeated => RepeatAudioSelected ? VideoStrings.AudioRepeated : VideoStrings.AudioOnceThenSilent,
                _ => VideoStrings.AudioAbout,
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
            _playModeCombo.Enabled = enabled && !_beatLocked; // cut on the beat: the photos play once
            _audioPlayModeCombo.Enabled = enabled;
        }

        // ── The settings kept with the photos ───────────────────────────────────

        /// <summary>The names the dialog makes itself (timelapse_20261105_093015.mp4): they are not put back, a new one is made each time.</summary>
        private static readonly System.Text.RegularExpressions.Regex AutomaticFileName =
            new(@"^timelapse_\d{8}_\d{6}\.mp4$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Every value of the dialog, as it is to be kept (see <see cref="VideoSettings"/>). What Use random and Photos: Repeat were
        /// before the cuts on the beat put them off is what is kept, not their greyed state.
        /// </summary>
        private VideoSettings CurrentSettings() => new()
        {
            SortOrder = SelectedSortOrder.ToString(),
            OutputFolder = _outputFolderTextBox.Text.Trim(),
            OutputFileName = _outputFileNameTextBox.Text.Trim(),
            AudioPath = _audioTextBox.Text.Trim(),
            RepeatPhotos = _beatLocked ? _repeatPhotosBeforeBeats : RepeatPhotosSelected,
            RepeatAudio = RepeatAudioSelected,
            CoverPath = _coverTextBox.Text.Trim(),
            CoverAtStart = _coverAtStartCheckBox.Checked,
            CoverStartSeconds = (double)_coverStartSecondsNumeric.Value,
            CoverAtEnd = _coverAtEndCheckBox.Checked,
            CoverEndSeconds = (double)_coverEndSecondsNumeric.Value,
            Width = (int)_widthNumeric.Value,
            Height = (int)_heightNumeric.Value,
            StartFrameTimeSeconds = (double)_frameTimeStartNumeric.Value,
            EndFrameTimeSeconds = (double)_frameTimeEndNumeric.Value,
            UseRandom = _beatLocked ? _randomBeforeBeats : _randomFrameTimesCheckBox.Checked,
            Codec = _codecCombo.SelectedItem?.ToString(),
            Crf = (int)_crfNumeric.Value,
            CutOnBeat = _beatCheckBox.Checked,
            TempoOctaves = _tempoOctaves,
        };

        /// <summary>
        /// Reads the settings the last video of the picked source's folder was made with, and puts them in the dialog. Read away from the
        /// window (the folder may be on a slow disk); only the latest folder asked for is used, and nothing is touched while a video is made.
        /// </summary>
        private async Task LoadSettingsAsync()
        {
            var generation = ++_settingsGeneration;
            var folder = SelectedSource.OutputFolder;
            var settings = await Task.Run(() => VideoSettingsFile.Load(folder));
            if (settings is null || IsDisposed || generation != _settingsGeneration || _isGenerating)
                return;

            ApplySettings(settings);
        }

        /// <summary>
        /// Puts the values the file has in the dialog (ones it does not have, and ones that make no sense, are left as they are; numbers are
        /// brought into the range of their box). The cut on the beat is put off first and on last, so that the two settings it holds are
        /// free while the others are put in, and hold what is put in.
        /// </summary>
        private void ApplySettings(VideoSettings settings)
        {
            var beatBefore = _beatCheckBox.Checked;
            _beatCheckBox.Checked = false;

            if (settings.GetSortOrder() is { } order)
            {
                var index = _sortOrderCombo.Items.Cast<Choice<PhotoSortOrder>>().ToList().FindIndex(choice => choice.Value == order);
                if (index >= 0)
                    _sortOrderCombo.SelectedIndex = index;
            }

            if (!string.IsNullOrWhiteSpace(settings.OutputFolder))
                _outputFolderTextBox.Text = settings.OutputFolder;
            if (!string.IsNullOrWhiteSpace(settings.OutputFileName) && !AutomaticFileName.IsMatch(settings.OutputFileName))
                _outputFileNameTextBox.Text = settings.OutputFileName;

            if (settings.AudioPath is not null)
                _audioTextBox.Text = settings.AudioPath;
            if (settings.RepeatPhotos is { } repeatPhotos)
                _playModeCombo.SelectedIndex = repeatPhotos ? 1 : 0;
            if (settings.RepeatAudio is { } repeatAudio)
                _audioPlayModeCombo.SelectedIndex = repeatAudio ? 0 : 1;

            if (settings.CoverPath is not null)
                _coverTextBox.Text = settings.CoverPath;
            if (settings.CoverAtStart is { } atStart)
                _coverAtStartCheckBox.Checked = atStart;
            SetNumeric(_coverStartSecondsNumeric, settings.CoverStartSeconds);
            if (settings.CoverAtEnd is { } atEnd)
                _coverAtEndCheckBox.Checked = atEnd;
            SetNumeric(_coverEndSecondsNumeric, settings.CoverEndSeconds);

            SetNumeric(_widthNumeric, settings.Width);
            SetNumeric(_heightNumeric, settings.Height);
            SetNumeric(_frameTimeStartNumeric, settings.StartFrameTimeSeconds);
            SetNumeric(_frameTimeEndNumeric, settings.EndFrameTimeSeconds);
            if (settings.UseRandom is { } random)
                _randomFrameTimesCheckBox.Checked = random;

            if (settings.Codec is { } codec && _codecCombo.Items.Contains(codec))
                _codecCombo.SelectedItem = codec;
            SetNumeric(_crfNumeric, settings.Crf);

            _wantedTempoOctaves = Math.Clamp(settings.TempoOctaves ?? 0, -MaxTempoOctaves, MaxTempoOctaves);
            _beatCheckBox.Checked = settings.CutOnBeat ?? beatBefore;
        }

        /// <summary>Puts a number in a box, brought into its range (a number that is not one, or is missing, is left out).</summary>
        private static void SetNumeric(NumericUpDown box, double? value)
        {
            if (value is not { } number || double.IsNaN(number) || double.IsInfinity(number))
                return;

            box.Value = (decimal)Math.Clamp(number, (double)box.Minimum, (double)box.Maximum);
        }

        // ── Cut on the beat (experimental) ──────────────────────────────────────

        /// <summary>The rhythm the photos are cut on: ticked, an audio file, and beats found in it. Otherwise null, and the times are as set.</summary>
        private RhythmAnalysis? BeatRhythm => _beatCheckBox.Checked && AudioPath is not null && _rhythm is { HasBeats: true } ? _rhythm : null;

        /// <summary>
        /// Starts finding the rhythm of the audio in the box (or throws away what was found, when the box is not ticked or there is no
        /// audio). Only the latest audio asked for counts; the one before it is stopped. A path that is being typed costs nothing: ffmpeg
        /// is started for a file that is there.
        /// </summary>
        private void StartRhythmAnalysis()
        {
            _rhythmCancellation?.Cancel();
            var generation = ++_rhythmGeneration;
            var path = AudioPath;

            _rhythmFound = null;
            _rhythm = null;
            _tempoOctaves = 0;
            _rhythmError = null;
            _analyzingRhythm = _beatCheckBox.Checked && path is not null;
            _rhythmTask = _analyzingRhythm ? AnalyzeRhythmAsync(path!, generation) : Task.CompletedTask;
            UpdateRhythmUi();
        }

        private async Task AnalyzeRhythmAsync(string path, int generation)
        {
            var cancellation = _rhythmCancellation = new CancellationTokenSource();
            RhythmAnalysis? found = null;
            string? error = null;
            try
            {
                var stamp = await Task.Run(() =>
                {
                    var info = new FileInfo(path);
                    return info.Exists ? ((DateTime, long)?)(info.LastWriteTimeUtc, info.Length) : null;
                }, cancellation.Token);

                if (stamp is not { } file)
                {
                    // Not there (yet: it may be being typed): nothing to find, and nothing to complain about
                }
                else if (_rhythmCache is { } cache && cache.Path == path && cache.Written == file.Item1 && cache.Length == file.Item2)
                {
                    found = cache.Found;
                }
                else
                {
                    found = await RhythmAnalyzer.AnalyzeAsync(path, cancellation.Token);
                    _rhythmCache = new RhythmCacheEntry(path, file.Item1, file.Item2, found);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            // Closed, or another audio asked for, while this one was read
            if (IsDisposed || generation != _rhythmGeneration)
                return;

            _analyzingRhythm = false;
            _rhythmError = error;
            _rhythmFound = found;
            _tempoOctaves = found is { HasBeats: true } ? _wantedTempoOctaves : 0; // ÷2 / ×2 as saved with the folder, for the first rhythm found
            _wantedTempoOctaves = 0;
            _rhythm = found?.Scaled(_tempoOctaves);
            UpdateRhythmUi();
        }

        private sealed record RhythmCacheEntry(string Path, DateTime Written, long Length, RhythmAnalysis Found);

        /// <summary>÷2 / ×2: takes the beats found as half, or twice, as many.</summary>
        private void ChangeTempo(int octaves)
        {
            if (_rhythmFound is not { HasBeats: true } found)
                return;

            _tempoOctaves = Math.Clamp(_tempoOctaves + octaves, -MaxTempoOctaves, MaxTempoOctaves);
            _rhythm = found.Scaled(_tempoOctaves);
            UpdateRhythmUi();
        }

        /// <summary>
        /// Brings everything about the rhythm up to date: the tick and the two buttons, what is said of the rhythm, the bars (shown when the
        /// box is ticked, which makes the dialog taller), the two settings that do not go with it, and the length of the video.
        /// </summary>
        private void UpdateRhythmUi()
        {
            if (IsDisposed || Disposing)
                return;

            var hasAudio = AudioPath is not null;
            var ticked = _beatCheckBox.Checked;
            _beatCheckBox.Enabled = !_isGenerating && hasAudio;

            string status;
            if (!ticked || !hasAudio)
                status = string.Empty;
            else if (_analyzingRhythm)
                status = VideoStrings.RhythmAnalyzing;
            else if (_rhythmError is not null)
                status = VideoStrings.RhythmFailed;
            else if (_rhythm is null)
                status = string.Empty;
            else if (_rhythm.HasBeats)
                status = string.Format(VideoStrings.RhythmFoundFormat, _rhythm.Bpm, _rhythm.Beats.Count);
            else
                status = VideoStrings.RhythmNoBeat;

            _beatStatusLabel.Text = status;
            _beatToolTip?.SetToolTip(_beatStatusLabel, _rhythmError is null ? VideoStrings.CutOnBeatHint : $"{VideoStrings.RhythmFailed} {_rhythmError}");

            var found = ticked && !_analyzingRhythm && _rhythm is { HasBeats: true };
            _slowerBeatButton.Enabled = !_isGenerating && found && _tempoOctaves > -MaxTempoOctaves;
            _fasterBeatButton.Enabled = !_isGenerating && found && _tempoOctaves < MaxTempoOctaves;

            // The bars: shown when there is something to say with them (being found, found, or failed)
            _rhythmView.Visible = ticked && hasAudio && (_analyzingRhythm || _rhythm is not null || _rhythmError is not null);
            _rhythmView.SetAnalysis(_rhythm, _analyzingRhythm ? VideoStrings.RhythmAnalyzing : _rhythmError is null ? string.Empty : $"{VideoStrings.RhythmFailed} {_rhythmError}");

            ApplyBeatLock();
            UpdateVideoLength();
            UpdateDialogSize();
        }

        /// <summary>
        /// While the photos are cut on the beat, Use random and Photos: Repeat do nothing (every photo gets a whole number of beats, and the
        /// photos play once), so they are turned off and greyed, and are what they were when the cuts are off again.
        /// </summary>
        private void ApplyBeatLock()
        {
            var lockIt = BeatRhythm is not null;
            if (lockIt && !_beatLocked)
            {
                _beatLocked = true;
                _randomBeforeBeats = _randomFrameTimesCheckBox.Checked;
                _repeatPhotosBeforeBeats = RepeatPhotosSelected;
                _randomFrameTimesCheckBox.Checked = false;
                _playModeCombo.SelectedIndex = 0;
            }
            else if (!lockIt && _beatLocked)
            {
                _beatLocked = false;
                _randomFrameTimesCheckBox.Checked = _randomBeforeBeats;
                _playModeCombo.SelectedIndex = _repeatPhotosBeforeBeats ? 1 : 0;
            }

            _randomFrameTimesCheckBox.Enabled = !_isGenerating && !_beatLocked;
            UpdatePlayModeCombo();
        }

        /// <summary>The times, from the start of the video, where one picture gives way to the next, up to the end of the audio.</summary>
        private static List<double> CutTimes((double? Cover, double[] Durations) plan, double limitSeconds)
        {
            var cuts = new List<double>();
            var time = 0d;
            if (plan.Cover is { } cover)
            {
                time = cover;
                cuts.Add(time);
            }

            foreach (var duration in plan.Durations)
            {
                time += duration;
                if (time > limitSeconds)
                    break;

                cuts.Add(time);
            }

            return cuts;
        }

        // The width of the dialog and of its label and button columns with the texts of English: wider where the texts of the language need it
        private const int BaseDialogWidth = 534;
        private const int BaseLabelColumnWidth = 130;
        private const int BaseButtonColumnWidth = 104;
        private const int LabelColumnPadding = 6;  // the label margins
        private const int ButtonColumnPadding = 20; // the margins and the border of a button

        // The cover row: a tick (its box and its text), the time box and "(sec)" beside the preview
        private const int CoverTimeBoxWidth = 60;
        private const int CheckBoxExtraWidth = 22; // the box of a check box and the gap before its text
        private const int CoverSideGap = 6;
        private const int LayoutPadding = 12;      // each side of the table
        private const int CoverPreviewGap = 12;    // between the preview and what is on its right

        private int _dialogWidth = BaseDialogWidth;
        private int _labelColumnWidth = BaseLabelColumnWidth;
        private int _buttonColumnWidth = BaseButtonColumnWidth;

        /// <summary>
        /// The column of the labels is wide enough for the longest label, and the one of the "Use first image" button for its text. The columns
        /// between them keep their width, so the dialog grows by what these two do. It also has to be wide enough for the cover row (the
        /// preview, and the ticks with their times on its right), which the texts of some languages make wider than the base width.
        /// </summary>
        private void MeasureColumns()
        {
            int Wide(string text) => TextRenderer.MeasureText(text, Font).Width;

            string[] labels =
            [
                VideoStrings.LabelPhotosFrom, VideoStrings.LabelSortOrder, VideoStrings.LabelOutputFolder, VideoStrings.LabelOutputFileName,
                VideoStrings.LabelAudio, VideoStrings.LabelCover, VideoStrings.LabelResolution, VideoStrings.LabelFrameTimeRange,
                VideoStrings.LabelRepeatOrOnce, VideoStrings.LabelVideoLength, VideoStrings.LabelCodec, VideoStrings.LabelQuality,
                VideoStrings.LabelRhythm, VideoStrings.LengthReading, VideoStrings.LengthUnknown, VideoStrings.FileNotFound,
            ];
            _labelColumnWidth = Math.Max(BaseLabelColumnWidth, labels.Max(Wide) + LabelColumnPadding);
            _buttonColumnWidth = Math.Max(BaseButtonColumnWidth, Wide(VideoStrings.UseFirstImage) + ButtonColumnPadding);
            _dialogWidth = BaseDialogWidth + (_labelColumnWidth - BaseLabelColumnWidth) + (_buttonColumnWidth - BaseButtonColumnWidth);

            var coverSide = Math.Max(Wide(VideoStrings.ShowAtStart), Wide(VideoStrings.ShowAtEnd)) + CheckBoxExtraWidth
                + CoverSideGap + CoverTimeBoxWidth + CoverSideGap + Wide(VideoStrings.LabelSec);
            var coverRow = (2 * LayoutPadding) + _labelColumnWidth + CoverPreviewSize.Width + CoverPreviewGap + coverSide + CoverSideGap;
            _dialogWidth = Math.Max(_dialogWidth, coverRow);
        }

        /// <summary>The height that makes room for the log and the progress bar, on top of the one of the rows.</summary>
        private const int LogHeight = 150;

        /// <summary>The height of the bars of the rhythm (the strip and its scroll bar), which the dialog grows by when they are shown.</summary>
        private const int RhythmViewHeight = 118;

        private const int ExpandedWidth = 840;

        /// <summary>÷2 and ×2 can be pressed this many times in a row (a quarter of the tempo found, or four times it).</summary>
        private const int MaxTempoOctaves = 2;

        private int _collapsedHeight;

        private static readonly Size CoverPreviewSize = new(160, 90);

        private void BrowseCover()
        {
            var extensions = _extensions.Select(extension => extension.StartsWith('.') ? "*" + extension : "*." + extension).ToList();
            using var dialog = new OpenFileDialog
            {
                Title = VideoStrings.ChooseCoverTitle,
                Filter = (extensions.Count > 0 ? $"{VideoStrings.FilterPictures}|{string.Join(';', extensions)}|" : string.Empty) + $"{VideoStrings.FilterAll}|*.*",
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
            _coverInfoLabel.Text = " ";
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
                _coverInfoLabel.Text = string.Format(VideoStrings.CoverSizeFormat, fullSize.Width, fullSize.Height);
            }
            catch (Exception ex)
            {
                // Nobody waits for this (it is an event handler), so a failed preview is told here and not thrown
                if (!IsDisposed && generation == _coverGeneration)
                {
                    _coverInfoLabel.Text = ex is FileNotFoundException or DirectoryNotFoundException
                        ? VideoStrings.CoverNotThere
                        : VideoStrings.CoverNoPreview;
                }
            }
        }

        private void SetCoverPreview(Bitmap? preview)
        {
            _coverPreviewBox.Image = preview;
            _coverBitmap?.Dispose();
            _coverBitmap = preview;
        }

        /// <summary>
        /// The two ticks ("Show at the start", "Show at the end") and Clear need a cover, a time needs its tick as well, and nothing is
        /// touched while generating. The ticks stay when the cover is cleared, to be there for the next one.
        /// </summary>
        private void UpdateCoverControls()
        {
            if (IsDisposed || Disposing)
                return;

            var hasCover = CoverPath is not null;
            _coverAtStartCheckBox.Enabled = !_isGenerating && hasCover;
            _coverStartSecondsNumeric.Enabled = !_isGenerating && hasCover && _coverAtStartCheckBox.Checked;
            _coverAtEndCheckBox.Enabled = !_isGenerating && hasCover;
            _coverEndSecondsNumeric.Enabled = !_isGenerating && hasCover && _coverAtEndCheckBox.Checked;
            _coverClearButton.Enabled = !_isGenerating && hasCover;
        }

        /// <summary>An empty box says what it is for.</summary>
        private void PaintCoverPlaceholder(object? sender, PaintEventArgs e)
        {
            if (_coverPreviewBox.Image is not null)
                return;

            TextRenderer.DrawText(e.Graphics, VideoStrings.NoCoverImage, Font, _coverPreviewBox.ClientRectangle, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void ApplyFirstImageResolution()
        {
            var firstImagePath = GetFirstImagePath();
            if (string.IsNullOrWhiteSpace(firstImagePath) || !File.Exists(firstImagePath))
            {
                MessageBox.Show(this, VideoStrings.NoSourceImage, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show(this, VideoStrings.GeneratedNotAvailable, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _runButton.Enabled = false;
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(GeneratedOutputPath)
            {
                UseShellExecute = true
            });
        }

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
                MessageBox.Show(this, VideoStrings.SelectOutputFolder, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_outputFileNameTextBox.Text))
            {
                MessageBox.Show(this, VideoStrings.EnterFileName, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_outputFileNameTextBox.Text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, VideoStrings.InvalidFileName, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_audioTextBox.Text) && !File.Exists(_audioTextBox.Text.Trim()))
            {
                MessageBox.Show(this, VideoStrings.AudioNotThere, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (UsedCoverPath is { } cover && !File.Exists(cover))
            {
                MessageBox.Show(this, VideoStrings.CoverFileNotThere, MainFormStrings.VideoDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        double? CoverSeconds,
        double? CoverEndSeconds,
        bool RepeatToAudio,
        bool RepeatAudio);
}
