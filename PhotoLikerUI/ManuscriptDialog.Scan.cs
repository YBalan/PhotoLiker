using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Manuscripts;
using PhotoLiker.PhotoBook;

namespace PhotoLikerUI
{
    /// <summary>
    /// Scanning in the Scan manuscripts window: one page or all the pages that have no text, the models, what is done with what was read (the text is laid out like the page, with its pictures in it), the
    /// boxes on the photo and the rows of the text that go with them, and joining the lines of a paragraph.
    /// </summary>
    internal sealed partial class ManuscriptDialog
    {
        private bool _syncing;

        // The rows of the text of the page that is open (a row is the lines of the photo that are side by side on it), made from the lines of the view
        private IReadOnlyList<LayoutRow>? _rows;
        private IReadOnlyList<PhotoLiker.Drawing.Ocr.TextLine>? _rowsFor;

        /// <summary>Stands in for the scanner (a test, which has no models): the picture, the kind of writing and the language go in, the lines come out.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<Bitmap32, ScanKind, ScanLanguage, CancellationToken, IProgress<(int Done, int Total)>?, ScanResult>? ScanOverride { get; set; }

        /// <summary>Asked before a page that has text is scanned again, which replaces it; a test answers it.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<bool>? AskReplaceText { get; set; }

        /// <summary>The model of the handwriting a language is read with: the Russian one for Russian, the Ukrainian one for the others (see <see cref="ScanLanguages.HandwritingModelOf"/>).</summary>
        internal static PhotoLiker.Effects.Models.ToolModel HandwritingModel(ScanLanguage language) =>
            ScanLanguages.HandwritingModelOf(language) == ScanLanguage.Russian ? PhotoLiker.Effects.Models.ToolModels.HandwritingRussian : PhotoLiker.Effects.Models.ToolModels.HandwritingUkrainian;

        // ── Scanning ────────────────────────────────────────────────────────

        /// <summary>Scans the page that is open (the Scan page button).</summary>
        internal async Task ScanPageAsync()
        {
            if (_busy || CurrentPage is not { } page)
                return;

            FlushText();
            if (ManuscriptText.HasText(page) && !(AskReplaceText?.Invoke() ?? ConfirmReplace()))
                return;

            await ScanPagesAsync([_current]);
        }

        /// <summary>Scans every page that has no text yet, one after the other (the Scan all pages button); pages that have text, and pages that are left out, are not touched.</summary>
        internal async Task ScanAllAsync()
        {
            if (_busy)
                return;

            FlushText();
            var todo = Enumerable.Range(0, _pages.Count).Where(index => _pages[index].LeftOut != true && !ManuscriptText.HasText(_pages[index])).ToList();
            if (todo.Count == 0)
            {
                SetStatus(ManuscriptStrings.StatusScanAllNothing);
                return;
            }

            await ScanPagesAsync(todo);
        }

