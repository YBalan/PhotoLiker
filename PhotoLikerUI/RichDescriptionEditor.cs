using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// A Rich Text Box with the buttons for what a book can show: bold, italic, underline, strike-through, the size and the colour of the text, and where the lines go (left, centre, right,
    /// spread to both sides). It looks like the paper of the book (black on white) whatever the theme of the program is, and it keeps its text as RTF (<see cref="Rtf"/>), which
    /// <see cref="RtfParser"/> reads for the PDF and the EPUB.
    /// </summary>
    internal sealed class RichDescriptionEditor : Panel
    {
        /// <summary>The font of the text as it is written here; the size of text that has none of its own is the one a book sets its text in.</summary>
        private static readonly string EditorFamily = "Segoe UI";

        private static readonly float[] Sizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48];

        private readonly RichTextBox _box = new();
        private readonly ToolStrip _tools = new();
        private readonly ToolStripButton _undo = new() { Name = "undoTextButton" };
        private readonly ToolStripButton _redo = new() { Name = "redoTextButton" };
        private readonly ToolStripSeparator _undoSeparator = new();
        private readonly ToolStripButton _bold = new();
        private readonly ToolStripButton _italic = new();
        private readonly ToolStripButton _underline = new();
        private readonly ToolStripButton _strike = new();
        private readonly ToolStripComboBox _size = new();
        private readonly ToolStripSplitButton _color = new();
        private readonly ToolStripButton _left = new();
        private readonly ToolStripButton _center = new();
        private readonly ToolStripButton _right = new();
        private readonly ToolStripButton _justify = new();
        private readonly ToolStripButton _clear = new();
        private readonly ToolStripSeparator _fixSeparator = new() { Available = false };
        private readonly ToolStripButton _fix = new() { Name = "fixTextButton", Available = false };
        private bool _updatingTools;
        private Color _lastColor = Color.Red;

        public RichDescriptionEditor()
        {
            _box.Dock = DockStyle.Fill;
            _box.Font = new Font(EditorFamily, (float)RichStyle.DefaultSize);
            _box.BackColor = Color.White;
            _box.ForeColor = Color.Black;
            _box.BorderStyle = BorderStyle.FixedSingle;
            _box.AcceptsTab = false;
            _box.DetectUrls = false;
            _box.EnableAutoDragDrop = false;
            _box.AllowDrop = false;
            _box.ShowSelectionMargin = true;
            _box.HideSelection = false;
            _box.Multiline = true;
            _box.ScrollBars = RichTextBoxScrollBars.Vertical;
            _box.TextChanged += (_, _) =>
            {
                UpdateUndoButtons();
                TextChanged?.Invoke(this, EventArgs.Empty);
            };
            _box.SelectionChanged += (_, _) =>
            {
                UpdateTools();
                UpdateUndoButtons();
            };

            _tools.GripStyle = ToolStripGripStyle.Hidden;
            _tools.Dock = DockStyle.Top;
            _tools.ImageScalingSize = new Size(16, 16);
            _tools.CanOverflow = true;

            // Undo and redo take back and bring again what was typed or done with the buttons (the Rich Text Box keeps the steps; Ctrl+Z and Ctrl+Y are the same)
            SetupHistory(_undo, "↶", BookStrings.Undo, () => _box.Undo());
            SetupHistory(_redo, "↷", BookStrings.Redo, () => _box.Redo());

            Setup(_bold, "B", BookStrings.Bold, FontStyle.Bold, () => ToggleStyle(FontStyle.Bold));
            Setup(_italic, "I", BookStrings.Italic, FontStyle.Italic, () => ToggleStyle(FontStyle.Italic));
            Setup(_underline, "U", BookStrings.Underline, FontStyle.Underline, () => ToggleStyle(FontStyle.Underline));
            Setup(_strike, "S", BookStrings.Strike, FontStyle.Strikeout, () => ToggleStyle(FontStyle.Strikeout));

            _size.DropDownStyle = ComboBoxStyle.DropDown;
            _size.AutoSize = false;
            _size.Width = 52;
            _size.ToolTipText = BookStrings.FontSize;
            foreach (var size in Sizes)
                _size.Items.Add(size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            _size.SelectedIndexChanged += (_, _) => ApplySizeFromBox();
            _size.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ApplySizeFromBox();
                    _box.Focus();
                    e.Handled = e.SuppressKeyPress = true;
                }
            };

            _color.Text = "A";
            _color.Font = new Font(Font, FontStyle.Bold);
            _color.ToolTipText = BookStrings.TextColor;
            _color.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _color.ButtonClick += (_, _) => ApplyColor(_lastColor);
            BuildColorMenu();

            Setup(_left, "≡", BookStrings.AlignLeft, null, () => SetAlignment(RichAlignment.Left));
            Setup(_center, "≡", BookStrings.AlignCenter, null, () => SetAlignment(RichAlignment.Center));
            Setup(_right, "≡", BookStrings.AlignRight, null, () => SetAlignment(RichAlignment.Right));
            Setup(_justify, "≡", BookStrings.AlignJustify, null, () => SetAlignment(RichAlignment.Justify));
            foreach (var (button, alignment) in AlignmentButtons)
            {
                button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                button.Image = AlignmentPicture(alignment, SystemColors.ControlText);
            }

            _clear.Text = "T✕";
            _clear.ToolTipText = BookStrings.ClearFormatting;
            _clear.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _clear.Click += (_, _) => ClearFormatting();

            // The button of the AI (a host that has something to correct the text with shows it, see FixVisible): it only says it was pressed
            _fix.DisplayStyle = ToolStripItemDisplayStyle.Image;
            _fix.AutoToolTip = false;
            _fix.Image = SparklePicture(false);
            _fix.Click += (_, _) => FixRequested?.Invoke(this, EventArgs.Empty);

            _tools.Items.AddRange([_undo, _redo, _undoSeparator, _bold, _italic, _underline, _strike, new ToolStripSeparator(), _size, _color, new ToolStripSeparator(), _left, _center, _right, _justify, new ToolStripSeparator(), _clear, _fixSeparator, _fix]);

            Controls.Add(_box);
            Controls.Add(_tools);
            UpdateTools();
            UpdateUndoButtons();
        }

        /// <summary>The Undo button (the arrow that turns left).</summary>
        public ToolStripButton UndoButton => _undo;

        /// <summary>The Redo button (the arrow that turns right).</summary>
        public ToolStripButton RedoButton => _redo;

        /// <summary>Whether the Undo and Redo buttons are at the start of the buttons.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool UndoVisible
        {
            get => _undo.Available;
            set => _undo.Available = _redo.Available = _undoSeparator.Available = value;
        }

        /// <summary>
        /// Forgets what can be undone: what the host put in the box (the text of another page) is not something the person typed, and undoing it would take the page away. Called when a text is put in.
        /// </summary>
        public void ClearHistory()
        {
            _box.ClearUndo();
            UpdateUndoButtons();
        }

        /// <summary>
        /// A step of the history that many changes belong to: what is changed in the box until the result is disposed is taken back (and brought again) by one Undo. For a host that changes the
        /// text in many places at once.
        /// </summary>
        public IDisposable UndoStep() => new UndoGroup(_box);

        /// <summary>Raised when the text or its look changes.</summary>
        public new event EventHandler? TextChanged;

        /// <summary>Raised when the AI button is pressed (it is shown when <see cref="FixVisible"/> is true).</summary>
        public event EventHandler? FixRequested;

        /// <summary>The button of the AI that corrects the text: the host words its tip.</summary>
        public ToolStripButton FixButton => _fix;

        /// <summary>Whether the AI button is shown at the end of the buttons (a book's descriptions have none).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool FixVisible
        {
            get => _fix.Available; // what was set, whether or not the window is on the screen yet
            set => _fix.Available = _fixSeparator.Available = value;
        }

        /// <summary>The text as RTF.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Rtf
        {
            get => _box.Rtf ?? string.Empty;
            set
            {
                // A box that cannot be edited (a scan is at work on it) takes no picture from RTF, which it takes as an object put into the text: it can be edited while the text is put in
                var readOnly = _box.ReadOnly;
                if (readOnly)
                    _box.ReadOnly = false;

                try
                {
                    // Not RTF (an empty text, or plain text): it is put in as it is; the text typed after it is in the look of the box
                    if (string.IsNullOrWhiteSpace(value))
                        _box.Clear();
                    else if (value.AsSpan().TrimStart().StartsWith(@"{\rtf", StringComparison.Ordinal))
                        _box.Rtf = value;
                    else
                        _box.Text = value;
                }
                finally
                {
                    if (readOnly)
                        _box.ReadOnly = true;
                }

                ClearHistory(); // what is put in is the start of the text, not a change of the person: Undo does not go back past it
                UpdateTools(); // the buttons show the look of the text at the start of what was put in
            }
        }

        /// <summary>The text of the box with nothing of its look.</summary>
        public string PlainText => _box.Text;

        /// <summary>True when there is no text, or only blanks, and no picture (the text of a box shows a picture as a blank).</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(_box.Text) && !(_box.TextLength > 0 && (_box.Rtf ?? string.Empty).Contains(@"\pict", StringComparison.Ordinal));

        public RichTextBox Box => _box;

        public void SetEditable(bool editable)
        {
            _box.ReadOnly = !editable;
            _tools.Enabled = editable;
            _box.BackColor = editable ? Color.White : Color.FromArgb(235, 235, 235);
        }

        private IEnumerable<(ToolStripButton Button, RichAlignment Alignment)> AlignmentButtons =>
            [(_left, RichAlignment.Left), (_center, RichAlignment.Center), (_right, RichAlignment.Right), (_justify, RichAlignment.Justify)];

        /// <summary>Gives the buttons the colours of the theme (the paper stays white, with black text: the theme paints a Rich Text Box in its own colours, which are not those of paper).</summary>
        public void ApplyTheme(bool dark)
        {
            if (_box.ForeColor != Color.Black)
                _box.ForeColor = Color.Black; // done before there is text: on a text that is there it would take the colour off the words that have one
            if (!_box.ReadOnly && _box.BackColor != Color.White)
                _box.BackColor = Color.White;

            foreach (var (button, alignment) in AlignmentButtons)
            {
                var old = button.Image;
                button.Image = AlignmentPicture(alignment, dark ? ThemeManager.DarkFore : SystemColors.ControlText);
                old?.Dispose();
            }

            var oldSparkle = _fix.Image;
            _fix.Image = SparklePicture(dark);
            oldSparkle?.Dispose();

            _tools.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
            _tools.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            _tools.Renderer = dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
            foreach (ToolStripItem item in _tools.Items)
            {
                item.BackColor = _tools.BackColor;
                item.ForeColor = _tools.ForeColor;
            }

            _size.ComboBox!.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            _size.ComboBox.ForeColor = _tools.ForeColor;
            BackColor = dark ? ThemeManager.DarkBack : ThemeManager.LightBack;
            Repaint();
        }

        private void Repaint() => _box.Invalidate();

        public override string ToString() => $"{nameof(RichDescriptionEditor)}: {_box.Text.Length} characters";

        // ── The buttons ─────────────────────────────────────────────────────

        private void Setup(ToolStripButton button, string text, string tip, FontStyle? style, Action click)
        {
            button.Text = text;
            button.ToolTipText = tip;
            button.AutoToolTip = false;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.CheckOnClick = false;
            if (style is { } look)
                button.Font = new Font(Font.FontFamily, 9f, look);
            button.Click += (_, _) =>
            {
                if (_updatingTools)
                    return;
                click();
                _box.Focus();
            };
        }

        /// <summary>A button of the history: pressing it goes one step back or forward, and the buttons are on while there is a step to go.</summary>
        private void SetupHistory(ToolStripButton button, string glyph, string tip, Action step)
        {
            button.Text = glyph;
            button.ToolTipText = tip;
            button.AccessibleName = tip;
            button.AutoToolTip = false;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Font = new Font("Segoe UI Symbol", 13f, FontStyle.Bold); // the arrows are not in every font, and are small at the size of the text
            button.Click += (_, _) =>
            {
                step();
                _box.Focus();
                UpdateUndoButtons();
            };
        }

        private void UpdateUndoButtons()
        {
            _undo.Enabled = _box.CanUndo;
            _redo.Enabled = _box.CanRedo;
        }

        private void UpdateTools()
        {
            _updatingTools = true;
            try
            {
                var font = _box.SelectionFont;
                _bold.Checked = font?.Bold ?? false;
                _italic.Checked = font?.Italic ?? false;
                _underline.Checked = font?.Underline ?? false;
                _strike.Checked = font?.Strikeout ?? false;

                if (!_size.Focused)
                    _size.Text = font is null ? string.Empty : font.SizeInPoints.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

                var alignment = GetAlignment();
                _left.Checked = alignment == RichAlignment.Left;
                _center.Checked = alignment == RichAlignment.Center;
                _right.Checked = alignment == RichAlignment.Right;
                _justify.Checked = alignment == RichAlignment.Justify;
            }
            finally
            {
                _updatingTools = false;
            }
        }

        /// <summary>The picture of a button of alignment: lines of text set left, centred, right, or filling the width.</summary>
        private static Bitmap AlignmentPicture(RichAlignment alignment, Color color)
        {
            var bitmap = new Bitmap(16, 16);
            using var graphics = Graphics.FromImage(bitmap);
            using var pen = new Pen(color, 1.5f);
            int[] widths = alignment == RichAlignment.Justify ? [12, 12, 12, 12] : [12, 8, 12, 6];
            for (var line = 0; line < 4; line++)
            {
                var width = widths[line];
                var x = alignment switch
                {
                    RichAlignment.Center => (16 - width) / 2,
                    RichAlignment.Right => 14 - width,
                    _ => 2,
                };
                graphics.DrawLine(pen, x, 3 + line * 3, x + width, 3 + line * 3);
            }

            return bitmap;
        }

        /// <summary>The picture of the AI button: a big four-pointed star with two small ones, the way an AI is drawn; in violet, a lighter one on the dark theme.</summary>
        private static Bitmap SparklePicture(bool dark)
        {
            var bitmap = new Bitmap(16, 16);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var big = new SolidBrush(dark ? Color.FromArgb(190, 160, 255) : Color.FromArgb(110, 60, 200));
            using var small = new SolidBrush(dark ? Color.FromArgb(255, 205, 90) : Color.FromArgb(225, 150, 20));
            Star(graphics, big, 6.2f, 9.2f, 6f);
            Star(graphics, small, 12.8f, 3.4f, 3f);
            Star(graphics, small, 13f, 12.8f, 2.2f);
            return bitmap;
        }

        /// <summary>A star of four points with hollow sides, <paramref name="radius"/> from its centre to a point.</summary>
        private static void Star(Graphics graphics, Brush brush, float x, float y, float radius)
        {
            var waist = radius * 0.28f;
            PointF[] points =
            [
                new(x, y - radius), new(x + waist, y - waist), new(x + radius, y), new(x + waist, y + waist),
                new(x, y + radius), new(x - waist, y + waist), new(x - radius, y), new(x - waist, y - waist),
            ];
            graphics.FillPolygon(brush, points);
        }

        /// <summary>
        /// Replaces words of the text, from the last to the first so that the places of the others stay right, and keeps the look of the rest: a replaced word has the look of its first letter. A box that
        /// cannot be edited (the host is working on it) takes them too: the changes are made by the program, not typed. The caret stays where it was.
        /// </summary>
        public void ApplyEdits(IEnumerable<PhotoLiker.Manuscripts.TextEdit> edits)
        {
            var ordered = edits.OrderByDescending(edit => edit.Index).ToList();
            if (ordered.Count == 0)
                return;

            var caret = _box.SelectionStart;
            SendMessage(_box.Handle, WM_SETREDRAW, 0, 0);
            using var undo = new UndoGroup(_box); // one Ctrl+Z takes all the changes back
            try
            {
                foreach (var edit in ordered)
                {
                    _box.Select(edit.Index, edit.Length);
                    _box.SelectedText = edit.Replacement;
                }

                _box.Select(Math.Min(caret, _box.TextLength), 0);
            }
            finally
            {
                SendMessage(_box.Handle, WM_SETREDRAW, 1, 0);
                _box.Invalidate();
            }
        }

        // ── The look of the text ────────────────────────────────────────────

        /// <summary>
        /// Changes the font of what is picked: of the typing position when nothing is picked. A pick of text in more than one look (a bold word in the middle) has no single font, so it
        /// is changed run by run: each run keeps its other looks and gets only the change.
        /// </summary>
        internal void ChangeFont(Func<Font, Font> change)
        {
            if (_box.SelectionLength == 0)
            {
                _box.SelectionFont = change(_box.SelectionFont ?? _box.Font);
                return;
            }

            // One look all through the pick: one change. (SelectionFont is not null for a pick that differs only in bold or italic, so the mask of Rich Edit says whether it is one look.)
            if (IsOneLook() && _box.SelectionFont is { } single)
            {
                _box.SelectionFont = change(single);
                return;
            }

            var start = _box.SelectionStart;
            var end = start + _box.SelectionLength;
            SendMessage(_box.Handle, WM_SETREDRAW, 0, 0);
            try
            {
                var position = start;
                while (position < end)
                {
                    _box.Select(position, 1);
                    var font = _box.SelectionFont ?? _box.Font;
                    var next = position + 1;
                    while (next < end)
                    {
                        _box.Select(next, 1);
                        if (_box.SelectionFont is not { } other || !SameLook(other, font))
                            break;
                        next++;
                    }

                    _box.Select(position, next - position);
                    _box.SelectionFont = change(font);
                    position = next;
                }

                _box.Select(start, end - start);
            }
            finally
            {
                SendMessage(_box.Handle, WM_SETREDRAW, 1, 0);
                _box.Invalidate();
            }
        }

        /// <summary>True when everything that is picked is in one font, size, and bold, italic, underline and strike-through setting (Rich Edit clears the bit of an attribute that is not the same all through).</summary>
        private bool IsOneLook()
        {
            if (!_box.IsHandleCreated)
                return true;

            var format = new CharFormat { cbSize = Marshal.SizeOf<CharFormat>(), szFaceName = string.Empty };
            SendMessage(_box.Handle, EM_GETCHARFORMAT, SCF_SELECTION, ref format);
            const uint all = CFM_BOLD | CFM_ITALIC | CFM_UNDERLINE | CFM_STRIKEOUT | CFM_SIZE | CFM_FACE;
            return (format.dwMask & all) == all;
        }

        private static bool SameLook(Font a, Font b) => a.Style == b.Style && a.SizeInPoints == b.SizeInPoints && a.FontFamily.Name == b.FontFamily.Name;

        /// <summary>Turns a look on, or off when the text is in it already (for a pick in more than one look: on, unless the first letter has it).</summary>
        internal void ToggleStyle(FontStyle style)
        {
            var current = _box.SelectionFont;
            if (current is null && _box.SelectionLength > 0)
            {
                var length = _box.SelectionLength;
                var start = _box.SelectionStart;
                _box.Select(start, 1);
                current = _box.SelectionFont;
                _box.Select(start, length);
            }

            var on = !(current?.Style.HasFlag(style) ?? false);
            ChangeFont(font => new Font(font, on ? font.Style | style : font.Style & ~style));
            UpdateTools();
        }

        internal void SetSize(float points)
        {
            if (points is < 4 or > 200)
                return;

            ChangeFont(font => new Font(font.FontFamily, points, font.Style, GraphicsUnit.Point));
            UpdateTools();
        }

        private void ApplySizeFromBox()
        {
            if (_updatingTools)
                return;

            if (float.TryParse(_size.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points) ||
                float.TryParse(_size.Text, out points))
                SetSize(points);
        }

        internal void ApplyColor(Color? color)
        {
            _box.SelectionColor = color ?? Color.Black;
            if (color is { } chosen)
                _lastColor = chosen;
            _box.Focus();
        }

        private void BuildColorMenu()
        {
            var automatic = new ToolStripMenuItem(BookStrings.ColorAutomatic);
            automatic.Click += (_, _) => ApplyColor(null);
            _color.DropDownItems.Add(automatic);

            foreach (var (name, value) in new[]
            {
                ("Red", Color.FromArgb(200, 30, 30)), ("Orange", Color.FromArgb(230, 120, 20)), ("Green", Color.FromArgb(30, 140, 50)), ("Blue", Color.FromArgb(30, 90, 180)),
                ("Purple", Color.FromArgb(120, 50, 160)), ("Grey", Color.FromArgb(110, 110, 110)),
            })
            {
                var swatch = new Bitmap(16, 16);
                using (var graphics = Graphics.FromImage(swatch))
                using (var brush = new SolidBrush(value))
                    graphics.FillRectangle(brush, 1, 1, 14, 14);

                var item = new ToolStripMenuItem(name, swatch) { Tag = value };
                item.Click += (_, _) => ApplyColor(value);
                _color.DropDownItems.Add(item);
            }

            var more = new ToolStripMenuItem(BookStrings.ColorMore);
            more.Click += (_, _) =>
            {
                using var dialog = new ColorDialog { FullOpen = true, Color = _lastColor };
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    ApplyColor(dialog.Color);
            };
            _color.DropDownItems.Add(new ToolStripSeparator());
            _color.DropDownItems.Add(more);
        }

        /// <summary>Takes the formatting off what is picked (the whole text when nothing is): the plain look, the usual size, left, no indent.</summary>
        internal void ClearFormatting()
        {
            if (_box.SelectionLength == 0)
                _box.SelectAll();

            _box.SelectionFont = new Font(EditorFamily, (float)RichStyle.DefaultSize, FontStyle.Regular);
            _box.SelectionColor = Color.Black;
            _box.SelectionIndent = 0;
            _box.SelectionHangingIndent = 0;
            SetAlignment(RichAlignment.Left);
            _box.Focus();
            UpdateTools();
        }

        // ── Where the lines go ──────────────────────────────────────────────

        internal enum RichAlignment
        {
            Left = 1,
            Right = 2,
            Center = 3,
            Justify = 4,
        }

        private const int WM_SETREDRAW = 0x000B;
        private const int EM_GETPARAFORMAT = 0x043D;
        private const int EM_SETPARAFORMAT = 0x0447;
        private const uint PFM_ALIGNMENT = 0x00000008;
        private const int EM_GETCHARFORMAT = 0x043A;
        private const int SCF_SELECTION = 0x0001;
        private const uint CFM_BOLD = 0x00000001;
        private const uint CFM_ITALIC = 0x00000002;
        private const uint CFM_UNDERLINE = 0x00000004;
        private const uint CFM_STRIKEOUT = 0x00000008;
        private const uint CFM_SIZE = 0x80000000;
        private const uint CFM_FACE = 0x20000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CharFormat
        {
            public int cbSize;
            public uint dwMask;
            public uint dwEffects;
            public int yHeight;
            public int yOffset;
            public int crTextColor;
            public byte bCharSet;
            public byte bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szFaceName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, ref CharFormat format);

        [StructLayout(LayoutKind.Sequential)]
        private struct ParaFormat2
        {
            public int cbSize;
            public uint dwMask;
            public ushort wNumbering;
            public ushort wEffects;
            public int dxStartIndent;
            public int dxRightIndent;
            public int dxOffset;
            public ushort wAlignment;
            public short cTabCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] rgxTabs;
            public int dySpaceBefore;
            public int dySpaceAfter;
            public int dyLineSpacing;
            public short sStyle;
            public byte bLineSpacingRule;
            public byte bOutlineLevel;
            public ushort wShadingWeight;
            public ushort wShadingStyle;
            public ushort wNumberingStart;
            public ushort wNumberingStyle;
            public ushort wNumberingTab;
            public ushort wBorderSpace;
            public ushort wBorderWidth;
            public ushort wBorders;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, int lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, ref ParaFormat2 format);

        private static ParaFormat2 NewFormat() => new() { cbSize = Marshal.SizeOf<ParaFormat2>(), rgxTabs = new int[32] };

        /// <summary>The alignment of the paragraph the pick starts in (Rich Text Box itself knows left, centre and right only).</summary>
        internal RichAlignment GetAlignment()
        {
            if (!_box.IsHandleCreated)
                return RichAlignment.Left;

            var format = NewFormat();
            format.dwMask = PFM_ALIGNMENT;
            SendMessage(_box.Handle, EM_GETPARAFORMAT, 0, ref format);
            return Enum.IsDefined((RichAlignment)format.wAlignment) ? (RichAlignment)format.wAlignment : RichAlignment.Left;
        }

        /// <summary>Sets the alignment of the paragraphs of the pick, justified too.</summary>
        internal void SetAlignment(RichAlignment alignment)
        {
            _box.CreateControl();
            var format = NewFormat();
            format.dwMask = PFM_ALIGNMENT;
            format.wAlignment = (ushort)alignment;
            SendMessage(_box.Handle, EM_SETPARAFORMAT, 0, ref format);
            UpdateTools();
            TextChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── One step of undo for many changes ───────────────────────────────

        private const int EM_GETOLEINTERFACE = 0x043C;

        /// <summary>
        /// The Text Object Model of Rich Edit, which can group the changes that are made while a group is open into one step of Undo. Only the two methods that are used are given their names; the
        /// others are there to keep their places in the table of methods of the interface.
        /// </summary>
        [ComImport, Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface ITextDocument
        {
            void GetName();
            void GetSelection();
            void GetStoryCount();
            void GetStoryRanges();
            void GetSaved();
            void SetSaved();
            void GetDefaultTabStop();
            void SetDefaultTabStop();
            void New();
            void Open();
            void Save();
            void Freeze();
            void Unfreeze();

            [PreserveSig]
            int BeginEditCollection();

            [PreserveSig]
            int EndEditCollection();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, out IntPtr lParam);

        /// <summary>
        /// While it is open, what is done to the text of the box is one step of Undo (a fix with thirty words changed is taken back by one Ctrl+Z, not thirty). When the box has no Text Object Model to
        /// ask, the changes are separate steps, as they are without a group.
        /// </summary>
        private sealed class UndoGroup : IDisposable
        {
            private ITextDocument? _document;

            public UndoGroup(RichTextBox box)
            {
                IntPtr ole = IntPtr.Zero, document = IntPtr.Zero;
                try
                {
                    SendMessage(box.Handle, EM_GETOLEINTERFACE, IntPtr.Zero, out ole);
                    var id = typeof(ITextDocument).GUID;
                    if (ole == IntPtr.Zero || Marshal.QueryInterface(ole, in id, out document) != 0 || document == IntPtr.Zero)
                        return;

                    var opened = (ITextDocument)Marshal.GetObjectForIUnknown(document);
                    if (opened.BeginEditCollection() == 0)
                        _document = opened;
                    else
                        Marshal.ReleaseComObject(opened);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or MarshalDirectiveException)
                {
                    _document = null; // no group: the changes are steps of their own
                }
                finally
                {
                    if (document != IntPtr.Zero)
                        Marshal.Release(document);
                    if (ole != IntPtr.Zero)
                        Marshal.Release(ole);
                }
            }

            public void Dispose()
            {
                if (_document is not { } open)
                    return;

                _document = null;
                try
                {
                    open.EndEditCollection();
                }
                finally
                {
                    Marshal.ReleaseComObject(open);
                }
            }
        }
    }
}
