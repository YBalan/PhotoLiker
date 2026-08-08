namespace PhotoLikerUI
{
    internal sealed class VideoGenerationDialog : Form
    {
        private readonly Func<VideoGenerationOptions, Action<string>, Action<int>, Task> _generateVideoAsync;
        private readonly IReadOnlyList<string> _sourceFiles;

        private readonly TextBox _outputFolderTextBox = new();
        private readonly TextBox _outputFileNameTextBox = new();
        private readonly NumericUpDown _widthNumeric = new();
        private readonly NumericUpDown _heightNumeric = new();
        private readonly NumericUpDown _frameTimeStartNumeric = new();
        private readonly NumericUpDown _frameTimeEndNumeric = new();
        private readonly ComboBox _codecCombo = new();
        private readonly NumericUpDown _crfNumeric = new();
        private readonly CheckBox _reverseSourceFilesCheckBox = new();
        private readonly ProgressBar _progressBar = new();
        private readonly TextBox _outputLogTextBox = new();
        private readonly List<Control> _inputControls = [];
        private readonly Button _generateButton = new();
        private readonly Button _runButton = new();
        private readonly Button _cancelButton = new();

        private bool _isGenerating;

        public string? GeneratedOutputPath { get; private set; }

        public VideoGenerationDialog(
            string defaultOutputFolder,
            IReadOnlyList<string> sourceFiles,
            Func<VideoGenerationOptions, Action<string>, Action<int>, Task> generateVideoAsync)
        {
            _sourceFiles = sourceFiles;
            _generateVideoAsync = generateVideoAsync;

            Text = MainFormStrings.VideoDialogTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 280);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 3,
                RowCount = 11
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _outputFolderTextBox.Text = defaultOutputFolder;
            _outputFolderTextBox.Dock = DockStyle.Fill;
            _outputFileNameTextBox.Text = $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";

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

            _reverseSourceFilesCheckBox.Text = "Reverse source files";
            _reverseSourceFilesCheckBox.AutoSize = true;

            var browseButton = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            browseButton.Click += (_, _) => BrowseOutputFolder();

            layout.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            layout.Controls.Add(_outputFolderTextBox, 1, 0);
            layout.Controls.Add(browseButton, 2, 0);

            layout.Controls.Add(new Label { Text = "Output file name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(_outputFileNameTextBox, 1, 1);
            layout.SetColumnSpan(_outputFileNameTextBox, 2);

            layout.Controls.Add(new Label { Text = "Resolution:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            var resolutionPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            resolutionPanel.Controls.Add(_widthNumeric);
            resolutionPanel.Controls.Add(new Label { Text = "x", AutoSize = true, Margin = new Padding(8, 6, 8, 0) });
            resolutionPanel.Controls.Add(_heightNumeric);
            layout.Controls.Add(resolutionPanel, 1, 2);

            var firstImageButton = new Button { Text = "Use first image", AutoSize = true, Dock = DockStyle.Fill };
            firstImageButton.Click += (_, _) => ApplyFirstImageResolution();
            layout.Controls.Add(firstImageButton, 2, 2);

            layout.Controls.Add(new Label { Text = "Frame time range (sec):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            var frameRangePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            frameRangePanel.Controls.Add(new Label { Text = "Start", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeStartNumeric);
            frameRangePanel.Controls.Add(new Label { Text = "End", AutoSize = true, Margin = new Padding(12, 6, 6, 0) });
            frameRangePanel.Controls.Add(_frameTimeEndNumeric);
            layout.Controls.Add(frameRangePanel, 1, 3);
            layout.SetColumnSpan(frameRangePanel, 2);

            layout.Controls.Add(new Label { Text = "Video codec:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            layout.Controls.Add(_codecCombo, 1, 4);
            layout.SetColumnSpan(_codecCombo, 2);

            layout.Controls.Add(new Label { Text = "Quality (CRF):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
            layout.Controls.Add(_crfNumeric, 1, 5);
            layout.SetColumnSpan(_crfNumeric, 2);

            layout.Controls.Add(_reverseSourceFilesCheckBox, 1, 6);
            layout.SetColumnSpan(_reverseSourceFilesCheckBox, 2);

            var hintLabel = new Label
            {
                Text = "Lower CRF = better quality and larger file size.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(hintLabel, 0, 7);
            layout.SetColumnSpan(hintLabel, 3);

            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Visible = false;
            layout.Controls.Add(_progressBar, 0, 8);
            layout.SetColumnSpan(_progressBar, 3);

            _outputLogTextBox.Multiline = true;
            _outputLogTextBox.ReadOnly = true;
            _outputLogTextBox.WordWrap = false;
            _outputLogTextBox.ScrollBars = ScrollBars.Both;
            _outputLogTextBox.Dock = DockStyle.Fill;
            _outputLogTextBox.Visible = false;
            layout.Controls.Add(_outputLogTextBox, 0, 9);
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

            layout.Controls.Add(buttonsPanel, 0, 10);
            layout.SetColumnSpan(buttonsPanel, 3);

            Controls.Add(layout);
            AcceptButton = _generateButton;
            CancelButton = _cancelButton;

            _inputControls.AddRange([
                _outputFolderTextBox,
                _outputFileNameTextBox,
                _widthNumeric,
                _heightNumeric,
                _frameTimeStartNumeric,
                _frameTimeEndNumeric,
                _codecCombo,
                _crfNumeric,
                _reverseSourceFilesCheckBox,
                browseButton,
                firstImageButton
            ]);

            FormClosing += (_, e) =>
            {
                if (_isGenerating)
                    e.Cancel = true;
            };
        }

        private async Task GenerateAsync()
        {
            if (_isGenerating || !ValidateInputs())
                return;

            var options = CreateOptions();

            ExpandForProgress();
            SetBusyState(true);
            SetProgress(0);
            AppendOutput($"Starting ffmpeg: {options.OutputPath}");

            try
            {
                await _generateVideoAsync(options, AppendOutput, SetProgress);

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
                _reverseSourceFilesCheckBox.Checked);
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

            _generateButton.Enabled = !busy;
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

        private string? GetFirstImagePath()
        {
            if (_sourceFiles.Count == 0)
                return null;

            return _reverseSourceFilesCheckBox.Checked ? _sourceFiles[^1] : _sourceFiles[0];
        }

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
        bool ReverseSourceFiles);
}