        private bool ConfirmReplace() =>
            MessageBox.Show(this, ManuscriptStrings.ConfirmReplaceText, ManuscriptStrings.DialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        private async Task ScanPagesAsync(IReadOnlyList<int> indexes)
        {
            var kind = Kind;
            if (kind == ScanKind.AiService)
            {
                await ScanWithAiAsync(indexes); // the photo goes to an AI service: no models of this computer, no boxes
                return;
            }

            var language = Language;
            var source = new CancellationTokenSource();
            _scan = source;
            SetBusy(true);
            var read = 0;
            var empty = 0;
            var pictureCount = 0;
            try
            {
                // The models: asked for (and downloaded) when they are not on this computer
                if (ScanOverride is null && !await _ensureModelsAsync(kind, language))
                {
                    if (!IsDisposed)
                        SetStatus(ManuscriptStrings.StatusScanDeclined);
                    return;
                }

                if (IsDisposed)
                    return;

                // The pictures are looked for too when the person wants them (and was not asked and said no already): the models are asked for once, a no is a scan of the text only
                var findPictures = ScanPicturesCheck.Checked && !_picturesDeclined;
                if (findPictures && !await EnsurePictureModelsAsync())
                {
                    findPictures = false;
                    if (IsDisposed)
                        return;
                }

                StopButton.Enabled = true;
                foreach (var index in indexes)
                {
                    source.Token.ThrowIfCancellationRequested();
                    if (index >= _pages.Count)
                        break;

                    var page = _pages[index];
                    SetStatus(string.Format(ManuscriptStrings.StatusReadingFormat, index + 1));
                    if (!File.Exists(page.File))
                    {
                        SetStatus(string.Format(ManuscriptStrings.PageMissingFormat, page.File));
                        continue;
                    }

                    var progress = new Progress<(int Done, int Total)>(step =>
                    {
                        if (IsDisposed)
                            return;
                        SetStatus(string.Format(ManuscriptStrings.StatusReadingLineFormat, index + 1, step.Done, step.Total));
                        _progress.Maximum = Math.Max(1, step.Total * indexes.Count);
                        _progress.Value = Math.Min(_progress.Maximum, read * step.Total + step.Done);
                    });

                    var result = await Task.Run(() =>
                    {
                        var decoded = WicPhotoDecoder.DecodeFull(File.ReadAllBytes(page.File!));
                        var bitmap = new Bitmap32(decoded.Width, decoded.Height, decoded.Bgra);
                        var scan = RunScan(bitmap, kind, language, source.Token, progress);
                        return (Result: scan, Pictures: findPictures ? TryFindPictures(bitmap, source.Token) : null, bitmap.Width, bitmap.Height);
                    }, source.Token);

                    if (IsDisposed)
                        return;

                    ApplyScan(index, result.Result, result.Width, result.Height, kind, language, result.Pictures);
                    pictureCount += result.Pictures?.Cut.Count ?? 0;
                    read++;
                    if (result.Result.IsEmpty)
                        empty++;
                }

                string done;
                if (indexes.Count == 1)
                {
                    var page = _pages[indexes[0]];
                    done = page.Lines is { Count: > 0 } lines
                        ? string.Format(ManuscriptStrings.StatusScannedFormat, indexes[0] + 1, lines.Count, (int)Math.Round(100 * lines.Average(line => line.Confidence)))
                        : ManuscriptStrings.StatusScanNothing;
                }
                else
                {
                    done = string.Format(ManuscriptStrings.StatusScanAllDoneFormat, read, empty);
                }

                // What the pictures came to, when they were looked for; or why they were not
                if (pictureCount > 0)
                    done += " " + string.Format(ManuscriptStrings.StatusPicturesInTextFormat, pictureCount);
                else if (ScanPicturesCheck.Checked && _picturesDeclined)
                    done += " " + ManuscriptStrings.StatusPicturesDeclined;
                SetStatus(done);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    SetStatus(ManuscriptStrings.StatusScanStopped);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or FileNotFoundException or UnauthorizedAccessException or InvalidOperationException
                or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or BadImageFormatException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(ManuscriptStrings.StatusScanFailedFormat, ex.Message));
            }
            finally
            {
                _scan = null;
                source.Dispose();
                ReleasePictureFinder();
                if (!IsDisposed)
                {
                    SetBusy(false);
                    SaveWork();
                }
            }
        }

        /// <summary>Reads a picture: with the stand-in of a test, or with the scanner of the kind of writing and the language (made when it is first needed, and kept while they stay the same).</summary>
        private ScanResult RunScan(Bitmap32 bitmap, ScanKind kind, ScanLanguage language, CancellationToken cancel, IProgress<(int Done, int Total)> progress)
        {
            if (ScanOverride is { } standIn)
                return standIn(bitmap, kind, language, cancel, progress);

            if (_scanner is not { } scanner || scanner.Kind != kind || scanner.Language != language)
            {
                _scanner?.Dispose();
                _scanner = null;
                SetStatusFromAnyThread(ManuscriptStrings.StatusPreparingModel);

                var folder = _models.ModelsFolder;
                var finder = PhotoLiker.Effects.Models.ToolModels.OcrFinder.PathIn(folder);
                scanner = kind == ScanKind.Handwriting
                    ? ManuscriptScanner.Handwriting(finder, HandwritingModel(language).PathIn(folder), language, _models.Threads)
                    : ManuscriptScanner.Printed(finder, PhotoLiker.Effects.Models.ToolModels.OcrReader.PathIn(folder), language, _models.Threads);
                _scanner = scanner;
            }

            return scanner.Scan(bitmap, null, cancel, progress);
        }

        private void SetStatusFromAnyThread(string text)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(() => SetStatus(text));
        }

