using PhotoLiker.Drawing;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Cloud;
using PhotoLiker.Manuscripts.Pages;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Scan manuscripts window: the photos of the pages of a manuscript (handwritten or printed) in a list on the left, the photo of the page that is open in the middle (with a box round each line the
    /// scan found), and its text on the right in a Rich Text Box, to be read against the photo and corrected. The writing and its language are picked, a page (or every page that has no text) is
    /// scanned, the photo can be turned (and is saved turned, with its Exif and dates), and the text of all the pages is saved as a PDF or an EPUB. Everything that is done is kept with the
    /// folder of the photos (<see cref="ManuscriptFile"/>), so the work goes on after the program is closed. The reading is done by <see cref="ManuscriptScanner"/> and its neighbours in
    /// PhotoLiker.Manuscripts, which have no window; see <see cref="ManuscriptView"/> for the photo and <see cref="RichDescriptionEditor"/> for the text.
    /// </summary>
    internal sealed partial class ManuscriptDialog : Form
    {
        private const int SaveDelayMilliseconds = 1500;
        private const int ViewMaximumSide = 2600;

        private string _folder; // the folder whose manuscript is open (it changes with the "Photos from" box)
        private readonly bool _dark;
        private readonly EffectModels _models;
        private readonly Func<ScanKind, ScanLanguage, Task<bool>> _ensureModelsAsync;
        private readonly Func<Task<bool>>? _ensureCutoutAsync;
        private readonly Func<Task<bool>>? _ensurePicturesAsync;
        private readonly Func<Task<bool>>? _ensureFixAsync;
        private readonly Action<string, int?>? _photoChanged;
        private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400 };
        private readonly ThumbnailCache _thumbnails = new();
        private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = SaveDelayMilliseconds };
        private readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = 150 };

        private readonly List<ManuscriptPage> _pages = [];
        private readonly ManuscriptSettings _work = new();
        private int _current = -1;
        private bool _loading;
        private bool _busy;
        private int _loadVersion;
        private CancellationTokenSource? _scan;
        private ManuscriptScanner? _scanner;
        private PictureFinder? _pictureFinder;

        // The toolbar
        internal readonly ToolStrip Bar = new();
        internal readonly ToolStripButton AddPagesButton = new();
        internal readonly ToolStripButton RemovePageButton = new();
        internal readonly ToolStripButton MoveUpButton = new();
        internal readonly ToolStripButton MoveDownButton = new();
        internal readonly ToolStripLabel KindLabel = new();
        internal readonly ToolStripComboBox KindCombo = new();
        internal readonly ToolStripLabel LanguageLabel = new();
        internal readonly ToolStripComboBox LanguageCombo = new();
        internal readonly ToolStripButton ScanPageButton = new();
        internal readonly ToolStripButton ScanAllButton = new();
        internal readonly ToolStripButton StopButton = new();
        internal readonly ToolStripButton FindPicturesButton = new();
        internal readonly ToolStripButton RotateLeftButton = new();
        internal readonly ToolStripButton RotateRightButton = new();
        internal readonly ToolStripButton ShowLinesButton = new();
        internal readonly ToolStripButton JoinLinesButton = new();

        // Which AI the AI button of the text uses: a row over the text, by the AI button (the toolbar is wider than the window as it is)
        internal readonly Label AiLabel = new();
        internal readonly ComboBox AiCombo = new();
        internal readonly Button AiServicesButton = new();
        internal readonly CheckBox UseContextCheck = new();
        private readonly FlowLayoutPanel _aiRow = new();

        // The pages, the photo and the text
        internal readonly ListView PageList = new();
        private readonly ImageList _pageImages = new() { ImageSize = new Size(72, 96), ColorDepth = ColorDepth.Depth32Bit };
        internal readonly ManuscriptView View = new();
        internal readonly RichDescriptionEditor Editor = new();
        private readonly Label _textTitle = new();
        private readonly Label _textHint = new();
        private readonly Label _pagesTitle = new();
        private readonly Label _pageInfo = new();
        private readonly SplitContainer _outer = new();
        private readonly SplitContainer _inner = new();

        // The book
        internal readonly Label TitleLabel = new();
        internal readonly TextBox TitleBox = new();
        internal readonly Label AuthorLabel = new();
        internal readonly TextBox AuthorBox = new();
        internal readonly CheckBox IncludePhotosCheck = new();
        internal readonly CheckBox CutPagesCheck = new();
        internal readonly CheckBox ScanPicturesCheck = new();
        internal readonly Button SaveBookButton = new();
        internal readonly Button CloseButton = new();

        // The status bar
        private readonly StatusStrip _statusBar = new();
        private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _infoLabel = new();
        private readonly ToolStripProgressBar _progress = new() { Visible = false, Width = 160 };

        /// <param name="folder">The folder of the manuscript: where its work is kept (the folder of the loaded photos).</param>
        /// <param name="startPhotos">The photos the window starts with: they are added to the pages of the manuscript of the folder when they are not in it, and the first of them is the page that is open.</param>
        /// <param name="models">Where the models are, and how many threads they may use.</param>
        /// <param name="ensureModelsAsync">Makes sure the models for the kind of writing and the language are on this computer (asks, and downloads them, if they are not) and says whether they are.</param>
        /// <param name="photoChanged">Told when the photo of a page was written over (its file, and the quarter turns when only its Exif was changed); the main window follows it up.</param>
        /// <param name="ensureCutoutAsync">Makes sure the model that finds the sheet in a photo (the one of the Telegram stickers) is on this computer, as <paramref name="ensureModelsAsync"/> does for the models of the scan; null: it is taken to be there.</param>
        /// <param name="loadedPhotos">The photos the program has loaded, in its order: they are the pages of a manuscript that has none yet (as the book of the program starts with them), and are added to one that has, except the photos that were taken out of it on purpose. Photos that are not pictures are skipped, and no more than <see cref="MaximumLoadedPages"/> are taken.</param>
        /// <param name="ensurePicturesAsync">Makes sure the models that find the pictures on a page (the finder of lines and the model of the Telegram stickers) are on this computer; null: they are taken to be there.</param>
        /// <param name="ensureFixAsync">Makes sure the model of the AI that corrects the text (the language model and its tokenizer) is on this computer; null: it is taken to be there.</param>
        /// <param name="aiServices">The keys of the AI services (ChatGPT, Gemini, Claude) and which of them corrects the text; null: the ones the program keeps (<see cref="AiServiceSettings.DefaultPath"/>).</param>
        /// <param name="likedFolder">The folder of the liked photos: the "Photos from" box offers it, and the groups in it, besides the photos that are loaded; null or empty: the box has the loaded photos only.</param>
        /// <param name="extensions">The extensions of the photos of a folder (with the dot); null: those of <see cref="PhotoExtensions"/>.</param>
        public ManuscriptDialog(
            string folder,
            IReadOnlyList<string> startPhotos,
            EffectModels models,
            bool dark,
            Func<ScanKind, ScanLanguage, Task<bool>> ensureModelsAsync,
            Action<string, int?>? photoChanged = null,
            Func<Task<bool>>? ensureCutoutAsync = null,
            IReadOnlyList<string>? loadedPhotos = null,
            Func<Task<bool>>? ensurePicturesAsync = null,
            Func<Task<bool>>? ensureFixAsync = null,
            AiServiceSettings? aiServices = null,
            string? likedFolder = null,
            IReadOnlyCollection<string>? extensions = null)
        {
            _ai = aiServices ?? AiServiceSettings.Load();
            _likedFolder = likedFolder;
            _extensions = extensions ?? PhotoExtensions;
            _ensureCutoutAsync = ensureCutoutAsync;
            _ensurePicturesAsync = ensurePicturesAsync;
            _ensureFixAsync = ensureFixAsync;
            _dark = dark;
            _models = models;
            _ensureModelsAsync = ensureModelsAsync;
            _photoChanged = photoChanged;
            _folder = !string.IsNullOrWhiteSpace(folder) ? folder : startPhotos.Count > 0 ? Path.GetDirectoryName(startPhotos[0]) ?? string.Empty : string.Empty;
            _currentFolder = _folder;

            Text = ManuscriptStrings.DialogTitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = DialogIcons.Manuscript;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            KeyPreview = false;

            var screen = Screen.FromPoint(MousePosition).WorkingArea;
            MinimumSize = new Size(Math.Min(980, screen.Width - 40), Math.Min(600, screen.Height - 60));
            ClientSize = new Size(Math.Min(1300, screen.Width - 40), Math.Min(800, screen.Height - 60));

            BuildBar();
            BuildBody();
            FillAiCombo();
            FillSources(loadedPhotos ?? []);
            var bottom = BuildBottom();
            BuildStatusBar();

            Controls.Add(_outer);
            Controls.Add(bottom);
            Controls.Add(_statusBar);
            Controls.Add(Bar);

            LoadWork(startPhotos, loadedPhotos ?? []);
            WireEvents();
            ApplyTheme(dark);
            RefreshPageList();
            UpdateButtons();
            SetStatus(_pagesCapped ? string.Format(ManuscriptStrings.StatusPagesCappedFormat, MaximumLoadedPages) : ManuscriptStrings.StatusReady);

            Disposed += (_, _) =>
            {
                _saveTimer.Dispose();
                _scrollTimer.Dispose();
                _toolTip.Dispose();
                _scan?.Cancel();
                _scanner?.Dispose();
                _pictureFinder?.Dispose();
                _thumbnails.Dispose();
                _pageImages.Dispose();
            };
        }

        /// <summary>The file of the book that was made last, once one was.</summary>
        public string? MadeBookPath { get; private set; }

        /// <summary>The number of pages of that file.</summary>
        public int MadeBookPages { get; private set; }

        /// <summary>The pages of the manuscript as they are now (for the tests).</summary>
        internal IReadOnlyList<ManuscriptPage> Pages => _pages;

        /// <summary>The number of the page that is open, -1 when there is none.</summary>
        internal int CurrentIndex => _current;

        /// <summary>How many thumbnails of pages are loaded: only the pages in sight have theirs (for the tests).</summary>
        internal int ThumbnailCount => _thumbnails.Count;

        /// <summary>The text of the status bar (for the tests).</summary>
        internal string StatusText => _statusLabel.Text ?? string.Empty;

        /// <summary>True while a scan, a turn or a book is being made.</summary>
        internal bool IsBusy => _busy;

        /// <summary>The kind of writing that is picked.</summary>
        internal ScanKind Kind => KindCombo.SelectedItem is Choice<ScanKind> choice ? choice.Value : ScanKind.Handwriting;

        /// <summary>The language that is picked.</summary>
        internal ScanLanguage Language => LanguageCombo.SelectedItem is Choice<ScanLanguage> choice ? choice.Value : ScanLanguage.Ukrainian;

        // ── Building the window ─────────────────────────────────────────────

        private void BuildBar()
        {
            Bar.GripStyle = ToolStripGripStyle.Hidden;
            Bar.Dock = DockStyle.Top;
            Bar.Name = "manuscriptBar";
            Bar.ImageScalingSize = new Size(18, 18);
            Bar.Padding = new Padding(4, 2, 4, 2);
            Bar.CanOverflow = true;

            Plain(AddPagesButton, "addPagesButton", ManuscriptStrings.AddPages, ManuscriptStrings.AddPagesTip);
            Plain(RemovePageButton, "removePageButton", ManuscriptStrings.RemovePage, ManuscriptStrings.RemovePageTip);
            Plain(MoveUpButton, "movePageUpButton", "▲", ManuscriptStrings.MovePageUpTip);
            Plain(MoveDownButton, "movePageDownButton", "▼", ManuscriptStrings.MovePageDownTip);
            MoveUpButton.AccessibleName = ManuscriptStrings.MovePageUpTip;
            MoveDownButton.AccessibleName = ManuscriptStrings.MovePageDownTip;

            KindLabel.Text = ManuscriptStrings.LabelKind;
            KindLabel.Name = "kindLabel";
            KindCombo.Name = "kindCombo";
            KindCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            KindCombo.AutoSize = false;
            KindCombo.Width = 140;
            KindCombo.DropDownWidth = 200;
            KindCombo.ToolTipText = ManuscriptStrings.HintKind;
            KindCombo.Items.AddRange(
            [
                new Choice<ScanKind>(ScanKind.Handwriting, ManuscriptStrings.KindHandwriting), new Choice<ScanKind>(ScanKind.Printed, ManuscriptStrings.KindPrinted),
                new Choice<ScanKind>(ScanKind.AiService, ManuscriptStrings.KindAi),
            ]);
            KindCombo.SelectedIndex = 0;

            LanguageLabel.Text = ManuscriptStrings.LabelLanguage;
            LanguageLabel.Name = "languageLabel";
            LanguageCombo.Name = "languageCombo";
            LanguageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            LanguageCombo.AutoSize = false;
            LanguageCombo.Width = 120;
            LanguageCombo.ToolTipText = ManuscriptStrings.HintLanguage;
            FillLanguages(ScanLanguage.Ukrainian);

            Plain(ScanPageButton, "scanPageButton", ManuscriptStrings.ScanPage, ManuscriptStrings.ScanPageTip);
            Plain(ScanAllButton, "scanAllButton", ManuscriptStrings.ScanAll, ManuscriptStrings.ScanAllTip);
            Plain(StopButton, "stopScanButton", ManuscriptStrings.Stop, ManuscriptStrings.StopTip);
            Plain(FindPicturesButton, "findPicturesButton", ManuscriptStrings.FindPictures, ManuscriptStrings.FindPicturesTip);
            ScanPageButton.Font = new Font(Bar.Font, FontStyle.Bold);

            Plain(RotateLeftButton, "rotateLeftButton", ManuscriptStrings.RotateLeft, ManuscriptStrings.RotateLeftTip);
            Plain(RotateRightButton, "rotateRightButton", ManuscriptStrings.RotateRight, ManuscriptStrings.RotateRightTip);
            RotateLeftButton.Image = AppIcons.RotateCounterClockwise();
            RotateRightButton.Image = AppIcons.RotateClockwise();
            RotateLeftButton.DisplayStyle = RotateRightButton.DisplayStyle = ToolStripItemDisplayStyle.Image; // the text is still the accessible name

            Plain(ShowLinesButton, "showLinesButton", ManuscriptStrings.ShowLines, ManuscriptStrings.ShowLinesTip);
            ShowLinesButton.CheckOnClick = true;
            ShowLinesButton.Checked = true;
            Plain(JoinLinesButton, "joinLinesButton", ManuscriptStrings.JoinLines, ManuscriptStrings.JoinLinesTip);

            Bar.Items.AddRange(
            [
                AddPagesButton, RemovePageButton, MoveUpButton, MoveDownButton, new ToolStripSeparator(),
                KindLabel, KindCombo, LanguageLabel, LanguageCombo, new ToolStripSeparator(),
                ScanPageButton, ScanAllButton, StopButton, FindPicturesButton, new ToolStripSeparator(),
                RotateLeftButton, RotateRightButton, new ToolStripSeparator(),
                ShowLinesButton, JoinLinesButton,
            ]);
        }

        private void Plain(ToolStripButton button, string name, string text, string tip)
        {
            button.Name = name;
            button.Text = text;
            button.ToolTipText = tip;
            button.AccessibleName = text;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
        }

        /// <summary>The languages the kind of writing that is picked can be read in; the one that was picked stays when the new kind has it too.</summary>
        private void FillLanguages(ScanLanguage keep)
        {
            LanguageCombo.Items.Clear();
            foreach (var language in ScanLanguages.For(Kind))
                LanguageCombo.Items.Add(new Choice<ScanLanguage>(language, LanguageName(language)));

            var index = Math.Max(0, ScanLanguages.For(Kind).ToList().IndexOf(keep));
            LanguageCombo.SelectedIndex = index;
        }

        private static string LanguageName(ScanLanguage language) => language switch
        {
            ScanLanguage.Ukrainian => ManuscriptStrings.LanguageUkrainian,
            ScanLanguage.Russian => ManuscriptStrings.LanguageRussian,
            ScanLanguage.English => ManuscriptStrings.LanguageEnglish,
            _ => ManuscriptStrings.LanguageMultilingual,
        };

        private void BuildBody()
        {
            // The pages
            PageList.Name = "pageList";
            PageList.View = System.Windows.Forms.View.LargeIcon;
            PageList.LargeImageList = _pageImages;
            PageList.MultiSelect = false;
            PageList.HideSelection = false;
            PageList.Dock = DockStyle.Fill;
            PageList.BorderStyle = BorderStyle.None;
            PageList.Alignment = ListViewAlignment.Top;
            PageList.ShowItemToolTips = true;

            _pagesTitle.Text = ManuscriptStrings.PagesTitle;
            _pagesTitle.Dock = DockStyle.Top;
            _pagesTitle.Padding = new Padding(6, 6, 0, 4);
            _pagesTitle.AutoSize = true;
            _pagesTitle.Font = new Font(Font, FontStyle.Bold);

            // Where the pages are from: over the list, under its title
            BuildSourceRow();
            var pages = new Panel { Dock = DockStyle.Fill };
            pages.Controls.Add(PageList);
            pages.Controls.Add(_sourceRow);
            pages.Controls.Add(_pagesTitle);

            // The photo
            View.Name = "manuscriptView";
            View.Dock = DockStyle.Fill;
            _pageInfo.Name = "pageInfo";
            _pageInfo.Dock = DockStyle.Top;
            _pageInfo.Padding = new Padding(6, 6, 0, 4);
            _pageInfo.AutoSize = true;
            var photo = new Panel { Dock = DockStyle.Fill };
            photo.Controls.Add(View);
            photo.Controls.Add(_pageInfo);

            // The text
            _textTitle.Text = ManuscriptStrings.TextTitle;
            _textTitle.Dock = DockStyle.Top;
            _textTitle.Padding = new Padding(6, 6, 0, 0);
            _textTitle.AutoSize = true;
            _textTitle.Font = new Font(Font, FontStyle.Bold);
            _textHint.Text = ManuscriptStrings.TextHint;
            _textHint.Dock = DockStyle.Top;
            _textHint.Padding = new Padding(6, 0, 6, 4);
            _textHint.AutoSize = true;
            _textHint.MaximumSize = new Size(560, 0);
            Editor.Name = "manuscriptEditor";
            Editor.Dock = DockStyle.Fill;
            Editor.FixVisible = true; // the AI button at the end of the buttons of the text
            Editor.FixButton.ToolTipText = ManuscriptStrings.FixTextTip;
            Editor.FixButton.AccessibleName = ManuscriptStrings.FixText;

            // Which AI the AI button uses (the model on this computer, or a service with a key), and the window of the keys; wraps when the text is narrow
            AiLabel.Name = "aiLabel";
            AiLabel.Text = ManuscriptStrings.LabelAi;
            AiLabel.AutoSize = true;
            AiLabel.Margin = new Padding(0, 6, 4, 0);
            AiCombo.Name = "aiCombo";
            AiCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            AiCombo.Width = 230;
            AiCombo.DropDownWidth = 340; // the name of a service with its model is longer than the box
            AiCombo.Margin = new Padding(0, 2, 8, 0);
            _toolTip.SetToolTip(AiCombo, ManuscriptStrings.HintAi);
            AiServicesButton.Name = "aiServicesButton";
            AiServicesButton.Text = ManuscriptStrings.AiServices;
            AiServicesButton.AutoSize = true;
            AiServicesButton.Padding = new Padding(6, 1, 6, 1);
            AiServicesButton.Margin = new Padding(0, 1, 0, 0);
            _toolTip.SetToolTip(AiServicesButton, ManuscriptStrings.AiServicesTip);
            UseContextCheck.Name = "useContextCheck";
            UseContextCheck.Text = ManuscriptStrings.UseContext;
            UseContextCheck.AutoSize = true;
            UseContextCheck.Margin = new Padding(12, 4, 0, 0);
            _toolTip.SetToolTip(UseContextCheck, ManuscriptStrings.HintUseContext);
            _aiRow.Name = "aiRow";
            _aiRow.Dock = DockStyle.Top;
            _aiRow.AutoSize = true;
            _aiRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _aiRow.FlowDirection = FlowDirection.LeftToRight;
            _aiRow.WrapContents = true;
            _aiRow.Padding = new Padding(6, 0, 6, 4);
            _aiRow.Controls.AddRange([AiLabel, AiCombo, AiServicesButton, UseContextCheck]);

            var text = new Panel { Dock = DockStyle.Fill };
            text.Controls.Add(Editor);
            text.Controls.Add(_aiRow);
            text.Controls.Add(_textHint);
            text.Controls.Add(_textTitle);

            _inner.Orientation = Orientation.Vertical;
            _inner.Dock = DockStyle.Fill;
            _inner.Panel1.Controls.Add(photo);
            _inner.Panel2.Controls.Add(text);

            _outer.Orientation = Orientation.Vertical;
            _outer.Dock = DockStyle.Fill;
            _outer.Panel1.Controls.Add(pages);
            _outer.Panel2.Controls.Add(_inner);

            // The splitters can only be set to a distance the control is wide enough for: when it has its size
            HandleCreated += (_, _) => BeginInvoke(PlaceSplitters);
        }

        private void PlaceSplitters()
        {
            if (IsDisposed || _outer.Width < 400)
                return;

            try
            {
                _outer.Panel1MinSize = 110;
                _outer.SplitterDistance = 170;
                _inner.Panel1MinSize = 220;
                _inner.Panel2MinSize = 260;
                _inner.SplitterDistance = Math.Max(220, _inner.Width * 52 / 100);
            }
            catch (InvalidOperationException)
            {
                // the window is too small for the minimum sizes at this moment; it keeps the distances it has
            }
        }

        private Panel BuildBottom()
        {
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(8, 6, 8, 6) };

            TitleLabel.Text = ManuscriptStrings.LabelTitle;
            TitleLabel.AutoSize = true;
            TitleBox.Name = "bookTitleBox";
            TitleBox.Width = 230;
            AuthorLabel.Text = ManuscriptStrings.LabelAuthor;
            AuthorLabel.AutoSize = true;
            AuthorBox.Name = "bookAuthorBox";
            AuthorBox.Width = 150;
            IncludePhotosCheck.Name = "includePhotosCheck";
            IncludePhotosCheck.Text = ManuscriptStrings.IncludePhotos;
            IncludePhotosCheck.AutoSize = true;
            _toolTip.SetToolTip(IncludePhotosCheck, ManuscriptStrings.HintIncludePhotos);
            CutPagesCheck.Name = "cutPagesCheck";
            CutPagesCheck.Text = ManuscriptStrings.CutPages;
            CutPagesCheck.AutoSize = true;
            CutPagesCheck.Enabled = false; // it is for the photos in the book
            _toolTip.SetToolTip(CutPagesCheck, ManuscriptStrings.HintCutPages);
            ScanPicturesCheck.Name = "scanPicturesCheck";
            ScanPicturesCheck.Text = ManuscriptStrings.ScanPictures;
            ScanPicturesCheck.AutoSize = true;
            ScanPicturesCheck.Checked = true;
            _toolTip.SetToolTip(ScanPicturesCheck, ManuscriptStrings.HintScanPictures);

            SaveBookButton.Name = "saveBookButton";
            SaveBookButton.Text = ManuscriptStrings.SaveBook;
            SaveBookButton.AutoSize = true;
            SaveBookButton.Padding = new Padding(6, 1, 6, 1);
            _toolTip.SetToolTip(SaveBookButton, ManuscriptStrings.SaveBookTip);
            CloseButton.Name = "closeManuscriptButton";
            CloseButton.Text = ManuscriptStrings.Close;
            CloseButton.AutoSize = true;
            CloseButton.Padding = new Padding(6, 1, 6, 1);

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = false };
            foreach (var control in new Control[] { TitleLabel, TitleBox, AuthorLabel, AuthorBox, IncludePhotosCheck, CutPagesCheck, ScanPicturesCheck })
                left.Controls.Add(control);
            TitleLabel.Margin = new Padding(0, 6, 4, 0);
            TitleBox.Margin = new Padding(0, 2, 14, 0);
            AuthorLabel.Margin = new Padding(0, 6, 4, 0);
            AuthorBox.Margin = new Padding(0, 2, 14, 0);
            IncludePhotosCheck.Margin = new Padding(0, 5, 14, 0);
            CutPagesCheck.Margin = new Padding(0, 5, 14, 0);
            ScanPicturesCheck.Margin = new Padding(0, 5, 0, 0);

            var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            SaveBookButton.Margin = new Padding(0, 0, 8, 0);
            CloseButton.Margin = new Padding(0);
            right.Controls.Add(SaveBookButton);
            right.Controls.Add(CloseButton);

            bottom.Controls.Add(left);
            bottom.Controls.Add(right);
            return bottom;
        }

        private void BuildStatusBar()
        {
            _statusBar.Name = "manuscriptStatus";
            _statusBar.SizingGrip = false;
            _statusBar.Items.AddRange([_statusLabel, _progress, _infoLabel]);
        }

        private void ApplyTheme(bool dark)
        {
            ThemeManager.Apply(this, dark);

            var back = dark ? ThemeManager.DarkBack : ThemeManager.LightBack;
            var inputBack = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            foreach (var control in All(this))
            {
                if (control is Label or CheckBox)
                    control.BackColor = Color.Transparent;
                else if (control is TextBox or ComboBox)
                    control.BackColor = inputBack;
            }

            PageList.BackColor = inputBack;
            PageList.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            View.BackColor = dark ? Color.FromArgb(28, 28, 30) : Color.FromArgb(120, 120, 124);
            _outer.BackColor = back;
            _inner.BackColor = back;
            _textHint.ForeColor = SystemColors.GrayText;
            Editor.ApplyTheme(dark);
            foreach (var combo in new[] { KindCombo, LanguageCombo })
            {
                combo.BackColor = inputBack;
                combo.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            }

            _statusBar.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
        }

        private static IEnumerable<Control> All(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (var descendant in All(child))
                    yield return descendant;
        }

        private void SetStatus(string text) => _statusLabel.Text = text;

        /// <summary>Shows what the main window says about a model that is being downloaded for this window (its progress), which is behind this window while it is open.</summary>
        internal void ShowModelStatus(string text)
        {
            if (!IsDisposed)
                SetStatus(text);
        }

        /// <summary>An item of a combo box: the value, shown as its text.</summary>
        private sealed class Choice<T>(T value, string text)
        {
            public T Value { get; } = value;

            public override string ToString() => text;
        }
    }
}
