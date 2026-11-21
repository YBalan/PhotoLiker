using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using PhotoLiker.TraceBitmap;

namespace PhotoLikerUI
{
    /// <summary>The part of the Trace Bitmap window that works: reads the photo, traces it on another thread (and stops that when something is changed), and writes the file.</summary>
    internal sealed partial class TraceBitmapDialog
    {
        private PixelImage? _image;
        private TraceResult? _trace;
        private TraceSettings? _tracedSettings;
        private int _tracedDetail;
        private CancellationTokenSource? _cancellation;
        private int _generation;
        private bool _failed;

        /// <summary>Asks where to save (the Save window of Windows); a test answers it. Gets the title, the kinds of file, the number of the kind that is picked at first (from 1), the folder and the name it begins with.</summary>
        internal delegate (string Path, int FilterIndex)? SavePathPrompt(string title, string filter, int filterIndex, string folder, string fileName);

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal SavePathPrompt? AskSavePath { get; set; }

        /// <summary>What was traced, once something was.</summary>
        internal TraceResult? CurrentTrace => _trace;

        /// <summary>True while the trace is being made.</summary>
        internal bool IsTracing { get; private set; }

        /// <summary>True while the photo is being read.</summary>
        internal bool IsReading { get; private set; }

        internal string StatusText => _statusLabel.Text;

        internal string CountsText => _countsLabel.Text;

        /// <summary>True when what is on the screen is not the trace of the settings that are in the window now (or nothing was traced yet).</summary>
        internal bool IsStale =>
            _trace is null || _tracedSettings is null || _tracedDetail != Detail ||
            JsonSerializer.Serialize(_tracedSettings) != JsonSerializer.Serialize(ReadTrace().Normalized());

        private void SetStatus(string text, bool failure = false)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = failure ? Color.FromArgb(220, 80, 70) : _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
        }

        // ── Reading the photo ───────────────────────────────────────────────

        private async Task OpenAsync()
        {
            await ReadPhotoAsync();
            if (_image is not null)
                await TraceNowAsync();
        }

        private async Task DetailChangedAsync()
        {
            if (_loading || _image is null)
                return;

            await ReadPhotoAsync();
            if (_image is not null)
            {
                SetStatus(TraceStrings.StatusStale);
                if (LiveCheck.Checked)
                    await TraceNowAsync();
            }
        }

