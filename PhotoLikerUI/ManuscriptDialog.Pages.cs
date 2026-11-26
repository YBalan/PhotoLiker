using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PhotoLiker.Manuscripts;

namespace PhotoLikerUI
{
    /// <summary>The pages of the Scan manuscripts window: the list, the page that is open (its photo and its text), adding, removing and moving pages, and keeping the work with the folder.</summary>
    internal sealed partial class ManuscriptDialog
    {
        /// <summary>The most photos a window takes as its pages from the photos the program has loaded: a folder of more than that is a folder of photos, not of a manuscript. The others are added with Add pages.</summary>
        internal const int MaximumLoadedPages = 1000;

        private bool _dirty;
        private bool _applyingPage;
        private bool _workSaveFailed;
        private bool _touched;
        private bool _pagesCapped;
        private readonly List<string> _removed = [];

        /// <summary>Opens the file dialog of Add pages; a test answers it (null: none picked).</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<string[]?>? AskPagePaths { get; set; }

        /// <summary>Asked before a page that has text is taken out of the manuscript; a test answers it.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<string, bool>? AskRemovePage { get; set; }

        private ManuscriptPage? CurrentPage => _current >= 0 && _current < _pages.Count ? _pages[_current] : null;

        // ── Loading and keeping the work ────────────────────────────────────

        private void LoadWork(IReadOnlyList<string> startPhotos, IReadOnlyList<string> loadedPhotos)
        {
            var stored = ManuscriptFile.Load(_folder);
            if (stored is not null)
            {
                _pages.AddRange(stored.Pages ?? []);
                _removed.AddRange(stored.RemovedPages ?? []);
                _work.OutputFolder = stored.OutputFolder;
                _work.OutputFileName = stored.OutputFileName;
                if (stored.GetKind() is { } kind)
                    SelectKind(kind);
                if (stored.GetLanguage() is { } language && ScanLanguages.IsSupported(Kind, language))
                    SelectLanguage(language);
                IncludePhotosCheck.Checked = stored.IncludePhotos == true;
                CutPagesCheck.Checked = stored.CutPages == true;
                CutPagesCheck.Enabled = IncludePhotosCheck.Checked;
                ScanPicturesCheck.Checked = stored.FindPictures != false; // on, unless it was turned off
                UseContextCheck.Checked = stored.UseContext == true; // off, unless it was turned on: the text of the pages goes to the service with it
                AiPlacesCheck.Checked = stored.AiPlaces == true; // off, unless it was turned on: the service writes more, which costs more
            }

            TitleBox.Text = !string.IsNullOrWhiteSpace(stored?.Title) ? stored!.Title : _folder.Length > 0 ? new DirectoryInfo(_folder).Name : string.Empty;
            AuthorBox.Text = stored?.Author ?? string.Empty;

            // The photos the program has loaded are the pages to start with, as they are the pages of a book of the program: every one that is not a page already and was not taken out on purpose
            var known = new HashSet<string>(_pages.Select(page => Key(page.File)), StringComparer.OrdinalIgnoreCase);
            var taken = new HashSet<string>(_removed.Select(Key), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var photo in loadedPhotos)
            {
                if (!IsPagePhoto(photo) || known.Contains(Key(photo)) || taken.Contains(Key(photo)))
                    continue;

                if (added >= MaximumLoadedPages)
                {
                    _pagesCapped = true;
                    break;
                }

                _pages.Add(new ManuscriptPage { File = photo });
                known.Add(Key(photo));
                added++;
            }

            // A photo that is asked for by name is a page, even one that was taken out before
            foreach (var photo in startPhotos)
            {
                if (string.IsNullOrWhiteSpace(photo))
                    continue;

                _removed.RemoveAll(path => SamePath(path, photo));
                if (!known.Contains(Key(photo)))
                {
                    _pages.Add(new ManuscriptPage { File = photo });
                    known.Add(Key(photo));
                }
            }

            _current = _pages.Count == 0
                ? -1
                : startPhotos.Count > 0 ? Math.Max(0, _pages.FindIndex(page => SamePath(page.File, startPhotos[0]))) : Math.Clamp(stored?.CurrentPage ?? 0, 0, _pages.Count - 1);
        }

