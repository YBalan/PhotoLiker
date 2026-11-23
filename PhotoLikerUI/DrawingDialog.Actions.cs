using System.ComponentModel;
using PhotoLiker.Core;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Effects.Models;
using PhotoLiker.Imaging;
using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>What the Draw window does with the picture: scanning it for text, saving it, and the keys. See <see cref="DrawingDialog"/>.</summary>
    internal sealed partial class DrawingDialog
    {
        private bool _busy;
        private bool _saving;
        private CancellationTokenSource? _scan;
        private TextScanner? _scanner;

        /// <summary>Asked where to save a copy: the file name that is suggested, the answer (null: not saved). A test answers it.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<string, string?> AskSaveAsPath { get; set; } = _ => null;

        /// <summary>Asked when the window is closed with changes that were not saved. A test answers it.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<DialogResult> AskSaveChanges { get; set; } = () => DialogResult.No;

        /// <summary>What reads the text of a picture (the picture, the frame of the Crop tool if there is one, the way to stop); null for the real models. A test puts one in that needs no model.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<Bitmap32, Rectangle?, CancellationToken, ScanResult>? ScanOverride { get; set; }

        /// <summary>Puts the real questions in place: the window is made first, since they need it as their owner.</summary>
        private void UseRealQuestions()
        {
            AskSaveAsPath = suggested =>
            {
                var own = PhotoFormats.ForPath(_photoPath) ?? PhotoFormat.Jpeg;
                var formats = new[] { own }.Concat(PhotoFormats.All.Where(format => format != own)).ToList();
                using var dialog = new SaveFileDialog
                {
                    Title = DrawingStrings.SaveAsTitle,
                    Filter = string.Join("|", formats.Select(PhotoFormats.Filter)),
                    FilterIndex = 1,
                    AddExtension = true,
                    DefaultExt = PhotoFormats.Extension(own).TrimStart('.'),
                    OverwritePrompt = true,
                    InitialDirectory = Path.GetDirectoryName(suggested) ?? string.Empty,
                    FileName = Path.GetFileName(suggested),
                };
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
            };
            AskSaveChanges = () => MessageBox.Show(this, string.Format(DrawingStrings.ConfirmSaveFormat, Path.GetFileName(_photoPath)), DrawingStrings.DialogTitle,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Canvas.ReadOnly = busy;
            _progress.Visible = busy;
            Bar.Enabled = !busy;
            UpdateButtons();
        }

        // ── Scan text ───────────────────────────────────────────────────────

        /// <summary>Whether a scan or a save is going on (the picture is not to be changed meanwhile).</summary>
        internal bool IsBusy => _busy;

        /// <summary>
        /// Finds the text in the picture (in the frame of the Crop tool, when there is one) and reads it: the models are asked for first (downloaded, the first time), then the picture is looked at off
        /// the window's thread. The lines found are marked on the picture and the text is shown at the side, to copy.
        /// </summary>
        internal async Task ScanAsync()
        {
            if (_busy)
                return;

            Canvas.CommitNote();
            SetBusy(true);
            SetStatus(DrawingStrings.StatusScanBusy);
            try
            {
                var ready = await _ensureModelsAsync();
                if (!ready || IsDisposed)
                {
                    if (!IsDisposed)
                        SetStatus(DrawingStrings.StatusScanDeclined);
                    return;
                }

                var finder = ToolModels.OcrFinder.PathIn(_models.ModelsFolder);
                var reader = ToolModels.OcrReader.PathIn(_models.ModelsFolder);
                _scan = new CancellationTokenSource();
                var cancel = _scan.Token;
                var picture = _session.Picture; // nothing changes it while the window is busy
                var area = Canvas.CropArea;

                var result = await Task.Run(() =>
                {
                    if (ScanOverride is { } stand)
                        return stand(picture, area, cancel);

                    NativeRuntime.EnsureOnnxRuntimeCanLoad();
                    _scanner ??= new TextScanner(finder, reader, _models.Threads);
                    return _scanner.Scan(picture, area, cancel);
                }, cancel);

                if (IsDisposed)
                    return;

                ShowScanned(result);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    SetStatus(DrawingStrings.StatusScanStopped);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Whatever went wrong (a model file that is damaged, a runtime that cannot load, something not thought of): the window goes on, and says so
                if (!IsDisposed)
                    SetStatus(string.Format(DrawingStrings.StatusScanFailedFormat, ex.Message));
            }
            finally
            {
                _scan?.Dispose();
                _scan = null;
                if (!IsDisposed)
                    SetBusy(false);
            }
        }

        private void ShowScanned(ScanResult result)
        {
            if (result.IsEmpty)
            {
                HideScan();
                SetStatus(DrawingStrings.StatusScanNothing);
                return;
            }

            ScanBox.Text = result.Text.Replace("\n", Environment.NewLine);
            ScanBox.SelectionStart = 0;
            ScanBox.SelectionLength = 0;
            ScanPanel.Visible = true;
            Canvas.ShowMarks(result.Lines.Select(line => line.Box).ToList());
            SetStatus(string.Format(DrawingStrings.StatusScanFoundFormat, result.Lines.Count));
        }

        /// <summary>The text of the scan, or the part of it that is picked, goes to the clipboard.</summary>
        internal void CopyScannedText()
        {
            var text = ScanBox.SelectionLength > 0 ? ScanBox.SelectedText : ScanBox.Text;
            if (text.Length == 0)
                return;

            try
            {
                Clipboard.SetText(text);
                SetStatus(DrawingStrings.StatusScanCopied);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                SetStatus(string.Format(DrawingStrings.StatusScanFailedFormat, ex.Message)); // another program holds the clipboard
            }
        }

        internal void HideScan()
        {
            ScanPanel.Visible = false;
            Canvas.ShowMarks(null);
        }

        // ── Saving ──────────────────────────────────────────────────────────

        /// <summary>Saves the picture over the photo. False when nothing was saved (nothing changed, the photo's format cannot be written, or the file could not be).</summary>
        internal async Task<bool> SaveAsync()
        {
            Canvas.CommitNote();
            if (_busy)
                return false;

            if (!HasUnsavedChanges)
            {
                SetStatus(DrawingStrings.StatusNothingToSave);
                return false;
            }

            if (PhotoFormats.ForPath(_photoPath) is null)
            {
                SetStatus(string.Format(DrawingStrings.StatusSaveOnlyAsFormat, Path.GetExtension(_photoPath).TrimStart('.').ToUpperInvariant()));
                return false;
            }

            return await WriteAsync(_photoPath);
        }

        /// <summary>Asks where to save a copy, and saves the picture there. False when nothing was saved.</summary>
        internal async Task<bool> SaveAsAsync()
        {
            Canvas.CommitNote();
            if (_busy)
                return false;

            var extension = PhotoFormats.ForPath(_photoPath) is null ? ".jpg" : Path.GetExtension(_photoPath);
            var suggested = Path.Combine(Path.GetDirectoryName(_photoPath) ?? string.Empty, Path.GetFileNameWithoutExtension(_photoPath) + " (edited)" + extension);
            if (AskSaveAsPath(suggested) is not { Length: > 0 } chosen)
                return false;

            return await WriteAsync(PhotoFormats.WithExtension(chosen, PhotoFormats.ForPath(_photoPath) ?? PhotoFormat.Jpeg));
        }

        private async Task<bool> WriteAsync(string destination)
        {
            SetBusy(true);
            _saving = true;
            SetStatus(DrawingStrings.StatusSaving);
            string? error;
            try
            {
                error = await Task.Run(() => WriteNow(destination));
            }
            finally
            {
                _saving = false;
                if (!IsDisposed)
                    SetBusy(false);
            }

            if (IsDisposed)
                return error is null;

            if (error is not null)
            {
                SetStatus(string.Format(DrawingStrings.StatusSaveFailedFormat, error));
                return false;
            }

            SetStatus(string.Format(SamePath(destination, _photoPath) ? DrawingStrings.StatusSavedOverFormat : DrawingStrings.StatusSavedFormat, destination));
            return true;
        }

        /// <summary>Writes the picture to the file and notes it as saved; the reason, when it could not be written (null: it was). Safe off the window's thread: nothing changes the picture meanwhile.</summary>
        private string? WriteNow(string destination)
        {
            var inPlace = SamePath(destination, _photoPath);
            try
            {
                if (inPlace && TryTurnLosslessly(out var wrote))
                {
                    // Only turned: the photo is as it was with the turn written into its Exif, so everything else it has stays (all of the Exif, the date taken, the dates of the file)
                    if (wrote)
                        PhotoWasChanged = true;
                    SavedPath = destination;
                    _savedVersion = _version;
                    return null;
                }

                var picture = _session.Picture;
                PhotoConverter.SaveEdited(_photoPath, destination, picture.Width, picture.Height, picture.Bgra);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return ex.Message;
            }

            SavedPath = destination;
            if (inPlace)
            {
                PhotoWasChanged = true;
                _pixelsWritten = true; // the file holds this picture as pixels now (what was turned is in them)
            }
            _savedVersion = _version;
            return null;
        }

        /// <summary>The quarter turns (clockwise) the photo file has been given by this window, by its Exif alone (as the Rotate buttons of the main window do it).</summary>
        private int _turnsWritten;

        /// <summary>True once the window has written the pixels of the picture over the photo (a drawing, a crop, a photo that is not a JPEG); a turn is then in the pixels, and no longer a flag to add to.</summary>
        private bool _pixelsWritten;

        /// <summary>
        /// The quarter turns this window wrote into the Exif of the photo when that was all it did to it (null when it wrote pixels, or nothing): the main window turns the faces of the index by them,
        /// as for its own Rotate buttons, instead of taking the photo for another picture.
        /// </summary>
        internal int? LosslessTurns => PhotoWasChanged && !_pixelsWritten ? _turnsWritten : null;

        /// <summary>
        /// A JPEG that was only turned (no stroke, no note, no crop, nothing written over it before) is not written again: the turn goes into its Exif, which changes the orientation and nothing else, so the
        /// picture is not coded a second time and everything else the photo has stays as it is, and so do the dates of its file. False when it was not that (or the Exif has no room for the turn): the
        /// pixels are written instead. <paramref name="wrote"/> is false when the file was already turned as far as the picture is.
        /// </summary>
        private bool TryTurnLosslessly(out bool wrote)
        {
            wrote = false;
            if (_pixelsWritten || !JpegOrientation.IsJpeg(_photoPath) || _session.Steps.Any(step => step is not RotateStep))
                return false;

            var total = Quarter(_session.Steps.Cast<RotateStep>().Sum(step => step.QuarterTurns));
            var more = Quarter(total - _turnsWritten);
            if (more != 0)
            {
                try
                {
                    JpegOrientation.TurnClockwise(_photoPath, more);
                }
                catch (InvalidDataException)
                {
                    return false; // a damaged Exif, or no room in it for the turn: the pixels are written
                }

                wrote = true;
            }

            _turnsWritten = total;
            return true;
        }

        private static int Quarter(int turns) => ((turns % 4) + 4) % 4;

        private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        // ── Keys and closing ────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.S:
                    _ = SaveAsync();
                    return true;
                case Keys.Control | Keys.Shift | Keys.S:
                    _ = SaveAsAsync();
                    return true;
            }

            if (Canvas.IsTyping)
            {
                // The note has its own keys (Ctrl+Z undoes its typing); only the ends of it are the window's
                if (keyData == (Keys.Control | Keys.Enter))
                {
                    Canvas.CommitNote();
                    return true;
                }

                if (keyData == Keys.Escape)
                {
                    Canvas.CancelNote();
                    return true;
                }

                return base.ProcessCmdKey(ref msg, keyData);
            }

            // Ctrl and the plus or the minus turn the picture, as they turn a photo in the main window
            if ((keyData & Keys.Control) != 0 && (keyData & Keys.Alt) == 0)
            {
                var turn = (keyData & Keys.KeyCode) switch
                {
                    Keys.Oemplus or Keys.Add => 1,
                    Keys.OemMinus or Keys.Subtract => -1,
                    _ => 0,
                };
                if (turn != 0)
                {
                    Rotate(turn);
                    return true;
                }
            }

            switch (keyData)
            {
                case Keys.Control | Keys.Z:
                    Undo();
                    return true;
                case Keys.Control | Keys.Y or Keys.Control | Keys.Shift | Keys.Z:
                    Redo();
                    return true;
                case Keys.Enter when _tool == DrawTool.Crop && Canvas.CropArea is not null:
                    ApplyCrop();
                    return true;
                case Keys.Escape:
                    if (_scan is not null)
                        _scan.Cancel();
                    else if (_tool == DrawTool.Crop && Canvas.CropArea is not null)
                        Canvas.CancelCrop();
                    else if (!_busy)
                        Close();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            _scan?.Cancel();
            if (_saving)
            {
                e.Cancel = true; // the file is being written: the window waits for it
                return;
            }

            Canvas.CommitNote();
            if (!HasUnsavedChanges)
                return;

            switch (AskSaveChanges())
            {
                case DialogResult.Cancel:
                    e.Cancel = true;
                    break;

                case DialogResult.Yes:
                    var target = PhotoFormats.ForPath(_photoPath) is null ? AskSaveAsPath(Path.ChangeExtension(_photoPath, ".jpg")) : _photoPath;
                    if (target is null)
                    {
                        e.Cancel = true; // no place was picked for a photo that cannot be written over: the window stays
                        break;
                    }

                    if (WriteNow(PhotoFormats.WithExtension(target, PhotoFormat.Jpeg)) is { } error)
                    {
                        MessageBox.Show(this, string.Format(DrawingStrings.StatusSaveFailedFormat, error), DrawingStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        e.Cancel = true;
                    }
                    break;
            }
        }
    }
}
