namespace PhotoLikerUI
{
    /// <summary>
    /// The About box: the version, the keys and mouse moves of the main window, and the buttons that open the user manual
    /// (the pages installed beside the program, see <see cref="UserManual"/>) in English or Ukrainian.
    /// </summary>
    internal sealed class AboutDialog : Form
    {
        private const int KeyColumnWidth = 20; // characters; the longest input ("Alt+1-9, Alt+A-Z") plus two spaces

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
                Text = string.Format(AboutStrings.VersionFormat, AppVersionInfo.Version, AppVersionInfo.BuildDate),
                AutoSize = true,
                Margin = new Padding(3, 0, 3, 12)
            };
            var shortcutsHeading = new Label { Text = AboutStrings.ShortcutsHeading, Font = sectionFont, AutoSize = true };

            var lines = AboutStrings.Shortcuts.Select(shortcut => shortcut.Input.PadRight(KeyColumnWidth) + shortcut.Action).ToArray();
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

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.AddRange([english, ukrainian, ok]);

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
            layout.Controls.AddRange([heading, version, shortcutsHeading, shortcuts, buttons]);
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
    }
}
