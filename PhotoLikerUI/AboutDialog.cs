namespace PhotoLikerUI
{
    /// <summary>
    /// The About box: the version, the keys and mouse moves of the main window, and the buttons that open the user manual
    /// (the pages installed beside the program, see <see cref="UserManual"/>) in English or Ukrainian, and the button that looks on GitHub
    /// for a newer version and installs it (see <see cref="UpdateLauncher"/>; nothing is asked of GitHub until the button is pressed).
    /// </summary>
    internal sealed class AboutDialog : Form
    {
        private const int KeyColumnGap = 2; // spaces after the longest input
        private const int BytesInMegabyte = 1024 * 1024;

        private readonly CancellationTokenSource _closed = new(); // cancelled when the box closes, which stops a check or a download in progress
        private readonly Button _check;
        private readonly Button _install;
        private readonly Button _releasePage;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private ReleaseInfo? _release;

        /// <summary>The setup of a new version that was downloaded and is to be run, with the program closed (the window does it once the box has closed).</summary>
        public string? SetupToRun { get; private set; }

        public AboutDialog(bool dark)
        {
            Text = MainFormStrings.AboutTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
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

            var shortcutList = AboutStrings.Shortcuts;
            var keyColumnWidth = shortcutList.Max(shortcut => shortcut.Input.Length) + KeyColumnGap;
            var lines = shortcutList.Select(shortcut => shortcut.Input.PadRight(keyColumnWidth) + shortcut.Action).ToArray();
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

            // Only while the setup is downloaded
            _progress = new ProgressBar { Style = ProgressBarStyle.Continuous, Size = new Size(shortcuts.Width, 14), Margin = new Padding(3, 4, 3, 0), Visible = false };
            TaskbarProgress.Watch(_progress);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.AddRange([english, ukrainian, ok]);

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
            layout.Controls.AddRange([heading, version, shortcutsHeading, shortcuts, updateButtons, _status, _progress, buttons]);
            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = ok;

            ThemeManager.Apply(this, dark);

            // The theme paints a label in the colour of a control; here they sit on the panel and show its colour
            foreach (var label in layout.Controls.OfType<Label>())
                label.BackColor = Color.Transparent;

            // The box shows the colour of an input (white in the light theme), as the boxes of the other dialogs do
            shortcuts.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
        }

        private void OpenManual(string language) => UserManualLauncher.Open(this, language);

        private async Task CheckAsync()
        {
            _check.Enabled = false;
            _install.Visible = _releasePage.Visible = false;
            _release = null;
            _status.Text = AboutStrings.UpdateChecking;

            try
            {
                ShowLatest(await UpdateLauncher.Checker.GetLatestAsync(_closed.Token));
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
