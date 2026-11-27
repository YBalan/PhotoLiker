using PhotoLiker.Core;
using PhotoLiker.Effects.Models;

namespace PhotoLikerUI
{
    /// <summary>The Telegram Sticker button of the toolbar: opens the window that turns the photo that is shown into a sticker (a WebP or PNG picture of 512 pixels with a see-through background).</summary>
    public partial class MainForm
    {
        private ToolStripButton _stickerButton = null!;

        private void InitializeSticker()
        {
            _stickerButton = new ToolStripButton(StickerStrings.ButtonText)
            {
                Name = "stickerToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Sticker(),
                ToolTipText = StickerStrings.ButtonTooltip,
            };
            _stickerButton.Click += (_, _) => Sticker();

            // The very last button of the toolbar, after Video to GIF (the other buttons are put in beside their neighbours, so this stays last)
            toolStrip1.Items.Add(_stickerButton);
        }

        private void ApplyStickerLanguage()
        {
            _stickerButton.Text = StickerStrings.ButtonText;
            _stickerButton.ToolTipText = StickerStrings.ButtonTooltip;
        }

        /// <summary>Where the window keeps its values (the outline, the cutout, the format) between two uses: with the other files of the program.</summary>
        internal static string StickerSettingsFile => Path.Combine(AppPaths.DataFolder, "telegram-sticker.json");

        /// <summary>Opens the Telegram Sticker window for the photo that is shown.</summary>
        private void Sticker()
        {
            if (pictureBox1.Tag is not string file || !File.Exists(file))
            {
                SetStatus(StickerStrings.StatusNoPhoto);
                return;
            }

            if (MediaKinds.IsVideo(file))
            {
                SetStatus(StickerStrings.StatusIsVideo);
                return;
            }

            if (RefuseModel(file))
                return;

            _stickerButton.Enabled = false;
            try
            {
                SetStatus(StickerStrings.StatusOpening);
                using var dialog = CreateStickerDialog(file);
                dialog.ShowDialog(this);

                if (!string.IsNullOrWhiteSpace(dialog.SavedPath) && File.Exists(dialog.SavedPath))
                    SetStatus(string.Format(StickerStrings.StatusSavedFormat, dialog.SavedPath));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(StickerStrings.StatusError, ex.Message));
                MessageBox.Show(this, ex.Message, StickerStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _stickerButton.Enabled = true;
            }
        }

        /// <summary>Makes the window for a photo; a test makes it without showing it. The model that finds the subject is asked for (and downloaded) when the window first needs it.</summary>
        internal StickerDialog CreateStickerDialog(string photoPath) =>
            new(photoPath, _currentConfig.IsDarkTheme, StickerSettingsFile, _effects, () => EnsureToolModelAsync(ToolModels.Cutout, ToolModelStrings.StickerFeature, askAgain: true));
    }
}