        /// <summary>Reads the photo as big as the detail asks (not bigger than it is), upright.</summary>
        private async Task ReadPhotoAsync()
        {
            IsReading = true;
            SetStatus(TraceStrings.StatusReading);
            var detail = Detail;
            try
            {
                var photo = await Task.Run(() => PhotoLiker.Imaging.WicPhotoDecoder.Decode(File.ReadAllBytes(_photoPath), detail));
                if (IsDisposed)
                    return;

                _image = new PixelImage(photo.Width, photo.Height, photo.Bgr);
                var old = Preview.Photo;
                Preview.Photo = ToBitmap(photo);
                old?.Dispose();
                SetDefaultSize();
                SetStatus(string.Empty);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!IsDisposed)
                {
                    _failed = true;
                    SetStatus(string.Format(TraceStrings.StatusPhotoFailedFormat, ex.Message), failure: true);
                }
            }
            finally
            {
                IsReading = false;
            }
        }

        private static Bitmap ToBitmap(PhotoLiker.Imaging.DecodedPhoto photo)
        {
            var bitmap = new Bitmap(photo.Width, photo.Height, PixelFormat.Format24bppRgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, photo.Width, photo.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var rowBytes = photo.Width * 3;
                for (var y = 0; y < photo.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(photo.Bgr, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        // ── Tracing ─────────────────────────────────────────────────────────

        /// <summary>
        /// Traces the photo with the settings that are in the window. A trace that is still going is stopped first. Done when the trace is made (or stopped, or failed); what is shown is the newest one.
        /// </summary>
        internal async Task TraceNowAsync()
        {
            if (_image is null || IsDisposed)
                return;

            _liveTimer.Stop();
            _cancellation?.Cancel();
            var cancellation = _cancellation = new CancellationTokenSource();
            var generation = ++_generation;
            var image = _image;
            var settings = ReadTrace().Normalized();
            var detail = Detail;

            IsTracing = true;
            _progress.Value = 0;
            _progress.Visible = true;
            SetStatus(TraceStrings.StatusTracing);
            var progress = new Progress<double>(value =>
            {
                if (generation == _generation && !IsDisposed)
                    _progress.Value = Math.Clamp((int)(value * _progress.Maximum), 0, _progress.Maximum);
            });

            try
            {
                var clock = Stopwatch.StartNew();
                var result = await Task.Run(() => BitmapTracer.Trace(image, settings, progress, cancellation.Token), cancellation.Token);
                if (generation != _generation || IsDisposed)
                    return;

                _trace = result;
                _tracedSettings = settings;
                _tracedDetail = detail;
                _elapsed = clock.ElapsedMilliseconds;
                Preview.Trace = result;
                SetStatus(StatusOfTrace());
                _countsLabel.Text = CountsOfTrace(result);
            }
            catch (OperationCanceledException)
            {
                // Another trace took its place, or the window was closed
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (generation == _generation && !IsDisposed)
                    SetStatus(string.Format(TraceStrings.StatusTraceFailedFormat, ex.Message), failure: true);
            }
            finally
            {
                if (generation == _generation && !IsDisposed)
                {
                    IsTracing = false;
                    _progress.Visible = false;
                }
            }
        }

        private long _elapsed;

        private string StatusOfTrace() =>
            _trace is { PathCount: 0 } ? TraceStrings.StatusNothingTraced : string.Format(TraceStrings.StatusTracedFormat, _trace?.PathCount ?? 0, _trace?.NodeCount ?? 0, _elapsed);

        private static string CountsOfTrace(TraceResult trace) =>
            string.Format(TraceStrings.StatusDrawingFormat, trace.PathCount, trace.NodeCount, trace.Layers.Count);

        // ── Saving ──────────────────────────────────────────────────────────

        private static string KindsOfFile => $"{TraceStrings.FilterSvg}|*.svg|{TraceStrings.FilterDxf}|*.dxf";

        /// <summary>The kind of file a name says: by its extension, or else by the number of the kind that was picked in the list.</summary>
        internal static TraceFileFormat FormatOf(string path, int filterIndex) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".svg" => TraceFileFormat.Svg,
                ".dxf" => TraceFileFormat.Dxf,
                _ => filterIndex == 2 ? TraceFileFormat.Dxf : TraceFileFormat.Svg,
            };

        /// <summary>Asks where to save and saves the drawing (the trace is made first when the settings were changed since it was). True when a file was written.</summary>
        internal async Task<bool> SaveAsync()
        {
            if (_image is null)
                return false;

            if (IsStale)
                await TraceNowAsync();
            if (_trace is not { } trace || IsStale)
                return false;

            var chosen = AskPath();
            if (chosen is not var (path, filterIndex))
                return false;

            var format = FormatOf(path, filterIndex);
            if (Path.GetExtension(path).Length == 0)
                path += format == TraceFileFormat.Dxf ? ".dxf" : ".svg";

            var (width, height) = DrawingSize;
            var accuracy = (double)AccuracyNumeric.Value;
            try
            {
                var text = await Task.Run(() => format == TraceFileFormat.Dxf ? DxfWriter.Write(trace, width, height, accuracy) : SvgWriter.Write(trace, width, height));
                await File.WriteAllTextAsync(path, text, format == TraceFileFormat.Dxf ? Encoding.ASCII : new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                SetStatus(string.Format(TraceStrings.StatusSaveFailedFormat, ex.Message), failure: true);
                return false;
            }

            _loading = true;
            FormatCombo.SelectedIndex = format == TraceFileFormat.Dxf ? 1 : 0;
            _loading = false;
            UpdateEnabled();
            SavedPath = path;
            SetStatus(string.Format(TraceStrings.StatusSavedFormat, path));
            return true;
        }

        private (string Path, int FilterIndex)? AskPath()
        {
            var name = Path.GetFileNameWithoutExtension(_photoPath) + (Format == TraceFileFormat.Dxf ? ".dxf" : ".svg");
            var folder = Path.GetDirectoryName(_photoPath) ?? string.Empty;
            var index = Format == TraceFileFormat.Dxf ? 2 : 1;
            if (AskSavePath is { } prompt)
                return prompt(TraceStrings.SaveTitle, KindsOfFile, index, folder, name);

            using var dialog = new SaveFileDialog
            {
                Title = TraceStrings.SaveTitle,
                Filter = KindsOfFile,
                FilterIndex = index,
                InitialDirectory = Directory.Exists(folder) ? folder : string.Empty,
                FileName = name,
                OverwritePrompt = true,
                CheckPathExists = true,
                AddExtension = true,
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? (dialog.FileName, dialog.FilterIndex) : null;
        }

        // ── Closing ─────────────────────────────────────────────────────────

        /// <summary>Whether the photo could not be read, which leaves the window with nothing to trace.</summary>
        internal bool PhotoFailed => _failed;

        private void OnClosing()
        {
            _liveTimer.Stop();
            _cancellation?.Cancel();
            ReadSettings().Save(_settingsFile);
        }
    }
}
