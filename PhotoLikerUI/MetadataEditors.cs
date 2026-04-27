namespace PhotoLikerUI
{
    using System.ComponentModel;

    internal class UrlLauncherEditor : System.Drawing.Design.UITypeEditor
    {
        public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(
            ITypeDescriptorContext? context)
            => System.Drawing.Design.UITypeEditorEditStyle.Modal;

        public override object? EditValue(
            ITypeDescriptorContext? context,
            IServiceProvider provider,
            object? value)
        {
            var url = value?.ToString();
            if (!string.IsNullOrWhiteSpace(url))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            return value;
        }
    }

    internal class MultilineTextViewEditor : System.Drawing.Design.UITypeEditor
    {
        public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(
            ITypeDescriptorContext? context)
            => System.Drawing.Design.UITypeEditorEditStyle.Modal;

        public override object? EditValue(
            ITypeDescriptorContext? context,
            IServiceProvider provider,
            object? value)
        {
            using var form = new Form
            {
                Text          = context?.PropertyDescriptor?.DisplayName ?? "Value",
                Size          = new Size(700, 450),
                MinimumSize   = new Size(400, 250),
                StartPosition = FormStartPosition.CenterParent,
            };

            var tb = new TextBox
            {
                Multiline  = true,
                ReadOnly   = true,
                Dock       = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap   = false,
                Text       = value?.ToString() ?? string.Empty,
                Font       = new Font("Consolas", 9f),
                BackColor  = SystemColors.Window,
            };

            var btnCopy = new Button
            {
                Text   = "Copy All",
                Dock   = DockStyle.Bottom,
                Height = 30,
            };
            btnCopy.Click += (_, _) => Clipboard.SetText(tb.Text);

            form.Controls.Add(tb);
            form.Controls.Add(btnCopy);
            form.ShowDialog();

            return value; // read-only: return original value unchanged
        }
    }
}
