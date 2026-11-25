using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLikerUI
{
    /// <summary>
    /// The HTTP client the AI services are asked with: one for the whole program (a client is meant to live long). It has no time limit of its own, since <see cref="CloudChat"/> limits each request itself.
    /// </summary>
    internal static class CloudHttp
    {
        public static HttpClient Shared { get; } = Create(null);

        /// <summary>
        /// What the real client sends with. It does not follow a redirect: .NET drops the <c>Authorization</c> header when a redirect goes to another host but keeps any other header, and the keys of
        /// Gemini and Claude are headers of their own (<c>x-goog-api-key</c>, <c>x-api-key</c>), so a key must never be able to go where a service sent the request on. The services do not redirect.
        /// </summary>
        internal static SocketsHttpHandler NewHandler() => new() { AllowAutoRedirect = false };

        /// <summary>A client for a handler; a test gives one that answers instead of the Internet.</summary>
        public static HttpClient Create(HttpMessageHandler? handler) =>
            handler is null ? new HttpClient(NewHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// The window where the keys of the AI services are set: one box for each of OpenAI (ChatGPT), Google (Gemini) and Anthropic (Claude), with the key, the model to ask, a button that tries the key by asking the
    /// service for its models (which also fills the list of models), a link to the page where a key is made, and a button to forget the key. What is typed is not kept until OK is pressed: then
    /// the keys go to <see cref="AiServiceSettings"/> (encrypted for the Windows account) and the window closes; a key or model that can not be one leaves the window open and says which.
    /// </summary>
    internal sealed class AiServicesDialog : Form
    {
        private static readonly Color Good = Color.FromArgb(46, 160, 67);
        private static readonly Color GoodDark = Color.FromArgb(86, 211, 100);
        private static readonly Color Bad = Color.FromArgb(207, 34, 46);
        private static readonly Color BadDark = Color.FromArgb(248, 81, 73);

        private readonly AiServiceSettings _settings;
        private readonly HttpClient _http;
        private readonly bool _dark;
        private readonly CancellationTokenSource _closing = new();
        private readonly Dictionary<AiVendor, VendorRow> _rows = [];
        private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400 };

        /// <summary>The controls of one service.</summary>
        internal sealed class VendorRow(AiVendor vendor)
        {
            public AiVendor Vendor { get; } = vendor;

            public GroupBox Group { get; } = new();
            public TextBox KeyBox { get; } = new();
            public CheckBox ShowCheck { get; } = new();
            public ComboBox ModelCombo { get; } = new();
            public Button CheckButton { get; } = new();
            public LinkLabel GetLink { get; } = new();
            public Button RemoveButton { get; } = new();
            public Label StatusLabel { get; } = new();
        }

        /// <param name="settings">The settings to show and, when OK is pressed, to change and save.</param>
        /// <param name="dark">Whether the program is in its dark theme.</param>
        /// <param name="http">What asks the services (a test gives one that answers instead of the Internet).</param>
        public AiServicesDialog(AiServiceSettings settings, bool dark, HttpClient? http = null)
        {
            _settings = settings;
            _dark = dark;
            _http = http ?? CloudHttp.Shared;

            Warn = text => MessageBox.Show(this, text, AiServicesStrings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Text = AiServicesStrings.Title;
            FormBorderStyle = FormBorderStyle.FixedSingle; // not FixedDialog: Windows shows no icon in the caption of that frame
            Icon = DialogIcons.Manuscript;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            const int width = 680;
            var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12, 10, 12, 4) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            IntroLabel.Text = AiServicesStrings.Intro;
            IntroLabel.AutoSize = true;
            IntroLabel.MaximumSize = new Size(width - 24, 0);
            IntroLabel.Margin = new Padding(0, 0, 0, 6);
            PrivacyLabel.Text = AiServicesStrings.Privacy;
            PrivacyLabel.AutoSize = true;
            PrivacyLabel.MaximumSize = new Size(width - 24, 0);
            PrivacyLabel.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(IntroLabel);
            root.Controls.Add(PrivacyLabel);

            foreach (var vendor in AiVendors.All)
            {
                var row = new VendorRow(vendor);
                _rows[vendor] = row;
                BuildRow(row, width - 24);
                root.Controls.Add(row.Group);
            }

            OkButton.Name = "aiServicesOkButton";
            OkButton.Text = AiServicesStrings.Ok;
            OkButton.Size = new Size(88, 28);
            OkButton.Click += (_, _) => Accept();
            CancelButtonControl.Name = "aiServicesCancelButton";
            CancelButtonControl.Text = AiServicesStrings.Cancel;
            CancelButtonControl.DialogResult = DialogResult.Cancel;
            CancelButtonControl.Size = new Size(88, 28);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
            buttons.Controls.Add(CancelButtonControl);
            buttons.Controls.Add(OkButton);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = OkButton;
            CancelButton = CancelButtonControl;
            ClientSize = new Size(width, root.GetPreferredSize(new Size(width, 0)).Height);

            ApplyTheme();
            Disposed += (_, _) =>
            {
                // A form can be disposed twice (a Close that disposes it, then the code that made it), and this must be done once
                if (_closing.IsCancellationRequested)
                    return;

                _closing.Cancel();
                _closing.Dispose();
                _toolTip.Dispose();
            };
        }

        internal Label IntroLabel { get; } = new();
        internal Label PrivacyLabel { get; } = new();
        internal Button OkButton { get; } = new();
        internal Button CancelButtonControl { get; } = new();

        /// <summary>The controls of each service.</summary>
        internal IReadOnlyDictionary<AiVendor, VendorRow> Rows => _rows;

        /// <summary>Opens a page of the Internet (the page where a key is made); returns why it did not work, or null. A test gives one that opens nothing.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<string, string?> OpenPage { get; set; } = ShellOpen.Url;

        /// <summary>Tells the person that the keys could not be saved (a message box); a test gives one that only notes it, as it can not answer a box.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Action<string> Warn { get; set; } = null!;

        // ── Building ────────────────────────────────────────────────────────

        private void BuildRow(VendorRow row, int width)
        {
            var group = row.Group;
            group.Name = "aiGroup_" + AiVendors.Code(row.Vendor);
            group.Text = AiVendors.Name(row.Vendor);
            group.Dock = DockStyle.Top;
            group.AutoSize = true;
            group.Padding = new Padding(8, 4, 8, 6);
            group.Margin = new Padding(0, 0, 0, 6);
            group.MinimumSize = new Size(width, 0);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 3 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var account = _settings.Get(row.Vendor);

            var keyLabel = new Label { Text = AiServicesStrings.LabelKey, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
            row.KeyBox.Name = "aiKey_" + AiVendors.Code(row.Vendor);
            row.KeyBox.UseSystemPasswordChar = true;
            row.KeyBox.MaxLength = AiServiceSettings.MaximumKeyLength;
            row.KeyBox.Dock = DockStyle.Fill;
            row.KeyBox.Text = account?.ApiKey ?? string.Empty;
            row.KeyBox.TextChanged += (_, _) => OnKeyTyped(row);
            row.ShowCheck.Name = "aiShow_" + AiVendors.Code(row.Vendor);
            row.ShowCheck.Text = AiServicesStrings.ShowKey;
            row.ShowCheck.AutoSize = true;
            row.ShowCheck.Anchor = AnchorStyles.Left;
            row.ShowCheck.CheckedChanged += (_, _) => row.KeyBox.UseSystemPasswordChar = !row.ShowCheck.Checked;
            row.RemoveButton.Name = "aiRemove_" + AiVendors.Code(row.Vendor);
            row.RemoveButton.Text = AiServicesStrings.RemoveKey;
            row.RemoveButton.AutoSize = true;
            row.RemoveButton.Padding = new Padding(4, 0, 4, 0);
            row.RemoveButton.Click += (_, _) => Forget(row);
            _toolTip.SetToolTip(row.RemoveButton, AiServicesStrings.HintRemoveKey);

            var modelLabel = new Label { Text = AiServicesStrings.LabelModel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
            row.ModelCombo.Name = "aiModel_" + AiVendors.Code(row.Vendor);
            row.ModelCombo.DropDownStyle = ComboBoxStyle.DropDown; // any name can be typed: the lists of the services change faster than this program
            row.ModelCombo.MaxLength = AiServiceSettings.MaximumModelLength;
            row.ModelCombo.Dock = DockStyle.Fill;
            row.ModelCombo.Text = _settings.ModelOf(row.Vendor);
            row.CheckButton.Name = "aiCheck_" + AiVendors.Code(row.Vendor);
            row.CheckButton.Text = AiServicesStrings.CheckKey;
            row.CheckButton.AutoSize = true;
            row.CheckButton.Padding = new Padding(4, 0, 4, 0);
            row.CheckButton.Click += async (_, _) => await CheckAsync(row);
            _toolTip.SetToolTip(row.CheckButton, AiServicesStrings.HintCheckKey);
            row.GetLink.Name = "aiGet_" + AiVendors.Code(row.Vendor);
            row.GetLink.Text = AiServicesStrings.GetKey;
            row.GetLink.AutoSize = true;
            row.GetLink.Anchor = AnchorStyles.Left;
            row.GetLink.LinkClicked += (_, _) => GetKey(row);
            _toolTip.SetToolTip(row.GetLink, AiServicesStrings.HintGetKey);

            row.StatusLabel.Name = "aiStatus_" + AiVendors.Code(row.Vendor);
            row.StatusLabel.AutoSize = true;
            row.StatusLabel.MaximumSize = new Size(width - 40, 0);
            row.StatusLabel.Margin = new Padding(0, 2, 0, 0);

            table.Controls.Add(keyLabel, 0, 0);
            table.Controls.Add(row.KeyBox, 1, 0);
            table.Controls.Add(row.ShowCheck, 2, 0);
            table.Controls.Add(row.RemoveButton, 3, 0);
            table.Controls.Add(modelLabel, 0, 1);
            table.Controls.Add(row.ModelCombo, 1, 1);
            table.Controls.Add(row.CheckButton, 2, 1);
            table.Controls.Add(row.GetLink, 3, 1);
            table.Controls.Add(row.StatusLabel, 1, 2);
            table.SetColumnSpan(row.StatusLabel, 3);
            group.Controls.Add(table);
            OnKeyTyped(row);
        }

        private void ApplyTheme()
        {
            ThemeManager.Apply(this, _dark);
            var inputBack = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            var fore = _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;

            // The theme gives every label a box of its own colour; the text should sit on what is behind it
            foreach (var control in AllControls(this))
            {
                if (control is Label or CheckBox)
                    control.BackColor = Color.Transparent;
            }

            foreach (var row in _rows.Values)
            {
                row.KeyBox.BackColor = inputBack;
                row.KeyBox.ForeColor = fore;
                row.ModelCombo.BackColor = inputBack;
                row.ModelCombo.ForeColor = fore;
                row.GetLink.LinkColor = _dark ? Color.FromArgb(121, 192, 255) : Color.FromArgb(0, 102, 204);
                row.GetLink.ActiveLinkColor = row.GetLink.LinkColor;
            }
        }

        private static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in AllControls(child))
                    yield return descendant;
            }
        }

        // ── What the buttons do ─────────────────────────────────────────────

        /// <summary>The buttons that need a key are on while there is one.</summary>
        private static void OnKeyTyped(VendorRow row)
        {
            var typed = row.KeyBox.Text.Trim().Length > 0;
            row.CheckButton.Enabled = typed;
            row.RemoveButton.Enabled = typed;
        }

        private void Say(VendorRow row, string text, bool? good)
        {
            row.StatusLabel.Text = text;
            row.StatusLabel.ForeColor = good switch
            {
                true => _dark ? GoodDark : Good,
                false => _dark ? BadDark : Bad,
                null => SystemColors.GrayText,
            };
        }

        private void Forget(VendorRow row)
        {
            row.KeyBox.Text = string.Empty;
            row.ModelCombo.Items.Clear();
            row.ModelCombo.Text = AiVendors.DefaultModel(row.Vendor);
            Say(row, AiServicesStrings.StatusRemoved, null);
        }

        /// <summary>The link: opens the page of the service where a key is made.</summary>
        internal void GetKey(VendorRow row)
        {
            if (OpenPage(AiVendors.KeysPage(row.Vendor)) is { } problem)
                Say(row, string.Format(AiServicesStrings.StatusCheckFailedFormat, problem), false);
        }

        /// <summary>Tries the key by asking the service for its models, and puts them in the list of the model box.</summary>
        internal async Task CheckAsync(VendorRow row)
        {
            var key = row.KeyBox.Text.Trim();
            if (key.Length == 0)
            {
                Say(row, AiServicesStrings.StatusNoKey, false);
                return;
            }

            if (!AiServiceSettings.IsPlausibleKey(key))
            {
                Say(row, AiServicesStrings.StatusKeyBad, false);
                return;
            }

            var typed = row.ModelCombo.Text.Trim();
            var model = typed.Length == 0 ? AiVendors.DefaultModel(row.Vendor) : typed;
            if (!AiServiceSettings.IsPlausibleModel(model))
            {
                Say(row, AiServicesStrings.StatusModelBad, false);
                return;
            }

            row.CheckButton.Enabled = false;
            Say(row, AiServicesStrings.StatusChecking, null);
            try
            {
                var models = await new CloudChat(row.Vendor, key, model, _http).ListModelsAsync(_closing.Token);
                if (IsDisposed)
                    return;

                row.ModelCombo.Items.Clear();
                row.ModelCombo.Items.AddRange([.. models]);
                row.ModelCombo.Text = model; // the Items change can clear what is typed
                Say(row, string.Format(AiServicesStrings.StatusCheckedFormat, models.Count), true);
            }
            catch (AiServiceException ex)
            {
                if (!IsDisposed)
                    Say(row, string.Format(AiServicesStrings.StatusCheckFailedFormat, ex.Message), false);
            }
            catch (OperationCanceledException)
            {
                // the window was closed
            }
            finally
            {
                if (!IsDisposed)
                    OnKeyTyped(row);
            }
        }

        /// <summary>OK: checks every key and model, then keeps them all (or tells which one can not be, and keeps nothing).</summary>
        internal void Accept()
        {
            foreach (var row in _rows.Values)
            {
                var key = row.KeyBox.Text.Trim();
                if (key.Length == 0)
                    continue;

                if (!AiServiceSettings.IsPlausibleKey(key))
                {
                    Say(row, AiServicesStrings.StatusKeyBad, false);
                    row.KeyBox.Focus();
                    return;
                }

                var model = row.ModelCombo.Text.Trim();
                if (model.Length > 0 && !AiServiceSettings.IsPlausibleModel(model))
                {
                    Say(row, AiServicesStrings.StatusModelBad, false);
                    row.ModelCombo.Focus();
                    return;
                }
            }

            foreach (var row in _rows.Values)
            {
                var key = row.KeyBox.Text.Trim();
                if (key.Length > 0)
                {
                    // The default model is kept as "no model", so a new default of a later version of the program is followed
                    var model = row.ModelCombo.Text.Trim();
                    _settings.Set(row.Vendor, key, string.Equals(model, AiVendors.DefaultModel(row.Vendor), StringComparison.Ordinal) ? null : model);
                }
                else if (_settings.HasKey(row.Vendor))
                {
                    _settings.Remove(row.Vendor); // a key of another Windows account is not touched: it is not shown, so it was not meant to go
                }
            }

            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warn(string.Format(AiServicesStrings.SaveFailedFormat, ex.Message));
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
