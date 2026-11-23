using System.ComponentModel;
using System.Drawing.Imaging;
using System.Text.Json;
using PhotoLiker.Effects.Models;
using PhotoLiker.Imaging;
using PhotoLiker.Search;
using PhotoLiker.Stickers;

namespace PhotoLikerUI
{
    /// <summary>
    /// The part of the Telegram Sticker window that works: reads the photo, finds its subject (the model is asked for, and downloaded, the first time), makes the sticker and the file it will be saved as on
    /// another thread (and stops that when something is changed), and writes the file.
    /// </summary>
    internal sealed partial class StickerDialog
    {
        /// <summary>The longer side the photo is read at: the cutout and the sticker are made from this, and a sticker is 512 pixels, so there is room to spare for a subject that is a third of the photo.</summary>
        internal const int PhotoSide = 1600;

        private readonly EffectModels _models;
        private readonly Func<Task<bool>> _ensureModelAsync;

        private PhotoPixels? _photo;
        private SubjectMap? _map;
        private float[]? _mask;
        private string? _maskKey;
        private StickerPicture? _sticker;
        private EncodedSticker? _encoded;
        private string? _madeKey;
        private CancellationTokenSource? _cancellation;
        private int _generation;
        private bool _failed;
        private bool _cutoutFallback;   // the window went to the whole picture by itself (no model): that is not what the person chose, so it is not kept
        private string? _notice;   // what the window has to say once the sticker is made (why the whole picture was used), until the person asks for the subject again

        /// <summary>Asks where to save (the Save window of Windows); a test answers it. Gets the title, the kinds of file, the number of the kind that is picked at first (from 1), the folder and the name it begins with.</summary>
        internal delegate (string Path, int FilterIndex)? SavePathPrompt(string title, string filter, int filterIndex, string folder, string fileName);

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal SavePathPrompt? AskSavePath { get; set; }

        /// <summary>Finds the subject of a photo, in place of the model (a test gives one that needs no model; null is the real thing).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<PhotoPixels, SubjectMap>? FindSubject { get; set; }

        /// <summary>The ffmpeg that writes the WebP; the one that comes with the program when null.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string? FfmpegPath { get; set; }

        /// <summary>The photo as it was read (at most <see cref="PhotoSide"/> pixels on its longer side).</summary>
        internal PhotoPixels? Photo => _photo;

        /// <summary>The cutout that is shown, the size of the photo that was read.</summary>
        internal float[]? CurrentMask => _mask;

        /// <summary>The picture of the sticker that is shown.</summary>
        internal StickerPicture? CurrentSticker => _sticker;

        /// <summary>The file the sticker would be saved as, as the settings are now.</summary>
        internal EncodedSticker? CurrentFile => _encoded;

        /// <summary>True while the photo is being read.</summary>
        internal bool IsReading { get; private set; }

        /// <summary>True while the model looks for the subject.</summary>
        internal bool IsFinding { get; private set; }

        /// <summary>True while the sticker is made.</summary>
        internal bool IsMaking { get; private set; }

        /// <summary>Whether the photo could not be read, which leaves the window with nothing to make a sticker of.</summary>
        internal bool PhotoFailed => _failed;

        internal bool IsBusy => IsReading || IsFinding || IsMaking;

        internal string StatusText => _statusLabel.Text;

        internal string InfoText => _infoLabel.Text;

        /// <summary>True when what is on the screen is not the sticker of the settings that are in the window now (or nothing was made yet).</summary>
        internal bool IsStale => _sticker is null || _madeKey != KeyOf(ReadSettings());

        private void SetStatus(string text, bool failure = false)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = failure ? Color.FromArgb(220, 80, 70) : _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
        }

        /// <summary>Says something that stays in the status line after the sticker is made (making it would otherwise wipe it).</summary>
        private void Notice(string text)
        {
            _notice = text;
            SetStatus(text, failure: true);
        }

        private void SetBusy(bool busy, bool marquee = false)
        {
            _progress.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            _progress.Visible = busy;
        }

        // ── Opening ─────────────────────────────────────────────────────────

        private async Task OpenAsync()
        {
            await ReadPhotoAsync();
            if (_photo is not null)
                await FindAndMakeAsync();
        }

