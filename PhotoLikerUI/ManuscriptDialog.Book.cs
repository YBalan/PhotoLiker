using PhotoLiker.Manuscripts;

namespace PhotoLikerUI
{
    /// <summary>
    /// The rest of the Scan manuscripts window: turning the photo of a page (in its file), making the book (a PDF or an EPUB of the texts of the pages, with or without their photos), the events
    /// of the controls, the keys, and closing.
    /// </summary>
    internal sealed partial class ManuscriptDialog
    {
        /// <summary>Asks where the book goes (the file dialog of Save as PDF or EPUB); a test answers it. The result is the path, null for none.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<string?>? AskBookPath { get; set; }

        // ── Turning the photo ───────────────────────────────────────────────

        /// <summary>
        /// Turns the photo of the page that is open a quarter (positive clockwise) and saves it in its file: a JPEG by its Exif (nothing is coded again; the date taken and the dates of the file stay or
        /// become one moment, see <see cref="PhotoTurner"/>), another kind of photo by its pixels. The boxes of the lines of the scan turn with it, so the page needs no new scan.
        /// </summary>
        internal async Task RotateCurrentAsync(int quarterTurns)
        {
            if (_busy || CurrentPage is not { } page || !File.Exists(page.File))
                return;

            FlushText();
            var file = page.File!;
            var number = _current + 1;
            SetBusy(true);
            PhotoTurner.Turned? turned = null;
            string? error = null;
            try
            {
                turned = await Task.Run(() => PhotoTurner.Turn(file, quarterTurns));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or ArgumentException)
            {
                error = ex.Message;
            }

            if (IsDisposed)
                return;

            SetBusy(false);
            if (turned is null)
            {
                SetStatus(string.Format(ManuscriptStrings.StatusRotateFailedFormat, error));
                return;
            }

            TurnLines(page, quarterTurns);
            _thumbnails.Forget(file);
            ApplyThumbnail(_current);
            _photoChanged?.Invoke(file, turned.Lossless ? quarterTurns : null);
            ShowPage();
            SetStatus(string.Format(ManuscriptStrings.StatusRotatedFormat, number));
            ScheduleSave();
        }

        /// <summary>The boxes of the lines of a page, and the outlines of its pictures, after its photo is turned: each point goes where the photo's pixel goes, and the size of the photo changes places.</summary>
        internal static void TurnLines(ManuscriptPage page, int quarterTurns)
        {
            var turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0 || page.Width is not > 0 || page.Height is not > 0)
                return;

            var width = page.Width.Value;
            var height = page.Height.Value;

            void Turn(float[] points)
            {
                for (var i = 0; i + 1 < points.Length; i += 2)
                {
                    float x = points[i], y = points[i + 1];
                    (points[i], points[i + 1]) = turns switch
                    {
                        1 => (height - y, x),
                        2 => (width - x, height - y),
                        _ => (y, width - x),
                    };
                }
            }

            foreach (var line in page.Lines ?? [])
            {
                if (line.Box is { Length: 8 } box)
                    Turn(box);
            }

            foreach (var picture in page.Pictures ?? [])
            {
                if (picture.Outline is { } outline)
                    Turn(outline);
            }

            if (turns != 2)
                (page.Width, page.Height) = (height, width);
        }

        // ── The book ────────────────────────────────────────────────────────

        /// <summary>
        /// Makes the PDF or the EPUB (the file dialog says which): the text of every page that has some (its pictures are in it, where they stand) and, when asked, the photo of the page above it.
        /// The language of the book is the one most of its pages were scanned in.
        /// </summary>
        internal async Task SaveBookAsync()
        {
            if (_busy)
                return;

            FlushText();
            var withPhotos = IncludePhotosCheck.Checked;

            // With the photos every page that is not left out is in the book (a page with no text is just its photo); without them only the pages that have text or a picture
            var entries = _pages
                .Where(page => page.LeftOut != true && (withPhotos ? File.Exists(page.File) : ManuscriptText.HasContent(page)))
                .Select(page => withPhotos ? new BookEntry(page.File!) { BelowRtf = page.Rtf } : new BookEntry(page.File!) { AboveRtf = page.Rtf })
                .ToList();
            if (!_pages.Any(ManuscriptText.HasContent))
            {
                SetStatus(ManuscriptStrings.StatusNoText);
                return;
            }

            var path = AskBookPath is { } ask ? ask() : PickBookPath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            var format = string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase) ? BookFormat.Epub : BookFormat.Pdf;
            var options = new BookOptions(TitleBox.Text.Trim(), AuthorBox.Text.Trim(), ScanLanguages.Code(BookLanguage()));

