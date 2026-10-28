using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Asks where the date taken of photos that have no usable EXIF date comes from, and shows for each answer how many of the photos have one
    /// from there and what it would be, before anything is decided.
    /// </summary>
    internal sealed class AssignDateDialog : Form
    {
        private readonly IReadOnlyList<PhotoDateFacts> _selected;
        private readonly IReadOnlyList<PhotoDateFacts> _all;
        private readonly string _root;
        private readonly DateTime _now = DateTime.Now;

        private readonly RadioButton _scopeSelected = new() { AutoSize = true };
        private readonly RadioButton _scopeAll = new() { AutoSize = true };
        private readonly (RadioButton Button, Label Count, DateChoice Choice)[] _sources;
        private readonly DateTimePicker _picker = new() { Format = DateTimePickerFormat.Custom, CustomFormat = DateAlignmentStrings.PickerFormat, Width = 210 };
        private readonly Label _fileWarning = new() { Text = DateAlignmentStrings.AssignFileWarning, AutoSize = true, MaximumSize = new Size(560, 0) };
        private readonly Label _noPlace = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
        private readonly Button _ok = new() { Text = DateAlignmentStrings.AssignOk, AutoSize = true, MinimumSize = new Size(110, 28), DialogResult = DialogResult.OK };
        private readonly Button _cancel = new() { Text = DateAlignmentStrings.AssignCancel, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.Cancel };

        /// <param name="selected">The photos without a usable date that are selected in the list (may be none).</param>
        /// <param name="all">All the photos without a usable date.</param>
        public AssignDateDialog(IReadOnlyList<PhotoDateFacts> selected, IReadOnlyList<PhotoDateFacts> all, string root, bool dark)
        {
            _selected = selected;
            _all = all;
            _root = root;

            Text = DateAlignmentStrings.AssignTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(610, 470);

            _sources =
            [
                (new RadioButton { Text = DateAlignmentStrings.AssignSourceExif, AutoSize = true, Checked = true }, new Label { AutoSize = true }, DateChoice.ExifAsItIs),
                (new RadioButton { Text = DateAlignmentStrings.AssignSourceFolder, AutoSize = true }, new Label { AutoSize = true }, DateChoice.FolderName),
                (new RadioButton { Text = DateAlignmentStrings.AssignSourceFile, AutoSize = true }, new Label { AutoSize = true }, DateChoice.FileDate),
                (new RadioButton { Text = DateAlignmentStrings.AssignSourceTyped, AutoSize = true }, new Label { AutoSize = true }, DateChoice.Typed),
            ];

            BuildLayout();

            _scopeSelected.Text = string.Format(DateAlignmentStrings.AssignScopeSelected, selected.Count);
            _scopeAll.Text = string.Format(DateAlignmentStrings.AssignScopeAll, all.Count);
            _scopeSelected.Enabled = selected.Count > 0;
            _scopeSelected.Checked = selected.Count > 0;
            _scopeAll.Checked = selected.Count == 0;

            // The date to start from: what the first photo gets from its folder, else from its file, else now
            var first = Scope().FirstOrDefault();
            _picker.Value = (first is null ? null : DateSources.FromFolderName(root, first.Path, null) ?? DateSources.FromFile(first, _now)) is { } start
                && start >= _picker.MinDate && start <= _picker.MaxDate ? start : _now;

            _scopeSelected.CheckedChanged += (_, _) => UpdatePreview();
            _scopeAll.CheckedChanged += (_, _) => UpdatePreview();
            foreach (var source in _sources)
                source.Button.CheckedChanged += (_, _) => UpdatePreview();
            _picker.ValueChanged += (_, _) =>
            {
                _sources[3].Button.Checked = true;
                UpdatePreview();
            };

            AcceptButton = _ok;
            CancelButton = _cancel;
            ThemeManager.Apply(this, dark);
            foreach (Control control in Controls.Cast<Control>().SelectMany(Flatten).Where(c => c is Label or RadioButton))
                control.BackColor = Color.Transparent;
            UpdatePreview();
        }

        public DateChoice Choice => _sources.FirstOrDefault(s => s.Button.Checked).Choice;

        public DateTime Typed => _picker.Value;

        /// <summary>The photos it is for: the ones that were selected, or all that have no usable date.</summary>
        public IReadOnlyList<PhotoDateFacts> Scope() => _scopeSelected.Checked ? _selected : _all;

        private static IEnumerable<Control> Flatten(Control root) => new[] { root }.Concat(root.Controls.Cast<Control>().SelectMany(Flatten));

        private void BuildLayout()
        {
            var intro = new Label { Text = DateAlignmentStrings.AssignIntro, AutoSize = true, MaximumSize = new Size(570, 0), Margin = new Padding(0, 0, 0, 10) };

            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 12, 14, 8), AutoScroll = false };
            panel.Controls.Add(intro);

            // Radio buttons that share a container are one group: the ones for who it is for are a group of their own
            var scope = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            scope.Controls.AddRange([_scopeSelected, _scopeAll]);
            panel.Controls.Add(scope);

            foreach (var (button, count, choice) in _sources)
            {
                button.Margin = new Padding(3, 12, 3, 0);
                count.Margin = new Padding(24, 0, 3, 0);
                panel.Controls.Add(button);
                if (choice == DateChoice.Typed)
                {
                    _picker.Margin = new Padding(24, 2, 3, 0);
                    panel.Controls.Add(_picker);
                    panel.Controls.Add(count);
                }
                else
                {
                    panel.Controls.Add(count);
                }
                if (choice == DateChoice.FileDate)
                {
                    _fileWarning.Margin = new Padding(24, 0, 3, 0);
                    panel.Controls.Add(_fileWarning);
                }
            }

            _noPlace.Margin = new Padding(3, 14, 3, 0);
            panel.Controls.Add(_noPlace);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(10, 6, 10, 6) };
            buttons.Controls.AddRange([_cancel, _ok]);

            Controls.Add(panel);
            Controls.Add(buttons);
        }

        /// <summary>Counts, for every answer, the photos of the scope that have a date from there, with an example.</summary>
        private void UpdatePreview()
        {
            var scope = Scope();
            foreach (var (button, count, choice) in _sources)
            {
                var found = scope
                    .Select(f => DateSources.Resolve(choice, f, _root, choice == DateChoice.Typed ? _picker.Value : null, _now))
                    .Where(d => d is not null)
                    .Select(d => d!.Value)
                    .ToList();
                count.Text = found.Count == 0
                    ? string.Format(DateAlignmentStrings.AssignCountNone, scope.Count)
                    : string.Format(DateAlignmentStrings.AssignCountFormat, found.Count, scope.Count, DateText.Display(found[0]));
            }

            var noPlace = scope.Count(f => f.Exif.OriginalOffset is null && f.Exif.DigitizedOffset is null && f.Exif.ModifiedOffset is null);
            _noPlace.Text = noPlace > 0 ? string.Format(DateAlignmentStrings.AssignNoPlaceFormat, noPlace) : string.Empty;
            _ok.Enabled = scope.Count > 0 && scope.Any(f => DateSources.Resolve(Choice, f, _root, _picker.Value, _now) is not null);
        }
    }
}