        /// <summary>
        /// Takes what a scan read into the page: its lines (to draw their boxes), the text (the lines one to a line), and when it was done. A scan that found nothing leaves the text of the page as it is.
        /// </summary>
        private void ApplyScan(int index, ScanResult result, int width, int height, ScanKind kind, ScanLanguage language, PictureSearch? pictures)
        {
            var page = _pages[index];
            page.Kind = kind.ToString();
            page.Language = language.ToString();
            page.ScannedUtc = DateTime.UtcNow;
            page.Width = width;
            page.Height = height;

            // The text is laid out like the page (rows, indents, tabs, blank lines), as a document; a scan that found nothing leaves the text of the page as it is
            IReadOnlyList<LayoutRow> rows = [];
            RichDocument document;
            if (result.IsEmpty)
            {
                page.Lines = null;
                document = RtfParser.Parse(page.Rtf);
            }
            else
            {
                page.Lines = [.. result.Lines.Select(StoredScanLine.From)];
                rows = ManuscriptLayout.Rows([.. result.Lines], width);
                page.Scanned = ManuscriptLayout.TextOf(rows);
                document = ManuscriptLayout.ToDocument(rows);
            }

            if (pictures is not null)
            {
                // The pictures that were found go between its rows, where they are on the page
                page.Pictures = [.. pictures.Outlines.Select(StoredPicture.From)];
                page.Rtf = RtfWith(document, pictures, rows, width);
            }
            else if (!result.IsEmpty)
            {
                page.Rtf = RtfWriter.Write(document);
            }

            UpdatePageItem(index);
            if (index != _current)
                return;

            ShowText(page);
            View.Lines = result.IsEmpty ? [] : [.. result.Lines];
            if (pictures is not null)
                View.Pictures = [.. pictures.Outlines.Select(picture => picture.Outline)];
            ShowLinesInEditor();
        }

        // ── The lines of the photo and the lines of the text ───────────────

        /// <summary>The rows of the text of the page that is open: the lines of its photo that are side by side on it are one row (see <see cref="ManuscriptLayout"/>).</summary>
        private IReadOnlyList<LayoutRow> CurrentRows()
        {
            var lines = View.Lines;
            if (_rows is null || !ReferenceEquals(_rowsFor, lines))
            {
                _rows = ManuscriptLayout.Rows(lines, CurrentPage?.Width ?? 0);
                _rowsFor = lines;
            }

            return _rows;
        }

        /// <summary>The rows of the photo and the lines of the text go together while the text has a line that is not blank for each row, as a scan makes it (the pictures and the blank lines are not lines).</summary>
        private bool LinesAligned() => View.Lines.Count > 0 && ManuscriptText.LineSpans(Editor.PlainText).Count == CurrentRows().Count;

        private void OnViewLineClicked(int line)
        {
            if (_syncing || !LinesAligned())
                return;

            var rows = CurrentRows();
            var row = line >= 0 && line < View.Lines.Count ? ManuscriptLayout.RowOfLine(rows, View.Lines.Count)[line] : -1;
            var spans = ManuscriptText.LineSpans(Editor.PlainText);
            if (row < 0 || row >= spans.Count)
                return;

            var span = spans[row];

            _syncing = true;
            try
            {
                Editor.Box.Focus();
                Editor.Box.Select(span.Start, span.Length);
                Editor.Box.ScrollToCaret();
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnEditorSelectionChanged(object? sender, EventArgs e)
        {
            if (_loading || _syncing)
                return;

            ShowLinesInEditor();
        }

        /// <summary>Selects, on the photo, the line that the caret of the text is in.</summary>
        private void ShowLinesInEditor()
        {
            if (View.Lines.Count == 0)
                return;

            _syncing = true;
            try
            {
                // The first box of the row the caret is in
                var row = LinesAligned() ? ManuscriptText.LineAt(ManuscriptText.LineSpans(Editor.PlainText), Editor.Box.SelectionStart) : -1;
                View.SelectedLine = row >= 0 && row < CurrentRows().Count ? CurrentRows()[row].Lines[0] : -1;
            }
            finally
            {
                _syncing = false;
            }
        }

        // ── Joining lines ───────────────────────────────────────────────────

        /// <summary>Joins the lines of the paragraphs of the selected text (or of all of it): see <see cref="ManuscriptText.JoinEdits"/>. What is done to the text is done in the editor, so the look of the text stays.</summary>
        internal void JoinLines()
        {
            var box = Editor.Box;
            var selected = box.SelectionLength > 0;
            var edits = ManuscriptText.JoinEdits(box.Text, selected ? box.SelectionStart : 0, selected ? box.SelectionLength : -1);
            if (edits.Count == 0)
            {
                SetStatus(ManuscriptStrings.StatusNoLinesToJoin);
                return;
            }

            // From the last change to the first, so that the places of the others are still right; one Undo takes all of them back
            using (Editor.UndoStep())
            {
                foreach (var edit in edits)
                {
                    box.Select(edit.Index, edit.Length);
                    box.SelectedText = edit.Replacement;
                }
            }

            box.Select(Math.Min(edits[^1].Index, box.TextLength), 0);
            SetStatus(string.Format(ManuscriptStrings.StatusLinesJoinedFormat, edits.Count));
        }
    }
}
