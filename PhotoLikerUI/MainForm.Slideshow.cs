namespace PhotoLikerUI
{
    /// <summary>
    /// Alt+Enter: the photos of the list as a slideshow on the whole screen the window is on, or on every screen (see <see cref="SlideshowSession"/>), beginning with the
    /// photo that is shown. How it runs is in the rows of the "4. Slideshow" category of the settings. When it ends the window shows the photo it ended on, on
    /// the screen of the window.
    /// </summary>
    public partial class MainForm
    {
        private void StartSlideshow()
        {
            if (CreateSlideshow() is not { } slideshow)
            {
                SetStatus(SlideshowStrings.StatusNoPhotos);
                return;
            }

            using (slideshow)
            {
                slideshow.Run(this);
                ShowWhereSlideshowEnded(slideshow.LastShownPath);
            }
        }

        /// <summary>The slideshow of the list that is open (the photos of the folder, or the search results while they are shown); null if it has no photos.</summary>
        private SlideshowSession? CreateSlideshow()
        {
            var files = _currentConfig.Files;
            if (files.Count == 0)
                return null;

            var start = pictureBox1.Tag is string shown ? Math.Max(0, files.IndexOf(shown)) : 0;
            var (screens, primary) = SlideshowScreens();
            return new SlideshowSession(
                files, start, _currentConfig.SlideshowMode, _currentConfig.SlideshowTimeout, _currentConfig.SlideshowWatermark, screens, primary);
        }

        /// <summary>The screens the slideshow covers, left to right, and which of them the window is on: that one only, or every screen that is connected.</summary>
        private (Rectangle[] Bounds, int Primary) SlideshowScreens()
        {
            var here = Screen.FromControl(this);
            var screens = _currentConfig.SlideshowMonitors == SlideshowMonitors.All
                ? Screen.AllScreens.OrderBy(screen => screen.Bounds.Left).ThenBy(screen => screen.Bounds.Top).ToArray()
                : [here];

            var primary = Math.Max(0, Array.FindIndex(screens, screen => screen.DeviceName == here.DeviceName));
            return ([.. screens.Select(screen => screen.Bounds)], primary);
        }

        /// <summary>The photo that was on the screen last is the one to see now; the list may have changed while the slideshow ran, so it is looked up again.</summary>
        private void ShowWhereSlideshowEnded(string? file)
        {
            var files = _currentConfig.Files;
            var index = file is null ? -1 : files.IndexOf(file);
            if (file is not null && index >= 0)
            {
                if (!string.Equals(file, pictureBox1.Tag as string, StringComparison.Ordinal))
                    LoadImage(file);
                SetStatus(string.Format(SlideshowStrings.StatusEndedFormat, Path.GetFileName(file), index + 1, files.Count));
            }

            FocusViewer(); // the keys are for the photo again
        }
    }
}