            SetBusy(true);
            _progress.Maximum = 100;
            SetStatus(ManuscriptStrings.StatusMakingBook);
            string? tempFolder = null;
            var notCut = 0;
            try
            {
                if (withPhotos && CutPagesCheck.Checked && entries.Count > 0)
                {
                    if (CutterOverride is null && _ensureCutoutAsync is not null && !await _ensureCutoutAsync())
                    {
                        if (!IsDisposed)
                            SetStatus(ManuscriptStrings.StatusCutDeclined);
                        return;
                    }

                    tempFolder = Path.Combine(Path.GetTempPath(), "PhotoLikerCut_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);
                    (entries, notCut) = await CutPagesOutAsync(entries, tempFolder);
                    if (IsDisposed)
                        return;
                    SetStatus(ManuscriptStrings.StatusMakingBook);
                }

                var result = await BookBuilder.BuildAsync(
                    entries, format, options, PictureQuality.Good, path,
                    preparing: null, writing: null,
                    percent: value => { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => _progress.Value = Math.Clamp(value, 0, 100)); },
                    CancellationToken.None, withPhotos);

                MadeBookPath = result.Path;
                MadeBookPages = result.Pages;
                _work.OutputFolder = Path.GetDirectoryName(result.Path);
                _work.OutputFileName = Path.GetFileNameWithoutExtension(result.Path);
                if (!IsDisposed)
                    SetStatus(notCut > 0
                        ? string.Format(ManuscriptStrings.StatusBookMadeCutFormat, result.Path, result.Pages, notCut)
                        : string.Format(ManuscriptStrings.StatusBookMadeFormat, result.Path, result.Pages));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException or InvalidDataException
                or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or BadImageFormatException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(ManuscriptStrings.StatusBookFailedFormat, ex.Message));
            }
            finally
            {
                if (tempFolder is not null)
                {
                    try { Directory.Delete(tempFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }

                if (!IsDisposed)
                {
                    SetBusy(false);
                    ScheduleSave();
                }
            }
        }

        /// <summary>The language of the book: the one most of the pages that are in it were scanned in (the one that is picked, when none was scanned).</summary>
        private ScanLanguage BookLanguage() =>
            _pages
                .Where(page => page.LeftOut != true && ManuscriptText.HasContent(page) && page.GetLanguage() is not null)
                .GroupBy(page => page.GetLanguage()!.Value)
                .OrderByDescending(group => group.Count())
                .Select(group => (ScanLanguage?)group.Key)
                .FirstOrDefault() ?? Language;

        /// <summary>
        /// Stands in for the cutter of pages (a test, which has no model): called once for a book that cuts its pages out. The cutter it gives is let go of when the book is made.
        /// </summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<PhotoLiker.Manuscripts.Pages.PageCutter>? CutterOverride { get; set; }

        /// <summary>
        /// Each photo of the book becomes the sheet in it, straightened, as a JPEG in <paramref name="folder"/> (the photo itself is not touched); a photo that has no sheet that can be cut out stays as it is.
        /// Returns the entries for the book and how many photos were not cut.
        /// </summary>
        private async Task<(List<BookEntry> Entries, int NotCut)> CutPagesOutAsync(List<BookEntry> entries, string folder)
        {
            var progress = _progress;
            var cutter = await Task.Run(() => CutterOverride?.Invoke() ?? new PhotoLiker.Manuscripts.Pages.PageCutter(PhotoLiker.Effects.Models.ToolModels.Cutout.PathIn(_models.ModelsFolder), _models.Threads));
            try
            {
                var result = new List<BookEntry>();
                var notCut = 0;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (IsDisposed)
                        break;

                    SetStatus(string.Format(ManuscriptStrings.StatusCuttingFormat, i + 1, entries.Count));
                    progress.Value = Math.Min(100, i * 100 / entries.Count);
                    var entry = entries[i];
                    var target = Path.Combine(folder, $"{i:D4}.jpg");
                    var cut = await Task.Run(() =>
                    {
                        try
                        {
                            var decoded = WicPhotoDecoder.DecodeFull(File.ReadAllBytes(entry.Path));
                            var found = cutter.Find(new PhotoLiker.Drawing.Bitmap32(decoded.Width, decoded.Height, decoded.Bgra));
                            if (found.Page is null)
                                return false;

                            SaveJpeg(found.Page, target);
                            return true;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
                        {
                            return false; // the photo stays as it is
                        }
                    });

                    if (cut)
                    {
                        result.Add(new BookEntry(target) { AboveRtf = entry.AboveRtf, BelowRtf = entry.BelowRtf });
                    }
                    else
                    {
                        notCut++;
                        result.Add(entry);
                    }
                }

                return (result, notCut);
            }
            finally
            {
                cutter.Dispose();
            }
        }

        /// <summary>A picture as a JPEG file (quality 92: it is made smaller again for the book).</summary>
        private static void SaveJpeg(PhotoLiker.Drawing.Bitmap32 picture, string path)
        {
            using var bitmap = new Bitmap(picture.Width, picture.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, picture.Width, picture.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(picture.Bgra, 0, data.Scan0, picture.Bgra.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(encoder => encoder.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
            parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
            bitmap.Save(path, codec, parameters);
        }

        private string? PickBookPath()
        {
            using var dialog = new SaveFileDialog
            {
                Title = ManuscriptStrings.SaveBookTitle,
                Filter = $"{ManuscriptStrings.FilterPdf} (*.pdf)|*.pdf|{ManuscriptStrings.FilterEpub} (*.epub)|*.epub",
                AddExtension = true,
                DefaultExt = "pdf",
                OverwritePrompt = true,
                FileName = DefaultBookName(),
                InitialDirectory = !string.IsNullOrWhiteSpace(_work.OutputFolder) && Directory.Exists(_work.OutputFolder) ? _work.OutputFolder : _folder.Length > 0 && Directory.Exists(_folder) ? _folder : string.Empty,
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        private string DefaultBookName()
        {
            var name = !string.IsNullOrWhiteSpace(_work.OutputFileName) ? _work.OutputFileName! : !string.IsNullOrWhiteSpace(TitleBox.Text) ? TitleBox.Text.Trim() : "manuscript";
            foreach (var bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            return name;
        }

        // ── Events, keys, closing ───────────────────────────────────────────

        private void WireEvents()
        {
            AddPagesButton.Click += (_, _) => AddPages();
            RemovePageButton.Click += (_, _) => RemoveCurrentPage();
            MoveUpButton.Click += (_, _) => MoveCurrentPage(-1);
            MoveDownButton.Click += (_, _) => MoveCurrentPage(1);
            KindCombo.SelectedIndexChanged += (_, _) =>
            {
                FillLanguages(Language);
                if (!_applyingPage)
                    ScheduleSave();
            };
            LanguageCombo.SelectedIndexChanged += (_, _) =>
            {
                if (!_applyingPage)
                    ScheduleSave();
            };
            ScanPageButton.Click += async (_, _) => await ScanPageAsync();
            ScanAllButton.Click += async (_, _) => await ScanAllAsync();
            StopButton.Click += (_, _) => _scan?.Cancel();
            RotateLeftButton.Click += async (_, _) => await RotateCurrentAsync(-1);
            RotateRightButton.Click += async (_, _) => await RotateCurrentAsync(1);
            ShowLinesButton.CheckedChanged += (_, _) => View.ShowLines = ShowLinesButton.Checked;
            JoinLinesButton.Click += (_, _) => JoinLines();
            FindPicturesButton.Click += async (_, _) => await FindPicturesAsync();
            Editor.FixRequested += async (_, _) => await FixTextAsync();
            AiCombo.SelectedIndexChanged += (_, _) => OnAiPicked();
            AiServicesButton.Click += (_, _) => OpenAiServices();

            PageList.SelectedIndexChanged += OnPageListSelection;
            View.LineClicked += OnViewLineClicked;
            Editor.TextChanged += (_, _) =>
            {
                if (_loading)
                    return;

                _dirty = true;
                if (CurrentPage is not null)
                    UpdatePageItem(_current);
                ScheduleSave();
            };
            Editor.Box.SelectionChanged += OnEditorSelectionChanged;
            _thumbnails.Loaded += OnThumbnailLoaded;

            TitleBox.TextChanged += (_, _) => ScheduleSave();
            AuthorBox.TextChanged += (_, _) => ScheduleSave();
            IncludePhotosCheck.CheckedChanged += (_, _) =>
            {
                CutPagesCheck.Enabled = IncludePhotosCheck.Checked;
                ScheduleSave();
            };
            ScanPicturesCheck.CheckedChanged += (_, _) => ScheduleSave();
            CutPagesCheck.CheckedChanged += (_, _) => ScheduleSave();
            SaveBookButton.Click += async (_, _) => await SaveBookAsync();
            CloseButton.Click += (_, _) => Close();
            _saveTimer.Tick += (_, _) => SaveWork();
            _scrollTimer.Tick += (_, _) => CheckThumbnailsInSight();
            _scrollTimer.Start();

            FormClosing += (_, _) =>
            {
                _scan?.Cancel();
                SaveWork();
            };
            Shown += (_, _) =>
            {
                ShowPage();
                Editor.Focus();
            };
        }

        /// <summary>Ctrl+'+' and Ctrl+'-' turn the photo as they do in the main window; Ctrl+S makes the book.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.Control) != 0 && (keyData & Keys.Alt) == 0)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Oemplus or Keys.Add:
                        _ = RotateCurrentAsync(1);
                        return true;
                    case Keys.OemMinus or Keys.Subtract:
                        _ = RotateCurrentAsync(-1);
                        return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
