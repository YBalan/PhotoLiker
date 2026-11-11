using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// Asks which language the chosen videos are to be heard in: one of the languages the speech model knows, or "auto", which leaves it to the model to guess (what takes a short or noisy
    /// video for any language there is). Says how many videos it is and how long, and, when it is known, what they were heard as before.
    /// </summary>
    internal sealed class SpeechLanguageDialog : Form
    {
        private sealed record Choice(string Code, string Text)
        {
            public override string ToString() => Text;
        }

        private readonly ComboBox _box = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, DropDownHeight = 360 };
        private readonly Button _ok = new() { Text = VideoSpeechStrings.LanguageOkButton, DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(110, 28) };
        private readonly Button _cancel = new() { Text = VideoSpeechStrings.LanguageCancelButton, DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 28) };
        private readonly bool _dark;

        /// <param name="videos">How many videos, with their noun ("12 videos").</param>
        /// <param name="length">How long they are together, as text.</param>
        /// <param name="heardAs">What they were heard as, as text ("Russian (ru) 12, English (en) 3"); empty if nothing is known.</param>
        /// <param name="suggested">The code of the language to have selected.</param>
        /// <param name="modelTitle">The speech model they are heard with ("Whisper small"): only the text this model has for them is replaced.</param>
        public SpeechLanguageDialog(string videos, string length, string heardAs, string suggested, string modelTitle, bool dark)
        {
            _dark = dark;
            Text = VideoSpeechStrings.LanguageTitle;
            StartPosition = FormStartPosition.CenterParent;
            Icon = DialogIcons.VideoSpeech;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _box.Items.Add(new Choice(SpeechLanguages.Auto, VideoSpeechStrings.LanguageAuto));
            foreach (var language in SpeechLanguages.All)
                _box.Items.Add(new Choice(language.Code, $"{language.Name} ({language.Code})"));
            _box.SelectedIndex = Math.Max(0, _box.Items.Cast<Choice>().ToList().FindIndex(c => string.Equals(c.Code, suggested, StringComparison.OrdinalIgnoreCase)));

            var question = Wrapping(string.Format(VideoSpeechStrings.LanguageQuestionFormat, videos, length, modelTitle));
            var hint = Wrapping(VideoSpeechStrings.LanguageHint);
            var label = new Label { Text = VideoSpeechStrings.LanguageLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };

            var pick = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 4) };
            pick.Controls.Add(label);
            pick.Controls.Add(_box);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_ok);

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(14, 12, 14, 10), Dock = DockStyle.Fill };
            root.Controls.Add(question);
            if (heardAs.Length > 0)
                root.Controls.Add(Wrapping(string.Format(VideoSpeechStrings.LanguageNowFormat, heardAs), bold: true));
            root.Controls.Add(pick);
            root.Controls.Add(hint);
            root.Controls.Add(buttons);
            Controls.Add(root);

            AcceptButton = _ok;
            CancelButton = _cancel;
            ThemeManager.Apply(this, _dark);
            foreach (var wrapped in root.Controls.OfType<Label>())
                wrapped.BackColor = Color.Transparent;
            label.BackColor = Color.Transparent;
        }

        /// <summary>"auto", or the code of the language that was chosen.</summary>
        public string Language => ((Choice)_box.SelectedItem!).Code;

        private static Label Wrapping(string text, bool bold = false)
        {
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 0, 0, 6) };
            if (bold)
                label.Font = new Font(label.Font, FontStyle.Bold);
            return label;
        }
    }
}
