namespace PhotoLikerUI
{
    /// <summary>
    /// The Make a book window: pick where the photos come from (the loaded ones, the liked folder, a group), put them in the order of the book by dragging their thumbnails, write a text above and
    /// below each photo in a Rich Text Box, choose a PDF or an EPUB and make it. What is done to a book (the order, what is written, the settings) is kept with the folder of its photos, so the
    /// next time the folder is made into a book the work is there. See <see cref="ThumbnailReorderView"/>, <see cref="RichDescriptionEditor"/> and <see cref="BookBuilder"/>.
    /// </summary>
    internal sealed partial class BookDialog : Form
    {
        private readonly string _likedFolder;
        private readonly IReadOnlyCollection<string> _extensions;

        // The controls of the settings
        private readonly ComboBox _sourceCombo = new();
        private readonly Label _countLabel = new();
        private readonly ComboBox _sortCombo = new();
        private readonly Button _sortButton = new();
        private readonly ComboBox _formatCombo = new();
        private readonly TextBox _titleBox = new();
        private readonly TextBox _authorBox = new();
        private readonly ComboBox _pageCombo = new();
        private readonly ComboBox _orientationCombo = new();
        private readonly NumericUpDown _marginNumeric = new();
        private readonly ComboBox _fontCombo = new();
        private readonly CheckBox _pageNumbersCheck = new();
        private readonly ComboBox _qualityCombo = new();
        private readonly TextBox _outputFolderBox = new();
        private readonly TextBox _fileNameBox = new();
        private readonly Button _browseButton = new();
        private readonly List<Panel> _pdfOnly = [];
        private readonly List<Control> _inputs = [];

        // The thumbnails and their buttons
        private readonly ThumbnailReorderView _view;
        private readonly ToolStrip _viewTools = new();
        private readonly ToolStripButton _earlierButton = new();
        private readonly ToolStripButton _laterButton = new();
        private readonly ToolStripButton _toStartButton = new();
        private readonly ToolStripButton _toEndButton = new();
        private readonly ToolStripButton _toPlaceButton = new();
        private readonly ToolStripButton _rotateLeftButton = new();
        private readonly ToolStripButton _rotateRightButton = new();
        private readonly ToolStripButton _leaveOutButton = new();
        private readonly ToolStripButton _reverseButton = new();
        private readonly ToolStripButton _undoButton = new();
        private readonly ToolStripButton _redoButton = new();
        private readonly ToolStripButton _pickAllButton = new();
        private readonly ToolStripButton _drawButton = new();
        private readonly Func<string, IWin32Window, bool>? _drawPhoto;
        private readonly TrackBar _zoom = new();
        private readonly Label _countsLabel = new();

        // What is written for the photo that is picked
        private readonly Label _pickedLabel = new();
        private readonly PictureBox _preview = new();
        private readonly RichDescriptionEditor _above = new();
        private readonly RichDescriptionEditor _below = new();
        private readonly Label _aboveLabel = new();
        private readonly Label _belowLabel = new();
        private readonly Label _descriptionHint = new();

        // Making the book
        private readonly ProgressBar _progress = new();
        private readonly Label _statusLabel = new();
        private readonly Button _makeButton = new();
        private readonly Button _openButton = new();
        private readonly Button _closeButton = new();

        private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400 };
        private readonly bool _dark;

        /// <param name="defaultOutputFolder">The folder of the loaded photos: where the book is saved while the loaded photos are the source.</param>
        /// <param name="currentPhotos">The photos that are loaded. The other sources (the liked folder, each group folder) are listed and counted in the background, so the window opens at once; the loaded photos are the source that is picked (the liked folder when none are loaded).</param>
        /// <param name="defaultSortOrder">The order the photos of a folder that has no book yet start in.</param>
        /// <param name="drawPhoto">Opens the Draw window for a photo (its file, the window it is a modal window of) and says whether the photo file was written over; null: the window has no Draw button.</param>
        public BookDialog(
            string defaultOutputFolder,
            IReadOnlyList<PhotoFile> currentPhotos,
            string likedFolder,
            IReadOnlyCollection<string> extensions,
            PhotoSortOrder defaultSortOrder,
            bool dark,
            Func<string, IWin32Window, bool>? drawPhoto = null)
        {
            _drawPhoto = drawPhoto;
            _likedFolder = likedFolder;
            _extensions = extensions;
            _dark = dark;
            _view = new ThumbnailReorderView(new ThumbnailCache());

            var current = new Source(BookStrings.SourceCurrent, null, defaultOutputFolder, BookStrings.NoPhotosLoaded) { Photos = [.. currentPhotos] };
            var liked = new Source(BookStrings.SourceLiked, likedFolder, likedFolder, BookStrings.EmptyFolder);

            // The photos the person is looking at are where a book starts; with none loaded there is nothing to make it of, and the liked folder is the place to start
            var start = currentPhotos.Count > 0 ? current : liked;
            _automaticOutputFolder = start.OutputFolder;

            Text = BookStrings.DialogTitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = DialogIcons.Book;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1000, 680);
            ClientSize = new Size(1240, 780);
            ShowInTaskbar = false;
            MinimizeBox = false;

