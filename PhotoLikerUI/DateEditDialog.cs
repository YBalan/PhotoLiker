namespace PhotoLikerUI
{
    /// <summary>A small dialog to pick a date and a time: the "..." of a date row of the metadata grid.</summary>
    internal sealed class DateEditDialog : Form
    {
        private readonly DateTimePicker _picker = new() { Format = DateTimePickerFormat.Custom, CustomFormat = DateText.DisplayFormat, Width = 230 };
        private readonly Button _ok = new() { Text = MainFormStrings.DateEditOk, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.OK };
        private readonly Button _cancel = new() { Text = MainFormStrings.DateEditCancel, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.Cancel };

        public DateEditDialog(string title, DateTime start, bool dark)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(300, 110);

            _picker.Value = start >= _picker.MinDate && start <= _picker.MaxDate ? start : DateTime.Now;
            _picker.Location = new Point(35, 18);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(10, 6, 10, 6) };
            buttons.Controls.AddRange([_cancel, _ok]);
            Controls.Add(_picker);
            Controls.Add(buttons);

            AcceptButton = _ok;
            CancelButton = _cancel;
            ThemeManager.Apply(this, dark);
        }

        public DateTime Picked => _picker.Value;
    }

    /// <summary>
    /// The "..." button of a date row of the metadata grid: a date and time picker. The date can also be typed into the row; either way
    /// the grid hands the new text on, and the photo is saved with it (see <see cref="DateRowEdit"/>).
    /// </summary>
    internal sealed class DateValueEditor : System.Drawing.Design.UITypeEditor
    {
        public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(System.ComponentModel.ITypeDescriptorContext? context) =>
            System.Drawing.Design.UITypeEditorEditStyle.Modal;

        public override object? EditValue(System.ComponentModel.ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            var text = value as string;
            var start = DateText.TryParseTyped(text, out var typed) || DateText.TryFindDisplayed(text, out typed) ? typed : DateTime.Now;
            var dark = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.IsDarkTheme ?? false;

            using var dialog = new DateEditDialog(context?.PropertyDescriptor?.DisplayName ?? MainFormStrings.DateEditTitle, start, dark);
            return dialog.ShowDialog() == DialogResult.OK ? DateText.Display(dialog.Picked) : value;
        }
    }
}
