using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The bar that floats over a black and white photo (see <see cref="ColorizeBar"/>): a photo that is shown is looked at, and if it is black and white the bar is there; pressing Colorize runs the
    /// model once (about 2 seconds) and the photo in the viewer is replaced by the same photo with colours, which change as the sliders are moved (the colours the model gave are kept, and put on the photo
    /// again for each change: tens of milliseconds); Save writes the copy that is on screen next to the photo. The viewer shows a picture of its own for that (the photo of the cache is not touched), and
    /// gives the photo back when the colours are turned off or another photo is shown.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>A photo is looked at only when it has stayed this long (milliseconds): going through the photos with a key held down reads none.</summary>
        private const int BarSettleMilliseconds = 150;

        /// <summary>The wish for colours goes on to the next black and white photo, which is coloured when it has stayed this long.</summary>
        private const int BarColoursSettleMilliseconds = 450;

        private ColorizeBar? _bar;
        private Task<Colorizer>? _barModel;            // the model, started when it is first needed and kept while the bar is in use
        private int _barRuns;                          // model runs, renders and saves that are going on: the model is not let go of under them
        private ColorPreview? _barPreview;             // the photo that is shown with colours, with the colours the model gave it
        private Bitmap? _barPicture;                   // what the viewer shows instead of the photo while its colours are on
        private string? _barPhoto;                     // the photo the bar is for
        private bool _barColours;                      // the person wants colours: they go on to the next black and white photos
        private int _barGeneration;                    // changes with the photo that is shown: work that comes in for another photo is dropped
        private int _barRenderTicket;
        private bool _barRendering;
        private Size _barOffset;                       // how far the person dragged the bar from its place (bottom, middle)
        private readonly Dictionary<string, bool> _barBlackAndWhite = new(StringComparer.Ordinal);

        private ColorizeBar EnsureBar()
        {
            if (_bar is { } existing)
                return existing;

            var bar = new ColorizeBar { Visible = false };
            bar.Values = (_globalConfig.ColorizeStrength, _globalConfig.ColorizeWarmth);
            bar.ColorsToggled += on =>
            {
                if (on)
                    _ = TurnColoursOnAsync();
                else
                    TurnColoursOff();
            };
            bar.TuningChanged += () => _ = RenderBarAsync();
            bar.TuningCommitted += () =>
            {
                (_globalConfig.ColorizeStrength, _globalConfig.ColorizeWarmth) = bar.Values;
                _globalConfig.Save();
            };
            bar.SaveClicked += () => _ = SaveBarCopyAsync();
            bar.MoreClicked += Colorize;
            bar.CloseClicked += CloseBarByPerson;
            bar.Moved += () => _barOffset = new Size(bar.Left - DefaultBarPlace(bar).X, bar.Top - DefaultBarPlace(bar).Y);
            bar.Used += FocusViewer;

            splitContainer3.Panel1.Controls.Add(bar);
            splitContainer3.Panel1.Resize += (_, _) => PlaceBar();
            ThemeManager.Apply(this, _currentConfig.IsDarkTheme);
            bar.Restyle();
            _bar = bar;
            return bar;
        }

        private Point DefaultBarPlace(Control bar)
        {
            var area = splitContainer3.Panel1.ClientSize;
            return new Point((area.Width - bar.Width) / 2, area.Height - statusStrip1.Height - bar.Height - 14);
        }

        private void PlaceBar()
        {
            if (_bar is not { Visible: true } bar)
                return;

            var area = splitContainer3.Panel1.ClientSize;
            var home = DefaultBarPlace(bar);
            bar.Location = new Point(
                Math.Clamp(home.X + _barOffset.Width, 0, Math.Max(0, area.Width - bar.Width)),
                Math.Clamp(home.Y + _barOffset.Height, 0, Math.Max(0, area.Height - bar.Height)));
        }

        private void ShowBar(string file, string status)
        {
            var bar = EnsureBar();
            _barPhoto = file;
            bar.Status = status;
            if (!bar.Visible)
            {
                bar.Visible = true;
                bar.PerformLayout();
            }
            bar.BringToFront();
            PlaceBar();
        }

        private void HideBar()
        {
            if (_bar is { Visible: true } bar)
                bar.Visible = false;
        }

        // ── the toolbar button ─────────────────────────────────────────────

        /// <summary>
        /// The Colorize button of the toolbar. With a photo shown it brings the bar to that photo and gives the photo its colours (the bar is only closed by its own button: a person who presses
        /// Colorize wants colours, also when the bar came by itself); with none it opens the window for the photos of the list.
        /// </summary>
        private void ColorizeButtonClicked()
        {
            if (pictureBox1.Tag is not string file || MediaKinds.IsVideo(file))
            {
                Colorize();
                return;
            }

            if (_bar is { Visible: true } shown && string.Equals(_barPhoto, file, StringComparison.Ordinal))
            {
                // The bar is there already (it came with the photo): the button does what the one on the bar does
                if (!shown.ColoursShown && !shown.Busy)
                    _ = TurnColoursOnAsync();
                return;
            }

            _globalConfig.ColorizeBar = true;
            _globalConfig.Save();
            ShowBar(file, _barBlackAndWhite.TryGetValue(file, out var blackAndWhite) && !blackAndWhite ? ColorizeStrings.BarStatusHasColours : ColorizeStrings.BarStatusReady);
            _ = TurnColoursOnAsync();
        }

        /// <summary>The person closed the bar: it stays away from the photos they look at, until the button of the toolbar is pressed.</summary>
        private void CloseBarByPerson()
        {
            _globalConfig.ColorizeBar = false;
            _globalConfig.Save();
            _barColours = false;
            _barGeneration++;
            DropBarPreview();
            HideBar();
            ResetBarState();
            ReleaseBarModel();
            FocusViewer();
        }

        // ── a photo is shown ───────────────────────────────────────────────

        /// <summary>
        /// A photo was chosen in the viewer (called by <see cref="LoadImage"/> before the picture is replaced): the picture of the colours of the photo before it goes, and the bar for this one
        /// when it is black and white (after a moment: not for each photo that a key held down goes through).
        /// </summary>
        private async void ColorizeBarPhotoChosen(string file)
        {
            var generation = ++_barGeneration;
            try
            {
                if (_barPreview is { } preview && !(string.Equals(preview.Path, file, StringComparison.Ordinal) && preview.MatchesFile()))
                    DropBarPreview();
                else if (_barPreview is not null)
                    return; // the same photo again, with its colours on: nothing changes

                if (!_globalConfig.ColorizeBar || MediaKinds.IsVideo(file) || ColorizedCopy.IsCopy(file))
                {
                    HideBar();
                    return;
                }

                // The bar stays while the next photo is looked at (most likely it is black and white as well), as it was for the last: it only forgets what was true of that one
                if (_bar is { Visible: true } shown)
                {
                    ResetBarState();
                    shown.Status = string.Empty;
                }

                await Task.Delay(BarSettleMilliseconds);
                if (generation != _barGeneration || IsDisposed)
                    return;

                if (!_barBlackAndWhite.TryGetValue(file, out var blackAndWhite))
                {
                    blackAndWhite = await Task.Run(() => IsBlackAndWhite(file));
                    if (generation != _barGeneration || IsDisposed)
                        return;
                    _barBlackAndWhite[file] = blackAndWhite;
                }

                if (!blackAndWhite)
                {
                    HideBar();
                    return;
                }

                ShowBar(file, ColorizeStrings.BarStatusReady);
                if (_barColours)
                {
                    await Task.Delay(BarColoursSettleMilliseconds - BarSettleMilliseconds);
                    if (generation == _barGeneration && !IsDisposed)
                        await TurnColoursOnAsync();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (generation == _barGeneration && !IsDisposed)
                    HideBar();
            }
        }

        private static bool IsBlackAndWhite(string file)
        {
            try
            {
                return ColorizeJob.Measure(file).IsBlackAndWhite;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false; // a photo that can not be read has nothing to colorize
            }
        }

        /// <summary>The list is made again, or the display cleared: the bar goes away with the photo, and what was known of the photos is not trusted any more.</summary>
        private void ColorizeBarCleared()
        {
            _barGeneration++;
            _barBlackAndWhite.Clear();
            DropBarPreview();
            HideBar();
            ResetBarState();
        }

        /// <summary>The bar forgets what was true of the photo it was for: no colours, nothing to save, nothing going on.</summary>
        private void ResetBarState()
        {
            if (_bar is not { } bar)
                return;
            bar.ColoursShown = false;
            bar.Ready = false;
            bar.Busy = false;
        }

        // ── the colours ────────────────────────────────────────────────────

        private async Task TurnColoursOnAsync()
        {
            if (_bar is not { } bar || _barPhoto is not { } file)
                return;

            if (_focusWanted)
                CloseRefocusBar(); // the Refocus bar changes the picture too: the two do not both have the viewer

            var generation = _barGeneration;
            _barColours = true;

            // This photo was coloured already (the person looked at the original for a moment): the colours are there
            if (_barPreview is { } have && string.Equals(have.Path, file, StringComparison.Ordinal))
            {
                bar.Ready = true;
                bar.ColoursShown = true;
                await RenderBarAsync();
                bar.Status = ColorizeStrings.BarStatusShown;
                return;
            }

            bool haveModel;
            try
            {
                haveModel = await EnsureToolModelAsync(ToolModels.Colorizer, ToolModelStrings.ColorizeFeature, askAgain: true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Nobody waits for this (it is started by a click): an exception would be lost, and the bar would stay as if the colours were coming
                _barColours = false;
                if (generation == _barGeneration && !IsDisposed)
                    bar.Status = string.Format(ColorizeStrings.BarStatusFailedFormat, ex.Message);
                return;
            }

            if (!haveModel)
            {
                _barColours = false; // asked, and the answer was no (or it could not be had): the main window says which
                return;
            }
            if (generation != _barGeneration || IsDisposed)
                return;

            bar.Busy = true;
            bar.Status = _barModel is null ? ColorizeStrings.BarStatusStarting : ColorizeStrings.BarStatusColouring;
            Interlocked.Increment(ref _barRuns);
            try
            {
                _barModel ??= Task.Run(() => _effects.CreateColorizer(Math.Max(2, Environment.ProcessorCount / 2)));
                var colorizer = await _barModel;
                if (generation == _barGeneration)
                    bar.Status = ColorizeStrings.BarStatusColouring;

                var preview = await Task.Run(() => ColorPreview.Open(colorizer, file));
                if (generation != _barGeneration || IsDisposed)
                    return; // another photo is shown by now

                _barPreview = preview;
                bar.Ready = true;
                bar.ColoursShown = true;
                bar.Busy = false; // the sliders and the button work while the first picture is made
                await RenderBarAsync();
                if (generation == _barGeneration && _barPreview == preview)
                    bar.Status = ColorizeStrings.BarStatusShown;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (_barModel is { IsFaulted: true })
                    _barModel = null; // the next try starts it again
                if (generation == _barGeneration && !IsDisposed)
                {
                    _barColours = false;
                    bar.ColoursShown = false;
                    bar.Ready = false;
                    bar.Status = string.Format(ColorizeStrings.BarStatusFailedFormat, ex.Message);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _barRuns);
                if (generation == _barGeneration && !IsDisposed)
                    bar.Busy = false;
            }
        }

        /// <summary>The person asked for the original: the viewer shows the photo itself again (the colours are kept, so that asking for them again costs nothing).</summary>
        private void TurnColoursOff()
        {
            _barColours = false;
            if (_bar is not { } bar)
                return;

            bar.ColoursShown = false;
            bar.Status = ColorizeStrings.BarStatusOriginal;
            _barRenderTicket++; // a picture that is being made is not wanted any more
            ShowOriginal();
        }

        /// <summary>Puts the photo of the cache back in the viewer in place of the picture with colours, which is let go of.</summary>
        private void ShowOriginal()
        {
            var picture = _barPicture;
            if (picture is null)
                return;

            _barPicture = null;
            if (ReferenceEquals(pictureBox1.Image, picture))
                pictureBox1.Image = pictureBox1.Tag is string file && _photoCache.TryGet(file, out var entry) ? entry.Full : null;
            picture.Dispose();
        }

        /// <summary>Lets go of the colours of the photo: the picture goes from the viewer (if it is there) and the photo has to be coloured again to have them.</summary>
        private void DropBarPreview()
        {
            _barPreview = null;
            _barRenderTicket++;
            ShowOriginal();
        }

        /// <summary>
        /// Makes the picture with the colours as the sliders say and puts it in the viewer. While one is being made the sliders may move on: only the last position is made after it, the ones in between are
        /// skipped, so the picture follows the slider without piling up.
        /// </summary>
        private async Task RenderBarAsync()
        {
            _barRenderTicket++;
            if (_barRendering)
                return;

            _barRendering = true;
            try
            {
                int served;
                do
                {
                    served = _barRenderTicket;
                    if (_barPreview is not { } preview || _bar is not { ColoursShown: true } bar)
                        break;

                    var tuning = bar.Tuning;
                    Bitmap picture;
                    Interlocked.Increment(ref _barRuns);
                    try
                    {
                        picture = await Task.Run(() => ImageHelper.BitmapOf(preview.Render(tuning)));
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _barRuns);
                    }

                    if (IsDisposed || !ReferenceEquals(preview, _barPreview) || !bar.ColoursShown || !string.Equals(pictureBox1.Tag as string, preview.Path, StringComparison.Ordinal))
                    {
                        picture.Dispose(); // the photo went, or the person asked for the original, while it was made
                        continue;
                    }

                    var old = _barPicture;
                    _barPicture = picture;
                    pictureBox1.Image = picture;
                    old?.Dispose();
                } while (served != _barRenderTicket);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (_bar is { } failed)
                    failed.Status = string.Format(ColorizeStrings.BarStatusFailedFormat, ex.Message);
            }
            finally
            {
                _barRendering = false;
            }
        }

        /// <summary>The photo of the cache was put in the viewer again (it was loaded again): the picture with colours goes back over it, if it is on.</summary>
        private void ColorizeBarPhotoShown()
        {
            if (_barPicture is { } picture && _bar is { ColoursShown: true } && _barPreview is { } preview && string.Equals(pictureBox1.Tag as string, preview.Path, StringComparison.Ordinal))
                pictureBox1.Image = picture;
        }

        // ── saving ─────────────────────────────────────────────────────────

        private async Task SaveBarCopyAsync()
        {
            if (_bar is not { } bar || _barPreview is not { } preview)
                return;

            if (_indexCts is not null)
            {
                // The index lists the photos it was started with; a copy that joins the folder meanwhile would be indexed as if it had been there
                bar.Status = ColorizeStrings.StatusIndexing;
                SetStatus(ColorizeStrings.StatusIndexing);
                return;
            }

            var generation = _barGeneration;
            var tuning = bar.Tuning;
            bar.Busy = true;
            bar.Status = ColorizeStrings.BarStatusSaving;
            Interlocked.Increment(ref _barRuns);
            try
            {
                var copy = await Task.Run(() => preview.Save(tuning));
                var text = string.Format(ColorizeStrings.BarStatusSavedFormat, Path.GetFileName(copy));
                if (generation == _barGeneration)
                    bar.Status = text;

                // The copy is a new file of the folder: it joins the list (the photo that is shown stays where it is)
                StopListingCheck();
                if (_listing is { } listing && !_resultsActive && !IsDisposed)
                    await CheckListingAsync(_folderGeneration, listing, _currentConfig.SortOrder);
                if (!IsDisposed)
                    SetStatus(text);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                var text = string.Format(ColorizeStrings.BarStatusSaveFailedFormat, ex.Message);
                if (generation == _barGeneration)
                    bar.Status = text;
                if (!IsDisposed)
                    SetStatus(text);
            }
            finally
            {
                Interlocked.Decrement(ref _barRuns);
                if (generation == _barGeneration && !IsDisposed)
                    bar.Busy = false;
            }
        }

        // ── the end ────────────────────────────────────────────────────────

        /// <summary>The model is let go of (135 MB) when nothing is using it; a run that is still going on is left to the end of the program.</summary>
        private void ReleaseBarModel()
        {
            if (_barModel is { IsCompletedSuccessfully: true } started && Volatile.Read(ref _barRuns) == 0)
            {
                _barModel = null;
                started.Result.Dispose();
            }
        }

        private void DisposeColorizeBar()
        {
            _barGeneration++;
            DropBarPreview();
            ReleaseBarModel();
        }
    }
}