            BuildSettings(current, liked, start, defaultSortOrder);
            var settings = BuildSettingsPanel();
            var main = BuildMain();
            var bottom = BuildBottom();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 10, 12, 10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(settings, 0, 0);
            root.Controls.Add(main, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);
            AcceptButton = null;

            WireEvents();
            ApplyThemeAndColors(dark);
            ShowPicked();
            UpdateCommands();
            UpdateFormatControls();
            _statusLabel.Text = BookStrings.StatusReady;

            Disposed += (_, _) =>
            {
                _saveTimer.Dispose();
                _preview.Image?.Dispose();
                _cancellation?.Cancel();
            };

            _ = LoadSourceAsync();
            _ = CountFoldersAsync();
        }

        /// <summary>The file that was made, once it was (what the window hands back).</summary>
        public string? MadeBookPath { get; private set; }

        // ── Building the window ─────────────────────────────────────────────

        private static Panel Field(string label, Control input, string? tip, ToolTip toolTip, out Label caption)
        {
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 14, 6) };
            caption = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            panel.Controls.Add(caption);
            input.Margin = new Padding(0);
            panel.Controls.Add(input);
            if (tip is not null)
            {
                toolTip.SetToolTip(input, tip);
                toolTip.SetToolTip(caption, tip);
            }

            return panel;
        }

        private void BuildSettings(Source current, Source liked, Source start, PhotoSortOrder defaultSortOrder)
        {
            _sourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _sourceCombo.Width = 230;
            _sourceCombo.DropDownWidth = 420; // a group folder can carry the search it was made for in its name
            _sourceCombo.Items.Add(current);
            _sourceCombo.Items.Add(liked);
            _sourceCombo.SelectedItem = start;

            _countLabel.AutoSize = true;
            _countLabel.Margin = new Padding(8, 5, 0, 0);

            _sortCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _sortCombo.Width = 150;
            Choice<PhotoSortOrder>[] sortOrders =
            [
                new(PhotoSortOrder.OldestFirst, BookStrings.SortOldestFirst),
                new(PhotoSortOrder.NewestFirst, BookStrings.SortNewestFirst),
                new(PhotoSortOrder.ByName, BookStrings.SortByName),
                new(PhotoSortOrder.Random, BookStrings.SortRandom),
            ];
            _sortCombo.Items.AddRange(sortOrders);
            _sortCombo.SelectedIndex = Math.Max(0, Array.FindIndex(sortOrders, choice => choice.Value == defaultSortOrder));
            _sortButton.Text = BookStrings.SortButton;
            _sortButton.AutoSize = true;
            _sortButton.Margin = new Padding(6, 0, 0, 0);

            _formatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _formatCombo.Width = 170;
            _formatCombo.Items.AddRange([new Choice<BookFormat>(BookFormat.Pdf, BookStrings.FormatPdf), new Choice<BookFormat>(BookFormat.Epub, BookStrings.FormatEpub)]);
            _formatCombo.SelectedIndex = 0;

            _titleBox.Width = 240;
            _authorBox.Width = 160;

            _pageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _pageCombo.Width = 110;
            _pageCombo.Items.AddRange(
            [
                new Choice<PageFormat>(PageFormat.A4, BookStrings.PageA4), new Choice<PageFormat>(PageFormat.A5, BookStrings.PageA5),
                new Choice<PageFormat>(PageFormat.Letter, BookStrings.PageLetter), new Choice<PageFormat>(PageFormat.Square, BookStrings.PageSquare),
            ]);
            _pageCombo.SelectedIndex = 0;

            _orientationCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _orientationCombo.Width = 130;
            _orientationCombo.Items.AddRange(
            [
                new Choice<PageOrientation>(PageOrientation.Portrait, BookStrings.OrientationPortrait), new Choice<PageOrientation>(PageOrientation.Landscape, BookStrings.OrientationLandscape),
                new Choice<PageOrientation>(PageOrientation.FollowPhoto, BookStrings.OrientationFollow),
            ]);
            _orientationCombo.SelectedIndex = 2;

            _marginNumeric.Minimum = 0;
            _marginNumeric.Maximum = 60;
            _marginNumeric.DecimalPlaces = 0;
            _marginNumeric.Value = 15;
            _marginNumeric.Width = 64;

            _fontCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _fontCombo.Width = 170;
            var fonts = PdfFonts.Available();
            _fontCombo.Items.AddRange([.. (fonts.Count > 0 ? fonts : ["Segoe UI"]).Cast<object>()]);
            _fontCombo.SelectedIndex = 0;

            _pageNumbersCheck.Text = BookStrings.PageNumbers;
            _pageNumbersCheck.AutoSize = true;
            _pageNumbersCheck.Checked = true;
            _pageNumbersCheck.Margin = new Padding(0, 3, 0, 0);

            _qualityCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _qualityCombo.Width = 200;
            _qualityCombo.Items.AddRange(
            [
                new Choice<PictureQuality>(PictureQuality.Screen, BookStrings.QualityScreen), new Choice<PictureQuality>(PictureQuality.Good, BookStrings.QualityGood),
                new Choice<PictureQuality>(PictureQuality.Print, BookStrings.QualityPrint),
            ]);
            _qualityCombo.SelectedIndex = 1;

            _outputFolderBox.Width = 340;
            _outputFolderBox.Text = start.OutputFolder;
            _fileNameBox.Width = 260;
            _fileNameBox.Text = NewAutomaticFileName(BookFormat.Pdf);
            _browseButton.Text = BookStrings.Browse;
            _browseButton.AutoSize = true;
            _browseButton.Margin = new Padding(6, 0, 0, 0);
        }

        private Control BuildSettingsPanel()
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0) };

            var source = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            source.Controls.Add(_sourceCombo);
            source.Controls.Add(_countLabel);
            flow.Controls.Add(Field(BookStrings.LabelPhotosFrom, source, null, _toolTip, out _));

            var sort = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            sort.Controls.Add(_sortCombo);
            sort.Controls.Add(_sortButton);
            flow.Controls.Add(Field(BookStrings.LabelSortOrder, sort, BookStrings.SortHint, _toolTip, out _));

            flow.Controls.Add(Field(BookStrings.LabelFormat, _formatCombo, null, _toolTip, out _));
            flow.Controls.Add(Field(BookStrings.LabelTitle, _titleBox, BookStrings.TitleHint, _toolTip, out _));
            flow.Controls.Add(Field(BookStrings.LabelAuthor, _authorBox, BookStrings.TitleHint, _toolTip, out _));
            flow.SetFlowBreak(flow.Controls[^1], true);

            void AddPdfOnly(string label, Control input)
            {
                var field = Field(label, input, BookStrings.PdfOnlyHint, _toolTip, out _);
                flow.Controls.Add(field);
                _pdfOnly.Add(field); // the caption and the input are switched off together
            }

            AddPdfOnly(BookStrings.LabelPageSize, _pageCombo);
            AddPdfOnly(BookStrings.LabelOrientation, _orientationCombo);
            AddPdfOnly(BookStrings.LabelMargin, _marginNumeric);
            AddPdfOnly(BookStrings.LabelFont, _fontCombo);
            var numbers = new Panel { AutoSize = true, Margin = new Padding(0) };
            numbers.Controls.Add(_pageNumbersCheck);
            AddPdfOnly(" ", numbers);
            flow.Controls.Add(Field(BookStrings.LabelQuality, _qualityCombo, BookStrings.QualityHint, _toolTip, out _));
            flow.SetFlowBreak(flow.Controls[^1], true);

            var folder = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            folder.Controls.Add(_outputFolderBox);
            folder.Controls.Add(_browseButton);
            flow.Controls.Add(Field(BookStrings.LabelOutputFolder, folder, null, _toolTip, out _));
            flow.Controls.Add(Field(BookStrings.LabelFileName, _fileNameBox, null, _toolTip, out _));

            _inputs.AddRange([_sourceCombo, _sortCombo, _sortButton, _formatCombo, _titleBox, _authorBox, _pageCombo, _orientationCombo, _marginNumeric, _fontCombo, _pageNumbersCheck, _qualityCombo,
                _outputFolderBox, _fileNameBox, _browseButton]);
            return flow;
        }

        private Control BuildMain()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.None, SplitterWidth = 6, Margin = new Padding(0, 4, 0, 4) };
            // The splitter is put when the container has its real width (before that its width is the default one, and a distance that is more than it is refused)
            var placed = false;
            split.SizeChanged += (_, _) =>
            {
                if (placed || split.Width < 780)
                    return;

                placed = true;
                split.Panel1MinSize = 420;
                split.Panel2MinSize = 340;
                split.SplitterDistance = (int)(split.Width * 0.6);
            };

            // On the left: the buttons of the order, the thumbnails, how many there are
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _viewTools.GripStyle = ToolStripGripStyle.Hidden;
            _viewTools.Dock = DockStyle.Fill;
            _viewTools.CanOverflow = true;
            Configure(_earlierButton, "◀ " + BookStrings.MoveEarlier, BookStrings.MoveEarlier + " (Alt+←)");
            Configure(_laterButton, BookStrings.MoveLater + " ▶", BookStrings.MoveLater + " (Alt+→)");
            Configure(_toStartButton, "◀◀ " + BookStrings.MoveToStart, BookStrings.MoveToStart + " (Alt+Home)");
            Configure(_toEndButton, BookStrings.MoveToEnd + " ▶▶", BookStrings.MoveToEnd + " (Alt+End)");
            Configure(_toPlaceButton, BookStrings.MoveToPlace, BookStrings.MoveToPlace);
            Configure(_rotateLeftButton, BookStrings.RotateLeft, BookStrings.RotateLeftTip);
            Configure(_rotateRightButton, BookStrings.RotateRight, BookStrings.RotateRightTip);
            _rotateLeftButton.Name = "rotateLeftBookButton";
            _rotateRightButton.Name = "rotateRightBookButton";
            _rotateLeftButton.Image = AppIcons.RotateCounterClockwise();
            _rotateRightButton.Image = AppIcons.RotateClockwise();
            _rotateLeftButton.DisplayStyle = _rotateRightButton.DisplayStyle = ToolStripItemDisplayStyle.Image; // the text is still the accessible name
            Configure(_drawButton, BookStrings.Draw, BookStrings.DrawTip);
            _drawButton.Name = "drawBookButton";
            _drawButton.Image = AppIcons.Drawing();
            _drawButton.DisplayStyle = ToolStripItemDisplayStyle.Image; // the text is still the accessible name
            Configure(_leaveOutButton, BookStrings.LeaveOut, BookStrings.LeaveOutHint);
            Configure(_reverseButton, BookStrings.Reverse, BookStrings.Reverse);
            Configure(_undoButton, "↶ " + BookStrings.Undo, BookStrings.Undo + " (Ctrl+Z)");
            Configure(_redoButton, BookStrings.Redo + " ↷", BookStrings.Redo + " (Ctrl+Y)");
            Configure(_pickAllButton, BookStrings.SelectAll, BookStrings.SelectAll + " (Ctrl+A)");
            _zoom.Minimum = 80;
            _zoom.Maximum = 320;
            _zoom.TickFrequency = 60;
            _zoom.SmallChange = 10;
            _zoom.LargeChange = 40;
            _zoom.Value = _view.ThumbnailSize;
            _zoom.AutoSize = false;
            _zoom.Size = new Size(150, 26);
            _zoom.Anchor = AnchorStyles.Right;
            _zoom.Margin = new Padding(0);
            _toolTip.SetToolTip(_zoom, BookStrings.ThumbnailSize);
            _viewTools.Items.AddRange(
            [
                _earlierButton, _laterButton, _toStartButton, _toEndButton, _toPlaceButton, new ToolStripSeparator(), _rotateLeftButton, _rotateRightButton, new ToolStripSeparator(), _leaveOutButton, _reverseButton, new ToolStripSeparator(),
                _undoButton, _redoButton, new ToolStripSeparator(), _pickAllButton,
            ]);
            if (_drawPhoto is not null)
                _viewTools.Items.Insert(_viewTools.Items.IndexOf(_rotateRightButton) + 1, _drawButton);

            _view.Dock = DockStyle.Fill;
            _view.Margin = new Padding(0);
            _view.EmptyText = BookStrings.NoPhotosToShow;
            _view.LeftOutText = BookStrings.LeftOutLabel;
            _view.AboveTip = BookStrings.HasAboveTip;
            _view.BelowTip = BookStrings.HasBelowTip;
            _toolTip.SetToolTip(_view, BookStrings.ThumbnailsHint);

            _countsLabel.AutoSize = true;
            _countsLabel.Margin = new Padding(0, 4, 0, 0);
            // Under the thumbnails: how many there are, and the slider that sizes them
            var under = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0) };
            under.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            under.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _countsLabel.Anchor = AnchorStyles.Left;
            _countsLabel.Margin = new Padding(0);
            under.Controls.Add(_countsLabel, 0, 0);
            under.Controls.Add(_zoom, 1, 0);
            left.Controls.Add(_viewTools, 0, 0);
            left.Controls.Add(_view, 0, 1);
            left.Controls.Add(under, 0, 2);
            split.Panel1.Controls.Add(left);

            // On the right: the photo that is picked and the two places to write
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(6, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _pickedLabel.AutoSize = true;
            _pickedLabel.MaximumSize = new Size(2000, 0);
            _pickedLabel.Margin = new Padding(0, 0, 0, 4);
            _pickedLabel.AutoEllipsis = true;
            _preview.Dock = DockStyle.Fill;
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.Margin = new Padding(0, 0, 0, 6);

            _aboveLabel.Text = BookStrings.LabelAbove;
            _aboveLabel.AutoSize = true;
            _aboveLabel.Margin = new Padding(0, 2, 0, 2);
            _belowLabel.Text = BookStrings.LabelBelow;
            _belowLabel.AutoSize = true;
            _belowLabel.Margin = new Padding(0, 6, 0, 2);
            _above.Dock = DockStyle.Fill;
            _below.Dock = DockStyle.Fill;
            _descriptionHint.Text = BookStrings.DescriptionHint;
            _descriptionHint.AutoSize = true;
            _descriptionHint.MaximumSize = new Size(480, 0);
            _descriptionHint.Margin = new Padding(0, 4, 0, 0);

            right.Controls.Add(_pickedLabel, 0, 0);
            right.Controls.Add(_preview, 0, 1);
            right.Controls.Add(_aboveLabel, 0, 2);
            right.Controls.Add(_above, 0, 3);
            right.Controls.Add(_belowLabel, 0, 4);
            right.Controls.Add(_below, 0, 5);
            right.Controls.Add(_descriptionHint, 0, 6);
            split.Panel2.Controls.Add(right);
            return split;
        }

        private static void Configure(ToolStripButton button, string text, string tip)
        {
            button.Text = text;
            button.ToolTipText = tip;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.AutoToolTip = false;
        }

        private Control BuildBottom()
        {
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, AutoSize = true, Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _progress.Dock = DockStyle.Fill;
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progress.Visible = false;
            _progress.Margin = new Padding(0, 0, 0, 6);
            TaskbarProgress.Watch(_progress, () => _making);

            _statusLabel.AutoSize = true;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Anchor = AnchorStyles.Left;
            _statusLabel.MaximumSize = new Size(900, 0);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            _makeButton.Text = BookStrings.MakeButton;
            _makeButton.AutoSize = true;
            _openButton.Text = BookStrings.OpenButton;
            _openButton.AutoSize = true;
            _openButton.Visible = false;
            _closeButton.Text = BookStrings.CloseButton;
            _closeButton.AutoSize = true;
            buttons.Controls.Add(_makeButton);
            buttons.Controls.Add(_openButton);
            buttons.Controls.Add(_closeButton);

            bottom.Controls.Add(_progress, 0, 0);
            bottom.SetColumnSpan(_progress, 2);
            bottom.Controls.Add(_statusLabel, 0, 1);
            bottom.Controls.Add(buttons, 1, 1);
            return bottom;
        }

        private void ApplyThemeAndColors(bool dark)
        {
            ThemeManager.Apply(this, dark);

            // The theme paints a label in the colour of a control; here they sit on panels and show their colour
            foreach (var label in All(this).OfType<Label>().Concat<Control>(All(this).OfType<CheckBox>()))
                label.BackColor = Color.Transparent;

            var inputBack = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            foreach (var input in All(this).Where(control => control is TextBox or NumericUpDown or ComboBox))
                input.BackColor = inputBack;

            _view.BackColor = inputBack;
            _view.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            _view.AccentColor = dark ? ThemeManager.DarkHighlight : ThemeManager.LightHighlight;
            _preview.BackColor = inputBack;
            _above.ApplyTheme(dark);
            _below.ApplyTheme(dark);
            _descriptionHint.ForeColor = SystemColors.GrayText;
            _viewTools.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
            _zoom.BackColor = dark ? ThemeManager.DarkBack : ThemeManager.LightBack;
        }

        private static IEnumerable<Control> All(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (var descendant in All(child))
                    yield return descendant;
        }

        /// <summary>An item of a combo box: the value, shown as its text.</summary>
        private sealed class Choice<T>(T value, string text)
        {
            public T Value { get; } = value;

            public override string ToString() => text;
        }
    }
}
