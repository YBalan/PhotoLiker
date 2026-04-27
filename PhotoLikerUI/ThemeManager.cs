namespace PhotoLikerUI
{
    internal static class ThemeManager
    {
        // ── colour palettes ────────────────────────────────────────────────
        public static readonly Color DarkBack      = Color.FromArgb(32,  32,  32);
        public static readonly Color DarkBackAlt   = Color.FromArgb(45,  45,  48);
        public static readonly Color DarkFore      = Color.FromArgb(220, 220, 220);
        public static readonly Color DarkBorder    = Color.FromArgb(63,  63,  70);
        public static readonly Color DarkHighlight = Color.FromArgb(0,   122, 204);

        public static readonly Color LightBack      = SystemColors.Control;
        public static readonly Color LightBackAlt   = SystemColors.Window;
        public static readonly Color LightFore      = SystemColors.ControlText;
        public static readonly Color LightBorder    = SystemColors.ControlDark;
        public static readonly Color LightHighlight = SystemColors.Highlight;

        // ── public entry point ─────────────────────────────────────────────
        public static void Apply(Form form, bool dark)
        {
            ApplyToControl(form, dark);
            ApplyToolStrips(form, dark);
            ApplyPropertyGrids(form, dark);
        }

        // ── recursive control walk ─────────────────────────────────────────
        private static void ApplyToControl(Control ctrl, bool dark)
        {
            // Skip controls whose colours should stay neutral
            if (ctrl is PictureBox) return;

            Color back = dark ? DarkBackAlt  : LightBack;
            Color fore = dark ? DarkFore     : LightFore;

            // Panels / forms get the primary background
            if (ctrl is Form || ctrl is Panel || ctrl is SplitContainer ||
                ctrl is TabControl || ctrl is TabPage)
                back = dark ? DarkBack : LightBack;

            ctrl.BackColor = back;
            ctrl.ForeColor = fore;

            // SplitContainer itself doesn't host children directly
            if (ctrl is SplitContainer sc)
            {
                ApplyToControl(sc.Panel1, dark);
                ApplyToControl(sc.Panel2, dark);
                return;
            }

            foreach (Control child in ctrl.Controls)
                ApplyToControl(child, dark);
        }

        // ── ToolStrip / StatusStrip / MenuStrip ────────────────────────────
        private static void ApplyToolStrips(Control root, bool dark)
        {
            foreach (Control ctrl in Flatten(root))
            {
                if (ctrl is ToolStrip ts)
                {
                    if (dark)
                    {
                        ts.BackColor = DarkBackAlt;
                        ts.ForeColor = DarkFore;
                        ts.Renderer  = new DarkToolStripRenderer();
                    }
                    else
                    {
                        ts.BackColor = LightBack;
                        ts.ForeColor = LightFore;
                        ts.Renderer  = new ToolStripProfessionalRenderer();
                    }

                    foreach (ToolStripItem item in ts.Items)
                        ApplyToolStripItem(item, dark);
                }
            }
        }

        private static void ApplyToolStripItem(ToolStripItem item, bool dark)
        {
            item.BackColor = dark ? DarkBackAlt : LightBack;
            item.ForeColor = dark ? DarkFore    : LightFore;

            if (item is ToolStripDropDownButton dd)
                foreach (ToolStripItem child in dd.DropDownItems)
                    ApplyToolStripItem(child, dark);
        }

        // ── PropertyGrid ───────────────────────────────────────────────────
        private static void ApplyPropertyGrids(Control root, bool dark)
        {
            foreach (Control ctrl in Flatten(root))
            {
                if (ctrl is not PropertyGrid pg) continue;

                if (dark)
                {
                    pg.BackColor              = DarkBack;
                    pg.LineColor              = DarkBorder;
                    pg.CategoryForeColor      = DarkFore;
                    pg.ViewBackColor          = DarkBackAlt;
                    pg.ViewForeColor          = DarkFore;
                    pg.HelpBackColor          = DarkBack;
                    pg.HelpForeColor          = DarkFore;
                    pg.CommandsBackColor      = DarkBack;
                    pg.CommandsForeColor      = DarkFore;
                }
                else
                {
                    pg.BackColor              = LightBack;
                    pg.LineColor              = LightBorder;
                    pg.CategoryForeColor      = LightFore;
                    pg.ViewBackColor          = LightBackAlt;
                    pg.ViewForeColor          = LightFore;
                    pg.HelpBackColor          = LightBack;
                    pg.HelpForeColor          = LightFore;
                    pg.CommandsBackColor      = LightBack;
                    pg.CommandsForeColor      = LightFore;
                }
            }
        }

        // ── helpers ────────────────────────────────────────────────────────
        private static IEnumerable<Control> Flatten(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (Control descendant in Flatten(child))
                    yield return descendant;
        }
    }

    // ── custom dark renderer for ToolStrip ─────────────────────────────────
    internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer()
            : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(ThemeManager.DarkBackAlt);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is ToolStripButton { Checked: true })
            {
                var r = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
                using var b = new SolidBrush(ThemeManager.DarkHighlight);
                e.Graphics.FillRectangle(b, r);
                return;
            }
            base.OnRenderButtonBackground(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = ThemeManager.DarkFore;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int x = e.Item.Width / 2;
            using var pen = new Pen(ThemeManager.DarkBorder);
            e.Graphics.DrawLine(pen, x, 4, x, e.Item.Height - 4);
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripBorder             => ThemeManager.DarkBorder;
        public override Color ButtonSelectedHighlight     => ThemeManager.DarkHighlight;
        public override Color ButtonPressedHighlight      => ThemeManager.DarkHighlight;
        public override Color ButtonCheckedHighlight      => ThemeManager.DarkHighlight;
        public override Color ButtonSelectedGradientBegin => ThemeManager.DarkBorder;
        public override Color ButtonSelectedGradientEnd   => ThemeManager.DarkBorder;
        public override Color ButtonPressedGradientBegin  => ThemeManager.DarkHighlight;
        public override Color ButtonPressedGradientEnd    => ThemeManager.DarkHighlight;
        public override Color SeparatorDark               => ThemeManager.DarkBorder;
        public override Color SeparatorLight              => ThemeManager.DarkBack;
    }
}
