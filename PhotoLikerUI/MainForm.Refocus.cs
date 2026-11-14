using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Refocus bar (see <see cref="RefocusBar"/>), which floats over the photo in the viewer as the Colorize bar does: the Refocus button of the toolbar opens it for the photo that is shown, and it stays
    /// open, going on to the next photos, until its ✕ is pressed (or the button again). A photo that is looked at has its depth worked out once (about half a second); then the viewer shows the photo taken in
    /// portrait mode, a click on the photo puts the focus there (a ring marks the place), the sliders change the blur, the sharp zone and the haze while the picture follows them, and the values are kept for the
    /// next photo and the next run (in the settings of the program). The viewer shows a picture of its own for that (the photo of the cache is not touched) and gives the photo back when the bar is closed or
    /// another photo is shown. Save a copy writes "name (portrait).jpg" next to the photo; the Save button of the toolbar saves what is on screen where the person says (see MainForm.SaveAs.cs).
    /// </summary>
    public partial class MainForm
    {
        /// <summary>The bar goes on to a photo that has stayed this long (milliseconds): going through the photos with a key held down works out the depth of none.</summary>
        private const int RefocusSettleMilliseconds = 450;

        /// <summary>A click that moved less than this many pixels is a click (more is the photo being pulled about when it is enlarged).</summary>
        private const int RefocusClickSlop = 4;

        private ToolStripButton _refocusButton = null!;
        private RefocusBar? _focusBar;
        private FocusPreview? _focusPreview;           // the photo that is shown refocused, with its depth
        private Bitmap? _focusPicture;                 // what the viewer shows instead of the photo while it is refocused
        private string? _focusPhoto;                   // the photo the bar is for
        private bool _focusWanted;                     // the bar is open: it goes on to the next photos
        private int _focusGeneration;                  // changes with the photo that is shown: work that comes in for another photo is dropped
        private int _focusRenderTicket;
        private bool _focusRendering;
        private int _focusRuns;                        // depth runs, renders and saves that are going on
        private FocusLook _focusLook = FocusLook.Portrait(0.5);
        private (double X, double Y)? _focusMarker;    // where the person clicked, as fractions of the photo (the ring over the picture)
        private Size _focusOffset;                     // how far the person dragged the bar from its place (top, middle)
        private Point? _focusMouseDown;                // where the button went down on the photo: a click is a press and a release at about the same place

        private void InitializeRefocus()
        {
            _refocusButton = new ToolStripButton(RefocusStrings.ButtonText)
            {
                Name = "refocusToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Refocus(),
                ToolTipText = RefocusStrings.ButtonTooltip,
            };
            _refocusButton.Click += (_, _) => RefocusButtonClicked();

            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(_colorizeButton) + 1, _refocusButton);
        }

        private void ApplyRefocusLanguage()
        {
            _refocusButton.Text = RefocusStrings.ButtonText;
            _refocusButton.ToolTipText = RefocusStrings.ButtonTooltip;
            _focusBar?.ApplyTexts();
        }

        /// <summary>
        /// The depth model is made sure of (asked for, and downloaded, the first time), then the photo is read and given its depth, off the window's thread; the faces the search found in it say where the
        /// focus begins (the largest). Null when the model could not be had. A test answers it with a photo of its own.
        /// </summary>
        protected virtual async Task<FocusPreview?> OpenRefocusAsync(string file)
        {
            if (!await EnsureToolModelAsync(ToolModels.Depth, ToolModelStrings.FocusFeature, askAgain: true))
                return null;

            IReadOnlyList<PhotoRegion> faces = _folderSearch is { } search
                ? [.. search.DescribeFaces(file).Select(label => new PhotoRegion(label.Face.X, label.Face.Y, label.Face.Width, label.Face.Height))]
                : [];
            return await Task.Run(() => FocusPreview.Open(_effects.Depth, file, faces));
        }

        // ── the bar ────────────────────────────────────────────────────────

        private RefocusBar EnsureFocusBar()
        {
            if (_focusBar is { } existing)
                return existing;

            var bar = new RefocusBar { Visible = false };
            bar.Values = (_globalConfig.RefocusBlur, _globalConfig.RefocusRange, _globalConfig.RefocusHaze);
            bar.LookChanged += () =>
            {
                _focusLook = LookOf(bar, _focusLook.Focus);
                _ = RenderRefocusAsync();
            };
            bar.LookCommitted += () =>
            {
                (_globalConfig.RefocusBlur, _globalConfig.RefocusRange, _globalConfig.RefocusHaze) = bar.Values;
                _globalConfig.Save();
            };
            bar.SaveClicked += () => _ = SaveRefocusCopyAsync();
            bar.CloseClicked += CloseRefocusBar;
            bar.Moved += () => _focusOffset = new Size(bar.Left - DefaultFocusBarPlace(bar).X, bar.Top - DefaultFocusBarPlace(bar).Y);
            bar.Used += FocusViewer;

            splitContainer3.Panel1.Controls.Add(bar);
            splitContainer3.Panel1.Resize += (_, _) => PlaceFocusBar();
            ThemeManager.Apply(this, _currentConfig.IsDarkTheme);
            bar.Restyle();
            _focusBar = bar;
            return bar;
        }

        /// <summary>The look the bar says, with the focus at the depth that is given.</summary>
        private static FocusLook LookOf(RefocusBar bar, double focus)
        {
            var (blur, range, haze) = bar.Values;
            return new FocusLook(focus, blur / (double)RefocusBar.Maximum, range / (double)RefocusBar.Maximum, haze / (double)RefocusBar.Maximum);
        }

        /// <summary>At the top of the photo (below the search strip, which is in the same panel), in the middle: the Colorize bar is at the bottom, so the two can be there together.</summary>
        private Point DefaultFocusBarPlace(Control bar) => new((splitContainer3.Panel1.ClientSize.Width - bar.Width) / 2, Math.Max(0, scrollPanel.Top) + 14);

        private void PlaceFocusBar()
        {
            if (_focusBar is not { Visible: true } bar)
                return;

            var area = splitContainer3.Panel1.ClientSize;
            var home = DefaultFocusBarPlace(bar);
            bar.Location = new Point(
                Math.Clamp(home.X + _focusOffset.Width, 0, Math.Max(0, area.Width - bar.Width)),
                Math.Clamp(home.Y + _focusOffset.Height, 0, Math.Max(0, area.Height - bar.Height)));
        }

        private void ShowFocusBar(string file, string status)
        {
            var bar = EnsureFocusBar();
            _focusPhoto = file;
            bar.Status = status;
            if (!bar.Visible)
            {
                bar.Visible = true;
                bar.PerformLayout();
            }
            bar.BringToFront();
            PlaceFocusBar();
        }

        private void HideFocusBar()
        {
            if (_focusBar is { Visible: true } bar)
                bar.Visible = false;
        }

        /// <summary>Whether the bar is open and the photo that is shown has its depth: what the Save button of the toolbar then saves is the picture that is on screen.</summary>
        internal bool RefocusWorking => _focusBar is { Visible: true, CanSave: true } && _focusPreview is not null && pictureBox1.Tag is string file && string.Equals(file, _focusPreview.Path, StringComparison.Ordinal);

        // ── the toolbar button ─────────────────────────────────────────────

        /// <summary>
        /// The Refocus button of the toolbar. With the bar open it closes it; with a photo shown it opens the bar for that photo (the Colorize bar, if it has colours on, gives the viewer back: the two do not
        /// both change the picture); with none it says so.
        /// </summary>
        private void RefocusButtonClicked()
        {
            if (_focusBar is { Visible: true })
            {
                CloseRefocusBar();
                return;
            }

            if (pictureBox1.Tag is not string file || MediaKinds.IsVideo(file))
            {
                SetStatus(RefocusStrings.StatusNoPhoto);
                return;
            }

            _focusWanted = true;
            if (_bar is { ColoursShown: true })
                TurnColoursOff();
            _focusGeneration++;
            ShowFocusBar(file, RefocusStrings.StatusStarting);
            _ = StartRefocusAsync();
        }

        /// <summary>The bar is closed (by its ✕, or by the button): it stays away from the photos until the button is pressed again, and the viewer shows the photo itself.</summary>
        private void CloseRefocusBar()
        {
            _focusWanted = false;
            _focusGeneration++;
            DropRefocusPreview();
            HideFocusBar();
            ResetFocusBarState();
            FocusViewer();
        }

        private void ResetFocusBarState()
        {
            if (_focusBar is not { } bar)
                return;
            bar.Ready = false;
            bar.Busy = false;
        }

        // ── a photo is shown ───────────────────────────────────────────────

        /// <summary>
        /// A photo was chosen in the viewer (called by <see cref="LoadImage"/> before the picture is replaced): the picture of the photo before it goes, and, with the bar open, the depth of this one is
        /// worked out after a moment (not for each photo that a key held down goes through). A video has no bar: it comes back with the next photo.
        /// </summary>
        private async void RefocusBarPhotoChosen(string file)
        {
            if (!_focusWanted)
                return;

            var generation = ++_focusGeneration;
            try
            {
                if (_focusPreview is { } preview && string.Equals(preview.Path, file, StringComparison.Ordinal) && preview.MatchesFile())
                    return; // the same photo again, refocused: nothing changes

                DropRefocusPreview();
                ResetFocusBarState();
                if (MediaKinds.IsVideo(file))
                {
                    HideFocusBar();
                    return;
                }

                ShowFocusBar(file, RefocusStrings.StatusStarting);
                await Task.Delay(RefocusSettleMilliseconds);
                if (generation != _focusGeneration || IsDisposed)
                    return;

                await StartRefocusAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (generation == _focusGeneration && !IsDisposed && _focusBar is { } bar)
                    bar.Status = string.Format(RefocusStrings.StatusFailedFormat, ex.Message);
            }
        }

        /// <summary>The list is made again, or the display cleared: the bar goes away with the photo (it comes back with the next one if it was open), and nothing of the photo that was is kept.</summary>
        private void RefocusBarCleared()
        {
            _focusGeneration++;
            DropRefocusPreview();
            HideFocusBar();
            ResetFocusBarState();
        }

        /// <summary>The photo of the cache was put in the viewer again (it was loaded again): the refocused picture goes back over it, if there is one.</summary>
        private void RefocusBarPhotoShown()
        {
            if (_focusPicture is { } picture && _focusPreview is { } preview && string.Equals(pictureBox1.Tag as string, preview.Path, StringComparison.Ordinal))
                pictureBox1.Image = picture;
        }

        // ── the work ───────────────────────────────────────────────────────

        private async Task StartRefocusAsync()
        {
            if (_focusBar is not { } bar || _focusPhoto is not { } file)
                return;

            var generation = _focusGeneration;
            bar.Ready = false;
            bar.Busy = true;
            bar.Status = RefocusStrings.StatusStarting;
            Interlocked.Increment(ref _focusRuns);
            try
            {
                var preview = await OpenRefocusAsync(file);
                if (generation != _focusGeneration || IsDisposed)
                    return; // another photo is shown by now

                if (preview is null)
                {
                    // The model was not wanted (or could not be had): the main window says which. The bar goes away, so that the next photo does not ask again
                    CloseRefocusBar();
                    SetStatus(RefocusStrings.StatusNoModel);
                    return;
                }

                _focusPreview = preview;
                _focusLook = LookOf(bar, preview.Initial.Focus); // the focus is where the people are (or the middle), the rest is what the person has set
                _focusMarker = null;
                bar.Ready = true;
                bar.Status = RefocusStrings.StatusReady;
                await RenderRefocusAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (generation == _focusGeneration && !IsDisposed)
                    bar.Status = string.Format(RefocusStrings.StatusFailedFormat, ex.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _focusRuns);
                if (generation == _focusGeneration && !IsDisposed)
                    bar.Busy = false;
            }
        }

        /// <summary>
        /// Makes the picture with the look as it is now and puts it in the viewer. While one is being made the sliders or the focus may move on: only the last position is made after it, the ones in between
        /// are skipped, so the picture follows without piling up.
        /// </summary>
        private async Task RenderRefocusAsync()
        {
            _focusRenderTicket++;
            if (_focusRendering)
                return;

            _focusRendering = true;
            try
            {
                int served;
                do
                {
                    served = _focusRenderTicket;
                    if (_focusPreview is not { } preview)
                        break;

                    var look = _focusLook;
                    Bitmap picture;
                    Interlocked.Increment(ref _focusRuns);
                    try
                    {
                        picture = await Task.Run(() => ImageHelper.BitmapOf(preview.Render(look)));
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _focusRuns);
                    }

                    if (IsDisposed || !ReferenceEquals(preview, _focusPreview) || !string.Equals(pictureBox1.Tag as string, preview.Path, StringComparison.Ordinal))
                    {
                        picture.Dispose(); // the photo went, or the bar was closed, while it was made
                        continue;
                    }

                    var old = _focusPicture;
                    _focusPicture = picture;
                    pictureBox1.Image = picture;
                    old?.Dispose();
                } while (served != _focusRenderTicket);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (_focusBar is { } failed)
                    failed.Status = string.Format(RefocusStrings.StatusFailedFormat, ex.Message);
            }
            finally
            {
                _focusRendering = false;
            }
        }

        /// <summary>Lets go of the refocused picture: it goes from the viewer (if it is there) and the photo has to be worked out again to be refocused.</summary>
        private void DropRefocusPreview()
        {
            _focusPreview = null;
            _focusMarker = null;
            _focusRenderTicket++;

            var picture = _focusPicture;
            if (picture is null)
                return;

            _focusPicture = null;
            if (ReferenceEquals(pictureBox1.Image, picture))
                pictureBox1.Image = pictureBox1.Tag is string file && _photoCache.TryGet(file, out var entry) ? entry.Full : null;
            picture.Dispose();
        }

        // ── the focus ──────────────────────────────────────────────────────

        /// <summary>The button went down on the photo (called after the faces have had their say): a click is a press and a release at about the same place.</summary>
        private void RefocusMouseDown(MouseEventArgs e) =>
            _focusMouseDown = _focusPreview is not null && e.Button == MouseButtons.Left ? e.Location : null;

        /// <summary>The button came up on the photo: if it was a click on the photo, the focus is put there. Never uses the event (the photo may be pulled about as well, when it is enlarged).</summary>
        private void RefocusMouseUp(MouseEventArgs e)
        {
            if (_focusMouseDown is not { } down)
                return;

            _focusMouseDown = null;
            if (_focusPreview is null || e.Button != MouseButtons.Left || Math.Abs(e.X - down.X) > RefocusClickSlop || Math.Abs(e.Y - down.Y) > RefocusClickSlop)
                return;

            var image = GetImageRectangle();
            if (!image.IsEmpty && image.Contains(e.Location))
                FocusRefocusAt((e.X - image.X) / image.Width, (e.Y - image.Y) / image.Height);
        }

        /// <summary>Puts the focus where the photo is pointed at (fractions of it): the depth of that place is what stays sharp.</summary>
        internal void FocusRefocusAt(double x, double y)
        {
            if (_focusPreview is not { } preview)
                return;

            _focusMarker = (Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
            _focusLook = _focusLook with { Focus = preview.FocusAt(_focusMarker.Value.X, _focusMarker.Value.Y) };
            pictureBox1.Invalidate();
            _ = RenderRefocusAsync();
        }

        /// <summary>The ring where the focus was put, over the picture (it is not in the copy).</summary>
        private void DrawRefocusMarker(Graphics g)
        {
            if (_focusMarker is not { } marker || _focusPicture is null || !ReferenceEquals(pictureBox1.Image, _focusPicture))
                return;

            var image = GetImageRectangle();
            if (image.IsEmpty)
                return;

            var x = image.X + (float)marker.X * image.Width;
            var y = image.Y + (float)marker.Y * image.Height;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var shadow = new Pen(Color.FromArgb(150, 0, 0, 0), 4f);
            using var ring = new Pen(Color.White, 2f);
            g.DrawEllipse(shadow, x - 15, y - 15, 30, 30);
            g.DrawEllipse(ring, x - 15, y - 15, 30, 30);
            g.DrawLine(ring, x - 4, y, x + 4, y);
            g.DrawLine(ring, x, y - 4, x, y + 4);
        }

        // ── saving a copy ──────────────────────────────────────────────────

        private async Task SaveRefocusCopyAsync()
        {
            if (_focusBar is not { } bar || _focusPreview is not { } preview)
                return;

            if (_indexCts is not null)
            {
                // The index lists the photos it was started with; a copy that joins the folder meanwhile would be indexed as if it had been there
                bar.Status = RefocusStrings.StatusIndexing;
                SetStatus(RefocusStrings.StatusIndexing);
                return;
            }

            var generation = _focusGeneration;
            var look = _focusLook;
            bar.Busy = true;
            bar.Status = string.Format(RefocusStrings.StatusSavingFormat, Path.GetFileName(preview.Path));
            Interlocked.Increment(ref _focusRuns);
            try
            {
                var copy = await Task.Run(() => preview.Save(look));
                var text = string.Format(RefocusStrings.StatusSavedFormat, Path.GetFileName(copy));
                if (generation == _focusGeneration)
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
                var text = string.Format(RefocusStrings.StatusSaveFailedFormat, ex.Message);
                if (generation == _focusGeneration)
                    bar.Status = text;
                if (!IsDisposed)
                    SetStatus(text);
            }
            finally
            {
                Interlocked.Decrement(ref _focusRuns);
                if (generation == _focusGeneration && !IsDisposed)
                    bar.Busy = false;
            }
        }

        private void DisposeRefocusBar()
        {
            _focusGeneration++;
            DropRefocusPreview();
        }
    }
}