        private static string Key(string? path) => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);

        /// <summary>A file that can be a page: a picture, not a video.</summary>
        private static bool IsPagePhoto(string? path) =>
            !string.IsNullOrWhiteSpace(path) && !MediaKinds.IsVideo(path) && PhotoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        private static bool SamePath(string? a, string? b) =>
            a is not null && b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private void SelectKind(ScanKind kind)
        {
            var index = KindCombo.Items.Cast<Choice<ScanKind>>().ToList().FindIndex(choice => choice.Value == kind);
            if (index >= 0)
                KindCombo.SelectedIndex = index;
            FillLanguages(Language);
        }

        internal void SelectLanguage(ScanLanguage language)
        {
            var index = LanguageCombo.Items.Cast<Choice<ScanLanguage>>().ToList().FindIndex(choice => choice.Value == language);
            if (index >= 0)
                LanguageCombo.SelectedIndex = index;
        }

        /// <summary>Takes the text of the editor into the page that is open.</summary>
        private void FlushText()
        {
            if (!_dirty || CurrentPage is not { } page)
                return;

            _dirty = false;
            page.Rtf = Editor.IsEmpty ? null : Editor.Rtf;
        }

        private void ScheduleSave()
        {
            if (_switchingSource)
                return; // a manuscript is being read into the window: what the boxes say is what it had, not a change

            _touched = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Writes the work to the file of the folder: the pages with their texts, the settings of the window. A failure is told once.</summary>
        internal bool SaveWork()
        {
            FlushText();
            _saveTimer.Stop();
            if (_folder.Length == 0 || !Directory.Exists(_folder))
                return false;

            // A window that was only looked at leaves nothing in the folder (its pages are the photos that are loaded, which it finds again)
            if (!_touched && !HasWorkToKeep() && !File.Exists(ManuscriptFile.GetPath(_folder)))
                return true;

            var settings = new ManuscriptSettings
            {
                Kind = Kind.ToString(),
                Language = Language.ToString(),
                Title = TitleBox.Text,
                Author = AuthorBox.Text,
                IncludePhotos = IncludePhotosCheck.Checked,
                CutPages = CutPagesCheck.Checked,
                FindPictures = ScanPicturesCheck.Checked,
                UseContext = UseContextCheck.Checked,
                AiPlaces = AiPlacesCheck.Checked,
                RemovedPages = _removed.Count > 0 ? [.. _removed] : null,
                OutputFolder = _work.OutputFolder,
                OutputFileName = _work.OutputFileName,
                CurrentPage = Math.Max(0, _current),
                Pages = _pages,
            };

            if (ManuscriptFile.TrySave(_folder, settings, out var error))
            {
                _workSaveFailed = false;
                return true;
            }

            if (!_workSaveFailed)
                SetStatus(string.Format(ManuscriptStrings.StatusWorkSaveFailedFormat, error));
            _workSaveFailed = true;
            return false;
        }

        /// <summary>Whether anything was done to the pages: a text, a scan, pictures, a page left out or taken out.</summary>
        private bool HasWorkToKeep() =>
            _removed.Count > 0 || _pages.Any(page => page.Rtf is not null || page.Scanned is not null || page.Lines is not null || page.Pictures is not null || page.LeftOut == true);

        // ── The list of pages ───────────────────────────────────────────────

        private static string StateMark(PageState state) => state switch
        {
            PageState.Scanned => "✓ ",
            PageState.Edited => "✎ ",
            _ => string.Empty,
        };

        private static string StateName(PageState state) => state switch
        {
            PageState.Scanned => ManuscriptStrings.StateScanned,
            PageState.Edited => ManuscriptStrings.StateEdited,
            _ => ManuscriptStrings.StateNew,
        };

        /// <summary>The list again from the pages (after one is added, removed or moved).</summary>
        private void RefreshPageList()
        {
            PageList.BeginUpdate();
            try
            {
                PageList.Items.Clear();
                _pageImages.Images.Clear();
                _thumbnailImage.Clear();
                _pageImages.Images.Add(Placeholder()); // the picture of every page until its thumbnail is there

                var items = new ListViewItem[_pages.Count];
                for (var i = 0; i < items.Length; i++)
                    items[i] = new ListViewItem { ImageIndex = 0, Tag = _pages[i] };
                PageList.Items.AddRange(items);
                for (var i = 0; i < items.Length; i++)
                    UpdatePageItem(i);

                if (_current >= 0 && _current < PageList.Items.Count)
                {
                    PageList.Items[_current].Selected = true;
                    PageList.Items[_current].EnsureVisible();
                }
            }
            finally
            {
                PageList.EndUpdate();
            }

            _shownFirst = -2; // what is in sight is looked at again
            UpdateVisibleThumbnails();
            UpdateButtons();
        }

        // The thumbnails are wanted for the pages that are in sight only: a list of hundreds of photos does not read them all (a folder on a router would be read for pages nobody looks at)
        private readonly Dictionary<string, int> _thumbnailImage = new(StringComparer.OrdinalIgnoreCase); // the picture in the image list that a page's thumbnail is
        private readonly HashSet<string> _inSight = new(StringComparer.OrdinalIgnoreCase);
        private int _shownFirst = -2;
        private int _shownHeight;

        /// <summary>The number of the first item that is at least in part in sight (items are in rows, so the ones in sight are one run of them).</summary>
        private int FirstInSight()
        {
            var low = 0;
            var high = PageList.Items.Count - 1;
            var first = 0;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (PageList.Items[middle].Bounds.Bottom > 0)
                {
                    first = middle;
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return first;
        }

        /// <summary>The numbers of the pages whose items are in sight, and a row after them (it is about to be).</summary>
        private List<int> PagesInSight()
        {
            var count = PageList.Items.Count;
            var result = new List<int>();
            if (!PageList.IsHandleCreated)
            {
                for (var i = 0; i < Math.Min(count, 40); i++)
                    result.Add(i);
                return result;
            }

            var bottom = PageList.ClientRectangle.Bottom;
            for (var i = FirstInSight(); i < count; i++)
            {
                var bounds = PageList.Items[i].Bounds;
                if (bounds.Top > bottom + bounds.Height)
                    break;

                result.Add(i);
            }

            return result;
        }

        /// <summary>Says which thumbnails are wanted now (those of the pages in sight, which are put in the list as they are there).</summary>
        private void UpdateVisibleThumbnails()
        {
            var inSight = PagesInSight();
            _inSight.Clear();
            foreach (var i in inSight)
                _inSight.Add(_pages[i].File!);

            _thumbnails.Want(_inSight);
            foreach (var i in inSight)
                ApplyThumbnail(i);
        }

        /// <summary>Looks again at what is in sight when the list was scrolled or its size changed (a list view says nothing when it is scrolled).</summary>
        private void CheckThumbnailsInSight()
        {
            if (!PageList.IsHandleCreated || PageList.Items.Count == 0)
                return;

            var first = FirstInSight();
            if (first == _shownFirst && PageList.ClientSize.Height == _shownHeight)
                return;

            _shownFirst = first;
            _shownHeight = PageList.ClientSize.Height;
            UpdateVisibleThumbnails();
        }

        private void UpdatePageItem(int index)
        {
            if (index < 0 || index >= PageList.Items.Count)
                return;

            var page = _pages[index];
            var state = ManuscriptText.StateOf(page);
            var item = PageList.Items[index];
            item.Text = $"{StateMark(state)}{index + 1}. {Path.GetFileNameWithoutExtension(page.File)}";
            item.ToolTipText = $"{string.Format(ManuscriptStrings.PageFormat, index + 1)}: {Path.GetFileName(page.File)} ({StateName(state)})";
        }

        private Bitmap Placeholder()
        {
            var bitmap = new Bitmap(_pageImages.ImageSize.Width, _pageImages.ImageSize.Height);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(_dark ? Color.FromArgb(60, 60, 64) : Color.FromArgb(225, 225, 228));
            return bitmap;
        }

        /// <summary>Puts the thumbnail of a page in the list, when it is loaded (and asks for it when it is not, if the page is in sight).</summary>
        private void ApplyThumbnail(int index)
        {
            if (index < 0 || index >= _pages.Count)
                return;

            var path = _pages[index].File!;
            var thumbnail = _thumbnails.TryGet(path, out _);
            if (thumbnail is null)
                return;

            var size = _pageImages.ImageSize;
            using var fitted = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(fitted))
            {
                g.Clear(_dark ? Color.FromArgb(60, 60, 64) : Color.FromArgb(225, 225, 228));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var scale = Math.Min((float)size.Width / thumbnail.Width, (float)size.Height / thumbnail.Height);
                var w = thumbnail.Width * scale;
                var h = thumbnail.Height * scale;
                g.DrawImage(thumbnail, (size.Width - w) / 2f, (size.Height - h) / 2f, w, h);
            }

            // The first thumbnail of a page is a new picture of the list; a thumbnail that is made again (the photo was turned) takes the place of the old
            if (_thumbnailImage.TryGetValue(path, out var imageIndex) && imageIndex < _pageImages.Images.Count)
            {
                _pageImages.Images[imageIndex] = fitted;
            }
            else
            {
                _pageImages.Images.Add(fitted);
                imageIndex = _pageImages.Images.Count - 1;
                _thumbnailImage[path] = imageIndex;
            }

            if (index < PageList.Items.Count)
                PageList.Items[index].ImageIndex = imageIndex;
            PageList.Invalidate();
        }

        private void OnThumbnailLoaded(string path)
        {
            if (!_inSight.Contains(path))
                return; // it went out of sight while it was read; the next look at the list asks for it again

            for (var i = 0; i < _pages.Count; i++)
            {
                if (string.Equals(_pages[i].File, path, StringComparison.OrdinalIgnoreCase))
                    ApplyThumbnail(i);
            }
        }

        // ── Adding, removing, moving ────────────────────────────────────────

        private static readonly string[] PhotoExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

        private void AddPages()
        {
            var paths = AskPagePaths is { } ask ? ask() : PickPagePaths();
            if (paths is null || paths.Length == 0)
                return;

            AddPagePaths(paths);
        }

        private string[]? PickPagePaths()
        {
            using var dialog = new OpenFileDialog
            {
                Title = ManuscriptStrings.AddPagesTitle,
                Multiselect = true,
                InitialDirectory = _folder.Length > 0 && Directory.Exists(_folder) ? _folder : string.Empty,
                Filter = $"{ManuscriptStrings.FilterPhotos}|{string.Join(";", PhotoExtensions.Select(extension => "*" + extension))}|{ManuscriptStrings.FilterAllFiles}|*.*",
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : null;
        }

        /// <summary>Adds the photos as pages at the end (one that is a page already is not added twice) and opens the first that was added.</summary>
        internal void AddPagePaths(IEnumerable<string> paths)
        {
            FlushText();
            var first = -1;
            foreach (var path in paths.OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
            {
                _removed.RemoveAll(removed => SamePath(removed, path)); // asked for by name: it is a page again
                if (_pages.Any(page => SamePath(page.File, path)))
                    continue;

                _pages.Add(new ManuscriptPage { File = path });
                if (first < 0)
                    first = _pages.Count - 1;
            }

            if (first < 0)
                return;

            _current = first;
            RefreshPageList();
            ShowPage();
            ScheduleSave();
        }

        internal void RemoveCurrentPage()
        {
            if (CurrentPage is not { } page || _busy)
                return;

            FlushText();
            if (ManuscriptText.HasText(page) && !(AskRemovePage?.Invoke(page.File!) ?? ConfirmRemove(page)))
                return;

            _pages.RemoveAt(_current);
            if (!_removed.Any(removed => SamePath(removed, page.File)))
                _removed.Add(page.File!); // so that it does not come back with the photos that are loaded
            _current = _pages.Count == 0 ? -1 : Math.Min(_current, _pages.Count - 1);
            RefreshPageList();
            ShowPage();
            ScheduleSave();
        }

        private bool ConfirmRemove(ManuscriptPage page) =>
            MessageBox.Show(this, string.Format(ManuscriptStrings.ConfirmRemovePageFormat, Path.GetFileName(page.File)), ManuscriptStrings.DialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        internal void MoveCurrentPage(int by)
        {
            if (CurrentPage is not { } page || _busy)
                return;

            var target = _current + by;
            if (target < 0 || target >= _pages.Count)
                return;

            FlushText();
            _pages.RemoveAt(_current);
            _pages.Insert(target, page);
            _current = target;
            RefreshPageList();
            ScheduleSave();
        }

        // ── The page that is open ───────────────────────────────────────────

        /// <summary>Opens a page of the list (the click of a person, or a test).</summary>
        internal void SelectPage(int index)
        {
            if (index == _current || index < 0 || index >= _pages.Count || _busy)
                return;

            FlushText();
            _current = index;
            ShowPage();
            ScheduleSave();
        }

        private void OnPageListSelection(object? sender, EventArgs e)
        {
            if (_loading || PageList.SelectedIndices.Count == 0)
                return;

            var index = PageList.SelectedIndices[0];
            if (index != _current)
            {
                if (_busy)
                {
                    PageList.Items[_current].Selected = true; // a scan or a turn is at work on this page
                    return;
                }

                SelectPage(index);
            }
        }

        /// <summary>Shows the page that is open: its text, its photo (decoded in the background) with the boxes of its lines.</summary>
        private async void ShowPage()
        {
            var version = ++_loadVersion;
            var page = CurrentPage;

            _loading = true;
            try
            {
                if (_current >= 0 && _current < PageList.Items.Count && !PageList.Items[_current].Selected)
                    PageList.Items[_current].Selected = true;
            }
            finally
            {
                _loading = false;
            }

            ShowText(page);
            ShowWriting(page);
            _pageInfo.Text = page is null ? ManuscriptStrings.NoPages : string.Format(ManuscriptStrings.PageCountFormat, _current + 1, _pages.Count) + " - " + Path.GetFileName(page.File);
            _infoLabel.Text = string.Empty;
            View.SetImage(null, 0);
            UpdateButtons();
            if (page is null)
                return;

            if (!File.Exists(page.File))
            {
                SetStatus(string.Format(ManuscriptStrings.PageMissingFormat, page.File));
                return;
            }

            var (image, error) = await Task.Run(() => LoadPicture(page.File!));
            if (version != _loadVersion || IsDisposed)
            {
                image?.Dispose();
                return;
            }

            if (image is null)
            {
                SetStatus(error ?? ManuscriptStrings.PhotoMissing);
                return;
            }

            ShowPicture(page, image);
        }

        /// <summary>Puts the text of a page in the editor (what is put there is not taken for the person typing).</summary>
        private void ShowText(ManuscriptPage? page)
        {
            _loading = true;
            try
            {
                Editor.Rtf = page?.Rtf ?? string.Empty;
            }
            finally
            {
                _loading = false;
            }

            _dirty = false;
        }

        /// <summary>
        /// A page that was scanned shows how: the kind of writing and the language it was read as are picked in the boxes when the page is opened. A page that was not scanned leaves the boxes as
        /// they are (they are what a scan of it will use).
        /// </summary>
        private void ShowWriting(ManuscriptPage? page)
        {
            if (page is null)
                return;

            _applyingPage = true;
            try
            {
                if (page.GetKind() is { } kind)
                    SelectKind(kind);
                if (page.GetLanguage() is { } language && ScanLanguages.IsSupported(Kind, language))
                    SelectLanguage(language);
            }
            finally
            {
                _applyingPage = false;
            }
        }

        /// <summary>Puts the picture of a page in the view with the boxes of its lines (when the photo has the shape it had when they were found).</summary>
        private void ShowPicture(ManuscriptPage page, Bitmap image)
        {
            var knowsSize = page.Width is > 0 && page.Height is > 0;
            View.SetImage(image, knowsSize ? page.Width!.Value : image.Width);
            _infoLabel.Text = string.Format(ManuscriptStrings.InfoFormat, knowsSize ? page.Width : image.Width, knowsSize ? page.Height : image.Height);

            View.Lines = LinesOf(page, image.Width, image.Height);
            View.Pictures = PicturesOf(page, image.Width, image.Height);
            ShowLinesInEditor();
        }

        /// <summary>The lines of a page, unless the photo is no longer the shape the scan read.</summary>
        private static IReadOnlyList<PhotoLiker.Drawing.Ocr.TextLine> LinesOf(ManuscriptPage page, int shownWidth, int shownHeight)
        {
            if (page.Lines is not { Count: > 0 } || page.Width is not > 0 || page.Height is not > 0)
                return [];

            var photoAspect = (double)page.Width.Value / page.Height.Value;
            var shownAspect = (double)shownWidth / shownHeight;
            if (Math.Abs(photoAspect - shownAspect) / photoAspect > 0.02)
                return [];

            return [.. page.Lines.Select(line => line.ToLine()).OfType<PhotoLiker.Drawing.Ocr.TextLine>()];
        }

        /// <summary>The picture of a photo for the view, upright and no larger than the view needs; the reason when it cannot be read.</summary>
        private static (Bitmap? Image, string? Error) LoadPicture(string path)
        {
            try
            {
                var decoded = WicPhotoDecoder.DecodeToFit(File.ReadAllBytes(path), ViewMaximumSide, ViewMaximumSide);
                var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppPArgb);
                var data = bitmap.LockBits(new Rectangle(0, 0, decoded.Width, decoded.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    Marshal.Copy(decoded.Pbgra, 0, data.Scan0, decoded.Pbgra.Length);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return (bitmap, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                return (null, ex.Message);
            }
        }

        // ── Buttons ─────────────────────────────────────────────────────────

        internal void UpdateButtons()
        {
            var hasPage = CurrentPage is not null;
            var idle = !_busy;
            AddPagesButton.Enabled = idle;
            RemovePageButton.Enabled = idle && hasPage;
            MoveUpButton.Enabled = idle && hasPage && _current > 0;
            MoveDownButton.Enabled = idle && hasPage && _current < _pages.Count - 1;
            KindCombo.Enabled = idle;
            LanguageCombo.Enabled = idle;
            ScanPageButton.Enabled = idle && hasPage;
            ScanAllButton.Enabled = idle && _pages.Count > 0;
            RescanAllButton.Enabled = idle && _pages.Count > 0;
            FindPicturesButton.Enabled = idle && hasPage;
            StopButton.Enabled = _busy && _scan is not null;
            RotateLeftButton.Enabled = idle && hasPage;
            RotateRightButton.Enabled = idle && hasPage;
            JoinLinesButton.Enabled = hasPage;
            AiCombo.Enabled = idle;
            AiServicesButton.Enabled = idle;
            SourceCombo.Enabled = idle;
            UseContextCheck.Enabled = idle && _ai.Active is not null; // the model on this computer reads a piece at a time and is not given the pages before it
            AiPlacesCheck.Enabled = idle && _ai.Active is not null && Kind == ScanKind.AiService; // only the reading of a photo by a service has places to ask for
            SaveBookButton.Enabled = idle && _pages.Count > 0;
        }

        private void SetBusy(bool busy, bool canStop = false)
        {
            _busy = busy;
            Editor.SetEditable(!busy);
            _progress.Visible = busy;
            if (!busy)
                _progress.Value = 0;
            UpdateButtons();
            StopButton.Enabled = busy && canStop;
        }
    }
}
