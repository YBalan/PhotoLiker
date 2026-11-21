namespace PhotoLikerUI
{
    /// <summary>
    /// The About box: the version, the keys and mouse moves of the main window, and the buttons that open the user manual
    /// (the pages installed beside the program, see <see cref="UserManual"/>) in English or Ukrainian, and the button that looks on GitHub
    /// for a newer version and installs it (see <see cref="UpdateLauncher"/>; nothing is asked of GitHub until the button is pressed).
    /// </summary>
    internal sealed class AboutDialog : Form
    {
        private const int BytesInMegabyte = 1024 * 1024;
        private const int MaxReadableChars = 110;  // a row of keys is not made longer than this, however wide the monitor is
        private const int MinChars = 40;           // nor shorter, however narrow it is
        private const int ScreenMargin = 24;       // the box keeps this much of the working area free, for the shadow and the look of it
        private const int MinVisibleRows = 6;      // a box that has to scroll still shows at least this many rows of keys
        private const int MaxChangeRows = 7;       // the list of what is new shows this many rows of text, then it scrolls
        private const int MinChangeRows = 3;       // and on a monitor with no room for more it still shows this many
        private const int ChangesSeconds = 8;      // the changelog is waited for this long: it is a courtesy, the new version is offered without it

        private readonly CancellationTokenSource _closed = new(); // cancelled when the box closes, which stops a check or a download in progress
        private readonly TableLayoutPanel _layout;
        private readonly TextBox _shortcuts;
        private readonly Size _shortcutsClient;    // the list of keys at the size of its text: the box is fitted to the monitor from this each time something is shown or hidden
        private readonly int _shortcutRowHeight;
        private readonly Button _check;
        private readonly Button _install;
        private readonly Button _releasePage;
        private readonly Label _status;
        private readonly Label _changesHeading;
        private readonly TextBox _changes;         // what is new in the versions after this one, one sentence each; shown with a new version, from its changelog
        private readonly ProgressBar _progress;
        private readonly Rectangle _workingArea;   // the part of the monitor the box has to fit in
        private int _changesHeight;                // the height the sentences want (lower if the monitor has no room), and whether they need a scroll bar
        private bool _changesScroll;
        private bool _changesShown;                // not read from Visible: that is false for as long as the box itself is not shown
        private ReleaseInfo? _release;

        /// <summary>The setup of a new version that was downloaded and is to be run, with the program closed (the window does it once the box has closed).</summary>
        public string? SetupToRun { get; private set; }

        /// <param name="workingArea">The working area (the monitor without the taskbar) of the monitor the box opens on; the box is made to fit in it: its rows of keys are wrapped to its width, and if it is still too tall the list scrolls. Null: the monitor the pointer is on.</param>
        public AboutDialog(bool dark, Rectangle? workingArea = null)
        {
            _workingArea = workingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
            Text = MainFormStrings.AboutTitle;
            FormBorderStyle = FormBorderStyle.FixedSingle; // not FixedDialog: Windows shows no icon in the caption of that frame
            Icon = DialogIcons.Help;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var headingFont = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold);
            var shortcutsFont = new Font("Consolas", Font.Size + 0.5f);
            var sectionFont = new Font(Font, FontStyle.Bold);
            Disposed += (_, _) =>
            {
                headingFont.Dispose();
                shortcutsFont.Dispose();
                sectionFont.Dispose();
            };

            var heading = new Label { Text = AboutStrings.Heading, Font = headingFont, AutoSize = true };
            var version = new Label
            {
                Text = string.Format(AboutStrings.VersionFormat, AppVersionInfo.DisplayVersion, AppVersionInfo.BuildDate),
                AutoSize = true,
                Margin = new Padding(3, 0, 3, 12)
            };
            var shortcutsHeading = new Label { Text = AboutStrings.ShortcutsHeading, Font = sectionFont, AutoSize = true };

            // How many letters of the list fit across the monitor (the font has letters of one width), less what is around it: the frame, the padding, the edge of the box, the scroll bar
            var letterWidth = TextRenderer.MeasureText(new string('0', 50), shortcutsFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width / 50.0;
            var around = 2 * SystemInformation.FrameBorderSize.Width + 2 * 12 + 12 + SystemInformation.VerticalScrollBarWidth + ScreenMargin;
            var maxChars = Math.Clamp((int)Math.Floor((_workingArea.Width - around) / letterWidth), MinChars, MaxReadableChars);

            var shortcutList = AboutStrings.Shortcuts;
            var lines = ShortcutLines.Layout(shortcutList, maxChars);
            var shortcuts = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.None,
                TabStop = false,
                Font = shortcutsFont,
                Text = string.Join(Environment.NewLine, lines)
            };

            // Exactly the size of the text (a box that is too small would scroll, too big would leave an empty edge), whatever the DPI is
            var textSize = lines
                .Select(line => TextRenderer.MeasureText(line, shortcutsFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding))
                .Aggregate(Size.Empty, (size, line) => new Size(Math.Max(size.Width, line.Width), size.Height + line.Height));
            shortcuts.ClientSize = new Size(textSize.Width + 12, textSize.Height + 8);
            _shortcuts = shortcuts;
            _shortcutsClient = shortcuts.ClientSize;
            _shortcutRowHeight = Math.Max(1, textSize.Height / Math.Max(1, lines.Length));

            var english = new Button { Text = AboutStrings.ManualEnglishButton, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            english.Click += (_, _) => OpenManual(UserManual.English);
            var ukrainian = new Button { Text = AboutStrings.ManualUkrainianButton, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            ukrainian.Click += (_, _) => OpenManual(UserManual.Ukrainian);
            var ok = new Button { Text = AboutStrings.OkButton, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, DialogResult = DialogResult.OK };

            _check = new Button { Text = AboutStrings.UpdateCheckButton, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            _check.Click += async (_, _) => await CheckAsync();
            _install = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false };
            _install.Click += async (_, _) => await InstallAsync();
            _releasePage = new Button { Text = AboutStrings.UpdateReleasePageButton, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false };
            _releasePage.Click += (_, _) => OpenReleasePage();
            FormClosed += (_, _) => _closed.Cancel();

            var updateButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            updateButtons.Controls.AddRange([_check, _install, _releasePage]);

            // As wide as the list of keys, and two lines high from the start: a message appearing does not make the box jump
            _status = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(shortcuts.Width, 0),
                MinimumSize = new Size(shortcuts.Width, TextRenderer.MeasureText("Ag", Font).Height * 2),
                Margin = new Padding(3, 6, 3, 0)
            };

            // Only when a new version is found, and its changelog says what is new in it
            _changesHeading = new Label { Text = AboutStrings.ChangesHeading, Font = sectionFont, AutoSize = true, Visible = false, Margin = new Padding(3, 4, 3, 0) };
            _changes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.None,
                TabStop = false,
                Visible = false,
                Width = shortcuts.Width,
                Margin = new Padding(3, 3, 3, 0)
            };

            // Only while the setup is downloaded
            _progress = new ProgressBar { Style = ProgressBarStyle.Continuous, Size = new Size(shortcuts.Width, 14), Margin = new Padding(3, 4, 3, 0), Visible = false };
            TaskbarProgress.Watch(_progress);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.AddRange([english, ukrainian, ok]);

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
            layout.Controls.AddRange([heading, version, shortcutsHeading, shortcuts, updateButtons, _status, _changesHeading, _changes, _progress, buttons]);
            Controls.Add(layout);
            _layout = layout;

            // Each control has its own row: a panel that is left to place them puts a hidden one where it was shown first, not where it was added (the list of what is new came out above its heading)
            layout.RowCount = layout.Controls.Count;
            for (var row = 0; row < layout.Controls.Count; row++)
                layout.SetCellPosition(layout.Controls[row], new TableLayoutPanelCellPosition(0, row));

            FitToWorkingArea();

            AcceptButton = ok;
            CancelButton = ok;

            ThemeManager.Apply(this, dark);

            // The theme paints a label in the colour of a control; here they sit on the panel and show its colour
            foreach (var label in layout.Controls.OfType<Label>())
                label.BackColor = Color.Transparent;

            // The boxes show the colour of an input (white in the light theme), as the boxes of the other dialogs do
            shortcuts.BackColor = _changes.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
        }

        /// <summary>
        /// Makes the box fit the monitor. The list of keys is at the size of its text, unless the box would be taller than the monitor lets it be (a small laptop screen, or a big text
        /// size of Windows): then the list is made lower and scrolls. The list of what is new (when it is shown) gives way after that, down to a few rows. The rest of the box stays as it is.
        /// Called again when what is new is shown or hidden, so it starts from the full size of both each time.
        /// </summary>
        private void FitToWorkingArea()
        {
            var chrome = SystemInformation.CaptionHeight + 2 * SystemInformation.FrameBorderSize.Height;
            var room = _workingArea.Height - ScreenMargin - chrome;

            _shortcuts.ScrollBars = ScrollBars.None;
            _shortcuts.ClientSize = _shortcutsClient;
            _changes.ScrollBars = _changesScroll ? ScrollBars.Vertical : ScrollBars.None;
            _changes.Width = _shortcuts.Width;
            _changes.ClientSize = new Size(_changes.ClientSize.Width, _changesHeight);

            var excess = _layout.GetPreferredSize(Size.Empty).Height - room;
            if (excess > 0)
            {
                var height = Math.Max(_shortcutRowHeight * MinVisibleRows + 8, _shortcutsClient.Height - excess);
                _shortcuts.ScrollBars = ScrollBars.Vertical;
                _shortcuts.ClientSize = new Size(_shortcutsClient.Width, height);
                _shortcuts.Width += SystemInformation.VerticalScrollBarWidth; // the bar is beside the text, not over it
            }

            // As wide as the list of keys
            _status.MaximumSize = new Size(_shortcuts.Width, 0);
            _status.MinimumSize = new Size(_shortcuts.Width, _status.MinimumSize.Height);
            _progress.Width = _shortcuts.Width;
            _changes.Width = _shortcuts.Width;

            excess = _layout.GetPreferredSize(Size.Empty).Height - room;
            if (excess > 0 && _changesShown)
            {
                var floor = RowHeightOf(_changes) * MinChangeRows + 8;
                if (_changes.ClientSize.Height > floor)
                {
                    _changes.ScrollBars = ScrollBars.Vertical;
                    _changes.ClientSize = new Size(_changes.ClientSize.Width, Math.Max(floor, _changes.ClientSize.Height - excess));
                }
            }
        }

        private static int RowHeightOf(Control control) => TextRenderer.MeasureText("Ag", control.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;

        /// <summary>Centred on the window but never off the working area: a box as tall as the monitor lets it be must not sit half under the taskbar or above the top of the screen.</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            KeepOnScreen();
        }

        private void KeepOnScreen()
        {
            var left = Math.Clamp(Left, _workingArea.Left, Math.Max(_workingArea.Left, _workingArea.Right - Width));
            var top = Math.Clamp(Top, _workingArea.Top, Math.Max(_workingArea.Top, _workingArea.Bottom - Height));
            if (left != Left || top != Top)
                Location = new Point(left, top);
        }

        private void OpenManual(string language) => UserManualLauncher.Open(this, language);

        private async Task CheckAsync()
        {
            _check.Enabled = false;
            _install.Visible = _releasePage.Visible = false;
            _release = null;
            ShowChanges(string.Empty);
            _status.Text = AboutStrings.UpdateChecking;

            try
            {
                var latest = await UpdateLauncher.Checker.GetLatestAsync(_closed.Token);

                // What is new is asked for before anything is shown, so that the answer and the list appear together
                var changes = latest is not null && IsOffered(latest) ? await ChangesOfAsync(latest) : string.Empty;
                ShowLatest(latest);
                ShowChanges(changes);
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested)
            {
                // the box was closed
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or IOException)
            {
                _status.Text = string.Format(AboutStrings.UpdateCheckFailedFormat, ex.Message); // OperationCanceledException here: GitHub did not answer in time
            }
            finally
            {
                if (!IsDisposed)
                    _check.Enabled = true;
            }
        }

        /// <summary>Whether the release is one to install: it is newer than this copy, or this copy is a build that is no release (it has no number to compare).</summary>
        private static bool IsOffered(ReleaseInfo latest) =>
            UpdateLauncher.InstalledVersion is not { } installed || UpdateChecker.IsNewer(latest.Version, installed);

        /// <summary>
        /// One sentence for each feature or fix of the versions after this one, from the changelog that the release carries. Empty if there is none, or it could not be got in time:
        /// the sentences are a courtesy and the version is offered without them.
        /// </summary>
        private async Task<string> ChangesOfAsync(ReleaseInfo latest)
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
                limit.CancelAfter(TimeSpan.FromSeconds(ChangesSeconds));

                var sections = await UpdateLauncher.Checker.GetChangelogAsync(latest, limit.Token);
                var news = Changelog.Between(sections, UpdateLauncher.InstalledVersion, latest.Version);
                return Changelog.Format(news, news.Count > 1 ? version => string.Format(AboutStrings.ChangesVersionFormat, version.ToString(3)) : null);
            }
            catch (Exception ex) when (!_closed.IsCancellationRequested && ex is HttpRequestException or InvalidDataException or IOException or OperationCanceledException)
            {
                return string.Empty; // no answer in time, an error of GitHub, or a file that is not a changelog
            }
        }

        /// <summary>
        /// Shows the sentences under the answer, in a box as high as they are (up to a few rows, then it scrolls), or hides the box if there is nothing to say. The box is fitted to the
        /// monitor again, and moved if it grew past the edge of the screen.
        /// </summary>
        private void ShowChanges(string text)
        {
            var show = _changesShown = text.Length > 0;
            _changes.Text = text;
            _changesHeading.Visible = _changes.Visible = show;

            if (show)
            {
                // Measured a little narrower than the box: a line that is wrapped one word earlier than here costs a blank line at the bottom, a line wrapped later would be hidden
                var width = _shortcutsClient.Width - SystemInformation.VerticalScrollBarWidth - 12;
                var wanted = TextRenderer.MeasureText(text, _changes.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl).Height;
                var limit = RowHeightOf(_changes) * MaxChangeRows;
                _changesScroll = wanted > limit;
                _changesHeight = Math.Min(wanted, limit) + 8;
            }

            FitToWorkingArea();
            _layout.PerformLayout();
            KeepOnScreen();
        }

        /// <summary>
        /// Says what the latest release means for this copy, and offers its setup (any copy can have it: one that the setup put here is replaced by it,
        /// any other, a zip or a build from the source, gets an installed copy next to it, which InstallAsync says before it does it).
        /// </summary>
        private void ShowLatest(ReleaseInfo? latest)
        {
            if (latest is null)
            {
                _status.Text = AboutStrings.UpdateNoRelease;
                return;
            }

            _release = latest;
            var number = latest.Version.ToString(3);
            var installed = UpdateLauncher.InstalledVersion;

            if (installed is not null && !UpdateChecker.IsNewer(latest.Version, installed))
            {
                _status.Text = string.Format(AboutStrings.UpdateUpToDateFormat, installed.ToString(3));
                return;
            }

            // A build that is no release (installed is null) has no number to compare: it is told what the latest is, and may install it
            _status.Text = installed is null
                ? string.Format(AboutStrings.UpdateDevelopmentFormat, number)
                : string.Format(latest.InstallerUrl is null ? AboutStrings.UpdateNoSetupFormat : AboutStrings.UpdateAvailableFormat, number, installed.ToString(3));
            _install.Text = string.Format(AboutStrings.UpdateInstallButtonFormat, number);
            _install.Visible = latest.InstallerUrl is not null;
            _releasePage.Visible = true;
        }

        /// <summary>
        /// Downloads the setup of the release found, with a progress bar, and closes the box; the window then closes, runs the setup silently and the
        /// program starts again (<see cref="SetupToRun"/>). A copy that the setup did not put here is asked first: the new version becomes a second copy.
        /// </summary>
        private async Task InstallAsync()
        {
            if (_release is not { InstallerUrl: not null } release)
                return;

            var number = release.Version.ToString(3);
            var total = (double)release.InstallerBytes / BytesInMegabyte;
            if (!UpdateLauncher.InstalledBySetup && !UpdateLauncher.Confirm(this, string.Format(AboutStrings.UpdateConfirmCopyFormat, number, total)))
                return;

            _check.Enabled = _install.Enabled = false;
            _progress.Value = 0;
            _progress.Visible = true;
            var progress = new Progress<(long Done, long Total)>(step =>
            {
                _status.Text = string.Format(AboutStrings.UpdateDownloadingFormat, number, (double)step.Done / BytesInMegabyte, (double)step.Total / BytesInMegabyte);
                _progress.Value = step.Total > 0 ? (int)Math.Min(100, 100 * step.Done / step.Total) : 0;
            });
            _status.Text = string.Format(AboutStrings.UpdateDownloadingFormat, number, 0.0, total);

            try
            {
                SetupToRun = await UpdateLauncher.Checker.DownloadInstallerAsync(release, UpdateLauncher.DownloadFolder, progress, _closed.Token);
                DialogResult = DialogResult.OK; // closes the box
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested)
            {
                // the box was closed
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _status.Text = string.Format(AboutStrings.UpdateDownloadFailedFormat, ex.Message);
            }
            finally
            {
                if (!IsDisposed)
                {
                    _progress.Visible = false;
                    _check.Enabled = _install.Enabled = true;
                }
            }
        }

        private void OpenReleasePage()
        {
            if (_release is null)
                return;

            var error = UpdateLauncher.OpenPage(_release.PageUrl);
            if (error is not null)
                MessageBox.Show(this, string.Format(DialogStrings.OpenErrorFormat, _release.PageUrl, error), MainFormStrings.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