        /// <summary>Reads the photo, upright and not bigger than <see cref="PhotoSide"/> on its longer side.</summary>
        private async Task ReadPhotoAsync()
        {
            IsReading = true;
            SetStatus(StickerStrings.StatusReading);
            try
            {
                var photo = await Task.Run(() => WicPhotoDecoder.Decode(File.ReadAllBytes(_photoPath), PhotoSide));
                if (IsDisposed)
                    return;

                _photo = new PhotoPixels(photo.Width, photo.Height, photo.Bgr);
                PhotoPane.Picture = ToBitmap(photo.Width, photo.Height, photo.Bgr, 3);
                PhotoPane.EmptyText = string.Empty;
                SetStatus(string.Empty);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!IsDisposed)
                {
                    _failed = true;
                    PhotoPane.EmptyText = string.Empty;
                    SetStatus(string.Format(StickerStrings.StatusPhotoFailedFormat, ex.Message), failure: true);
                }
            }
            finally
            {
                IsReading = false;
            }
        }

        /// <summary>A bitmap of 24 bits from B, G, R bytes (<paramref name="bytesPerPixel"/> 3) or one of 32 bits from B, G, R, A (4) with a straight alpha.</summary>
        internal static Bitmap ToBitmap(int width, int height, byte[] pixels, int bytesPerPixel)
        {
            var format = bytesPerPixel == 4 ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb;
            var bitmap = new Bitmap(width, height, format);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, format);
            try
            {
                var rowBytes = width * bytesPerPixel;
                for (var y = 0; y < height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        // ── Finding the subject ─────────────────────────────────────────────

        private async Task CutoutChangedAsync()
        {
            if (_loading)
                return;

            UpdateEnabled();
            _cutoutFallback = false; // the person picked it: from here on the choice is theirs
            if (_photo is null || IsReading)
                return;

            // Picking "Find the subject" is a request, so the model is asked for again even if it was said no to (the question is the main window's: see MainForm.Sticker)
            await FindAndMakeAsync();
        }

        /// <summary>Finds the subject when the cutout is to be the model's and it was not found yet; falls back to the whole picture when the model is not to be had; then makes the sticker.</summary>
        internal async Task FindAndMakeAsync()
        {
            _notice = null;
            if (Pick(CutoutCombo, CutoutMode.Detect) == CutoutMode.Detect && _map is null && !await FindSubjectAsync())
            {
                if (IsDisposed)
                    return;

                // Without the model the window still has a sticker to offer: the whole picture
                _loading = true;
                Select(CutoutCombo, CutoutMode.WholePicture);
                _loading = false;
                _cutoutFallback = true;
                UpdateEnabled();
            }

            if (!IsDisposed)
                await MakeAsync();
        }

        /// <summary>Makes sure the model is there (asking to download it the first time) and lets it look at the photo. True when the subject was found.</summary>
        internal async Task<bool> FindSubjectAsync()
        {
            if (_photo is not { } photo)
                return false;
            if (_map is not null)
                return true;

            IsFinding = true;
            SetBusy(true, marquee: true);
            try
            {
                if (!await _ensureModelAsync())
                {
                    if (!IsDisposed)
                        Notice(StickerStrings.StatusModelMissing);
                    return false;
                }

                if (IsDisposed)
                    return false;

                SetStatus(StickerStrings.StatusFindingSubject);
                var finder = FindSubject;
                _map = await Task.Run(() => finder is not null ? finder(photo) : DetectWithModel(photo));
                return !IsDisposed;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!IsDisposed)
                    Notice(string.Format(StickerStrings.StatusSubjectFailedFormat, ex.Message));
                return false;
            }
            finally
            {
                IsFinding = false;
                if (!IsDisposed)
                    SetBusy(false);
            }
        }

        /// <summary>The model looks at the photo and is let go of at once: it takes about a gigabyte while it works, and one photo needs it once.</summary>
        private SubjectMap DetectWithModel(PhotoPixels photo)
        {
            NativeRuntime.EnsureOnnxRuntimeCanLoad();
            using var detector = new SubjectDetector(ToolModels.Cutout.PathIn(_models.ModelsFolder), _models.Threads);
            return detector.Detect(photo);
        }

        // ── Making the sticker ──────────────────────────────────────────────

        private static string KeyOf(StickerSettings settings) => JsonSerializer.Serialize(settings.Normalized());

        /// <summary>What the cutout is made by: the settings that the sticker's cutout depends on (the outline and the format do not change it).</summary>
        private static string MaskKeyOf(StickerSettings settings) => $"{settings.Cutout}|{settings.Sensitivity}|{settings.SmallParts}|{settings.FollowEdges}";

        /// <summary>
        /// Makes the cutout, the sticker and the file it would be saved as with the settings that are in the window. One that is still being made is stopped first. Done when it is made (or stopped, or
        /// failed); what is shown is the newest one.
        /// </summary>
        internal async Task MakeAsync()
        {
            if (_photo is not { } photo || IsDisposed)
                return;

            _debounce.Stop();
            _cancellation?.Cancel();
            var cancellation = _cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var generation = ++_generation;
            var settings = ReadSettings();
            var map = _map;
            var key = KeyOf(settings);
            var maskKey = MaskKeyOf(settings);
            var reuse = _mask is not null && _maskKey == maskKey ? _mask : null;

            if (settings.Cutout == CutoutMode.Detect && map is null)
                return; // the subject is not found (yet): nothing to cut

            IsMaking = true;
            SetBusy(true, marquee: true);
            SetStatus(StickerStrings.StatusMaking);
            SaveButton.Enabled = false;
            try
            {
                // The cutout, and the picture of the photo with its cut line (only when the cutout is new)
                var mask = reuse;
                Bitmap? cutline = null;
                if (mask is null)
                {
                    (mask, cutline) = await Task.Run(() => MakeCutout(photo, map, settings), token);
                    if (generation != _generation || IsDisposed)
                    {
                        cutline?.Dispose();
                        return;
                    }

                    _mask = mask;
                    _maskKey = maskKey;
                    PhotoPane.Picture = cutline;
                }

                StickerPicture sticker;
                try
                {
                    sticker = await Task.Run(() => StickerRenderer.Render(photo, mask, settings), token);
                }
                catch (StickerException ex) when (ex.Reason == StickerFailure.NoSubject)
                {
                    if (generation == _generation && !IsDisposed)
                    {
                        _sticker = null;
                        _encoded = null;
                        _madeKey = null;
                        StickerPane.Picture = null;
                        _infoLabel.Text = string.Empty;
                        SetStatus(StickerStrings.StatusNoSubject, failure: true);
                    }
                    return;
                }

                if (generation != _generation || IsDisposed)
                    return;

                _sticker = sticker;
                StickerPane.Picture = ToBitmap(sticker.Width, sticker.Height, sticker.Bgra, 4);

                // The file as it would be saved: its size is what says whether Telegram takes it
                var encoded = await StickerEncoder.EncodeAsync(sticker, settings.Format, settings.Quality, token, FfmpegPath);
                if (generation != _generation || IsDisposed)
                    return;

                _encoded = encoded;
                _madeKey = key;
                ShowInfo(sticker, encoded);
                SetStatus(_notice ?? string.Empty, failure: _notice is not null);
                SaveButton.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                // Another one took its place, or the window was closed
            }
            catch (StickerException ex)
            {
                if (generation == _generation && !IsDisposed)
                    SetStatus(WordsFor(ex), failure: true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (generation == _generation && !IsDisposed)
                    SetStatus(string.Format(StickerStrings.StatusEncodeFailedFormat, ex.Message), failure: true);
            }
            finally
            {
                if (generation == _generation && !IsDisposed)
                {
                    IsMaking = false;
                    SetBusy(false);
                }
            }
        }

        /// <summary>The cutout of the photo and the picture that shows it (what is cut away darkened, the cut line drawn).</summary>
        private static (float[] Mask, Bitmap Cutline) MakeCutout(PhotoPixels photo, SubjectMap? map, StickerSettings settings)
        {
            var mask = Cutout.Shape(photo, map, settings);
            var radius = Math.Max(1, Math.Max(photo.Width, photo.Height) / 700);
            var overlay = ContourOverlay.Draw(photo, mask, radius, (255, 40, 200));
            return (mask, ToBitmap(photo.Width, photo.Height, overlay, 4));
        }

        private static string WordsFor(StickerException ex) => ex.Reason switch
        {
            StickerFailure.FfmpegNotFound => StickerStrings.StatusFfmpegMissing,
            StickerFailure.NoSubject => StickerStrings.StatusNoSubject,
            _ => string.Format(StickerStrings.StatusEncodeFailedFormat, ex.Detail),
        };

        /// <summary>What the file is (read from its own header) and whether Telegram takes it, in words under the pictures.</summary>
        private void ShowInfo(StickerPicture sticker, EncodedSticker encoded)
        {
            var info = StickerFile.Inspect(encoded.Bytes) ?? new StickerFileInfo(encoded.Format, sticker.Width, sticker.Height, true, encoded.Bytes.Length);
            var fits = StickerFile.Check(info) == StickerProblems.None;
            var format = info.Format == StickerFormat.Png ? "PNG" : "WebP";
            _infoLabel.Text = string.Format(fits ? StickerStrings.InfoFitsFormat : StickerStrings.InfoTooBigFormat, info.Width, info.Height, info.Bytes / 1000.0, format);
            _infoLabel.ForeColor = fits ? (_dark ? ThemeManager.DarkFore : ThemeManager.LightFore) : Color.FromArgb(220, 80, 70);
        }

        // ── Saving ──────────────────────────────────────────────────────────

        private static string KindsOfFile => $"{StickerStrings.FilterWebP}|*.webp|{StickerStrings.FilterPng}|*.png";

        /// <summary>The kind of file a name says: by its extension, or else by the number of the kind that was picked in the list.</summary>
        internal static StickerFormat FormatOf(string path, int filterIndex) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => StickerFormat.Png,
                ".webp" => StickerFormat.WebP,
                _ => filterIndex == 2 ? StickerFormat.Png : StickerFormat.WebP,
            };

        /// <summary>Asks where to save and saves the sticker (it is made first when the settings were changed since it was). True when a file was written.</summary>
        internal async Task<bool> SaveAsync()
        {
            if (_photo is null)
                return false;

            if (IsStale)
                await MakeAsync();
            if (_sticker is not { } sticker || IsStale)
                return false;

            var chosen = AskPath();
            if (chosen is not var (path, filterIndex))
                return false;

            var format = FormatOf(path, filterIndex);
            if (Path.GetExtension(path).Length == 0)
                path += format == StickerFormat.Png ? ".png" : ".webp";

            var settings = ReadSettings();
            try
            {
                // The file that is shown in the size line is the one that is saved, unless the name asks for the other kind
                var file = _encoded is { } shown && shown.Format == format
                    ? shown
                    : await StickerEncoder.EncodeAsync(sticker, format, settings.Quality, CancellationToken.None, FfmpegPath);
                await WriteFileAsync(path, file.Bytes);

                SavedPath = path;
                SetStatus(file.Fits ? string.Format(StickerStrings.StatusSavedFormat, path) : string.Format(StickerStrings.StatusSavedTooBigFormat, path), failure: !file.Fits);
                return true;
            }
            catch (StickerException ex)
            {
                SetStatus(string.Format(StickerStrings.StatusSaveFailedFormat, WordsFor(ex)), failure: true);
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                SetStatus(string.Format(StickerStrings.StatusSaveFailedFormat, ex.Message), failure: true);
                return false;
            }
        }

        /// <summary>Writes the file under another name and moves it into place when it is whole, so that a sticker that was there stays until a new one is complete.</summary>
        private static async Task WriteFileAsync(string path, byte[] bytes)
        {
            var temporary = $"{path}.{Guid.NewGuid():N}.part";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A leftover in the folder of the sticker is not worth failing for
                }
            }
        }

        private (string Path, int FilterIndex)? AskPath()
        {
            var png = Format == StickerFormat.Png;
            var name = Path.GetFileNameWithoutExtension(_photoPath) + "-sticker" + (png ? ".png" : ".webp");
            var folder = Path.GetDirectoryName(_photoPath) ?? string.Empty;
            var index = png ? 2 : 1;
            if (AskSavePath is { } prompt)
                return prompt(StickerStrings.SaveTitle, KindsOfFile, index, folder, name);

            using var dialog = new SaveFileDialog
            {
                Title = StickerStrings.SaveTitle,
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

        private void OnClosing()
        {
            _debounce.Stop();
            _cancellation?.Cancel();

            // A window that fell back to the whole picture because the model was not there opens the same way next time, and asks for the model again
            var settings = ReadSettings();
            if (_cutoutFallback)
                settings.Cutout = CutoutMode.Detect;
            settings.Save(_settingsFile);
        }
    }
}
