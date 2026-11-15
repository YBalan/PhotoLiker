namespace PhotoLikerUI
{
    /// <summary>The tips of the rows of the settings grid that show less than they hold (see <see cref="GridValueTips"/>).</summary>
    public partial class MainForm
    {
        private GridValueTips? _gridTips;

        /// <summary>The colour table of the slideshow is shown in the grid by the name of its file only (see <see cref="LutFileConverter"/>): the tip of that row is the whole path.</summary>
        private string? SettingsRowTip(GridItem item) =>
            item.PropertyDescriptor?.Name == nameof(Settings.SlideshowLookFile) && item.Value is string { Length: > 0 } path ? path : null;
    }
}
