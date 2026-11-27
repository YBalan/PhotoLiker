namespace PhotoLikerUI
{
    /// <summary>
    /// 3D models in the viewer (see <see cref="ModelViewController"/>) and Photo Liker as a program Windows opens .stl files with (see <see cref="StlAssociation"/>). With
    /// <see cref="Settings.SupportModels"/> on (it is by default) the STL files of the folder are in the list with the photos: the thumbnail is a picture of the model, the viewer shows the
    /// model, which is turned with the mouse, and the grid under it says how many triangles it has, how big it is and what it holds. It is liked, sorted into groups and deleted like a
    /// photo; the tools that work on the pixels of a photo (drawing, turning, tracing ...) do not work on it.
    /// </summary>
    public partial class MainForm
    {
        private ModelViewController _model = null!;
        private ToolStripButton _stlButton = null!;

        private void InitializeModel()
        {
            _model = new ModelViewController(
                () => pictureBox1.ClientSize,
                image => pictureBox1.Image = image,
                () => pictureBox1.Invalidate(),
                () => DeviceDpi / 96f,
                () => Font);
            _model.DrawFailed += ex => SetStatus(string.Format(AppStrings.StatusImageLoadError, ex.Message));

            pictureBox1.Resize += (_, _) => _model.Resized();
            pictureBox1.MouseDoubleClick += (_, e) =>
            {
                // A double click on the model starts over; on one of its buttons it is only two clicks
                if (e.Button == MouseButtons.Left && _model.Active && !_model.IsOverButton(e.Location))
                    _model.Reset();
            };

            // Beside the button of the context menu: both are about what Windows does with the program
            _stlButton = new ToolStripButton(ModelStrings.ButtonText)
            {
                Name = "stlToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.StlFiles(),
            };
            _stlButton.Click += (_, _) => StlButtonClicked();
            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(contextMenuToolStripButton) + 1, _stlButton);
            UpdateStlButton();
        }

        private void ApplyModelLanguage()
        {
            UpdateStlButton();

            // The name of the kind of file (shown by Explorer) was written in the language that was on when the program was registered. Only a registration that starts this very program
            // is touched: another copy of the program (a build that is being tried, say) must not take what the person registered for the one they use.
            try
            {
                var association = CreateStlAssociation();
                if (association.IsRegistered() && association.IsCurrent())
                    association.Register(ModelStrings.FileTypeName, ModelStrings.AppDescription);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // the name stays in the old language
            }
        }

        // ── the model that is shown ────────────────────────────────────────

        /// <summary>A photo entry was shown: if it is a model, the viewer starts looking at it (the first time), else it lets go of the model it had.</summary>
        private void ShowModel(PhotoEntry entry)
        {
            if (entry.Model is { } mesh && pictureBox1.Tag is string file && MediaKinds.IsModel(file))
            {
                if (!ReferenceEquals(_model.Mesh, mesh))
                    _model.Attach(mesh);

                SetStatus(string.Format(ModelStrings.StatusShownFormat, Path.GetFileName(file), mesh.TriangleCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
            }
            else if (_model.Active)
            {
                _model.Detach();
            }
        }

        /// <summary>
        /// The program was started by opening a file: that is the photo to show first, once the folder is listed. A model that is opened is shown even where the models are off
        /// in the settings of its folder (the person asked for it by name).
        /// </summary>
        private void ShowFirstFile(string? file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return;

            _currentConfig.CurrentFilePath = file;
            if (MediaKinds.IsModel(file) && !_currentConfig.SupportModels)
                _currentConfig.SupportModels = true;
        }

        /// <summary>Tells the user a tool works on photos only, when what is shown is a 3D model. True when it is one (the caller stops).</summary>
        private bool RefuseModel(string? file)
        {
            if (!MediaKinds.IsModel(file))
                return false;

            SetStatus(ModelStrings.StatusNotAPhoto);
            return true;
        }

        // ── Windows' list of programs for .stl files ───────────────────────

        /// <summary>The registration of the program for .stl files in the registry; a test gives one that writes somewhere of its own.</summary>
        protected virtual StlAssociation CreateStlAssociation() => new(StlAssociation.ProgramPath(Application.ExecutablePath, AppContext.BaseDirectory));

        /// <summary>Opens the page of Settings where the person makes the program the default. A test answers it (it would open Settings on the desktop).</summary>
        protected virtual string? OpenDefaultAppsSettings() => StlAssociation.OpenDefaultAppsSettings();

        /// <summary>Asked when the program is registered already: yes opens Settings, no takes it out of the list, cancel does nothing. A test answers it.</summary>
        protected virtual DialogResult AskStlChoice(string question) =>
            MessageBox.Show(this, question, ModelStrings.DialogTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        private void UpdateStlButton()
        {
            bool registered, isDefault;
            try
            {
                var association = CreateStlAssociation();
                registered = association.IsRegistered();
                isDefault = registered && association.IsDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                registered = isDefault = false;
            }

            _stlButton.Text = ModelStrings.ButtonText;
            _stlButton.ToolTipText = isDefault ? ModelStrings.TooltipDefault : registered ? ModelStrings.TooltipRegistered : ModelStrings.TooltipOff;
            _stlButton.Checked = isDefault;
            LayoutRail();
        }

        private void StlButtonClicked()
        {
            try
            {
                var association = CreateStlAssociation();
                // Not registered, or registered to start another program than this one: the press makes it this program (nothing to ask, it is what was asked for)
                if (!association.IsRegistered() || !association.IsCurrent())
                {
                    association.Register(ModelStrings.FileTypeName, ModelStrings.AppDescription);
                    SetStatus(OpenDefaultAppsSettings() is null ? ModelStrings.StatusRegisteredOpened : ModelStrings.StatusRegisteredNoSettings);
                }
                else
                {
                    switch (AskStlChoice(association.IsDefault() ? ModelStrings.QuestionDefault : ModelStrings.QuestionRegistered))
                    {
                        case DialogResult.Yes:
                            SetStatus(OpenDefaultAppsSettings() is null ? ModelStrings.StatusSettingsOpened : ModelStrings.StatusRegisteredNoSettings);
                            break;
                        case DialogResult.No:
                            association.Unregister();
                            SetStatus(ModelStrings.StatusUnregistered);
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                SetStatus(string.Format(ModelStrings.StatusAssociationErrorFormat, ex.Message));
            }

            UpdateStlButton();
        }
    }
}
