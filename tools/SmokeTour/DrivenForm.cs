using System.ComponentModel;
using PhotoLikerUI;

namespace SmokeTour;

/// <summary>
/// The real main window, with the one thing a person does that code can't: pressing a key. <see cref="Press"/> goes
/// through the same <c>ProcessCmdKey</c> a keystroke does.
/// </summary>
internal sealed class DrivenForm(string folder) : MainForm(folder)
{
    public void Press(Keys key)
    {
        var message = new Message { HWnd = Handle };
        ProcessCmdKey(ref message, key);
    }

    // ── looking things up by the names the designer gave them ───────────────────────────────────

    public T Find<T>(string name) where T : Control => (T)Controls.Find(name, searchAllChildren: true).Single();

    public PropertyGrid SettingsGrid => Find<PropertyGrid>("settingsPropertyGrid");
    public PropertyGrid MetadataGrid => Find<PropertyGrid>("imageMetaPropertyGrid");
    public PictureBox Viewer => Find<PictureBox>("pictureBox1");

    public string Status => StatusItem<ToolStripStatusLabel>("toolStripStatusLabel1").Text ?? string.Empty;
    public ToolStripStatusLabel GpsLink => StatusItem<ToolStripStatusLabel>("toolStripStatusLabelGpsLink");

    public ToolStripItem ToolbarItem(string name) => Find<ToolStrip>("toolStrip1").Items[name]!;

    public ToolStripItem StatusBarItem(string name) => Find<StatusStrip>("statusStrip1").Items[name]!;

    private T StatusItem<T>(string name) where T : ToolStripItem => (T)Find<StatusStrip>("statusStrip1").Items[name]!;

    /// <summary>The settings object the grid shows (the one the app works with).</summary>
    public PhotoLiker.Core.Settings Config => (PhotoLiker.Core.Settings)SettingsGrid.SelectedObject!;

    public GridItem SettingsRow(string name) => Row(SettingsGrid, name);

    public static GridItem Row(PropertyGrid grid, string name)
    {
        var root = grid.SelectedGridItem;
        while (root?.Parent is not null)
            root = root.Parent;

        return root!.GridItems.Cast<GridItem>()
            .SelectMany(category => category.GridItems.Cast<GridItem>())
            .First(item => item.Label == name);
    }

    public IEnumerable<string> VisibleSettingsCategories()
    {
        var root = SettingsGrid.SelectedGridItem;
        while (root?.Parent is not null)
            root = root.Parent;

        return root!.GridItems.Cast<GridItem>().Select(category => category.Label!);
    }

    public IEnumerable<string> VisibleSettingsRows()
    {
        var root = SettingsGrid.SelectedGridItem;
        while (root?.Parent is not null)
            root = root.Parent;

        return root!.GridItems.Cast<GridItem>().SelectMany(category => category.GridItems.Cast<GridItem>()).Select(item => item.Label!);
    }

    public PropertyDescriptor SettingsProperty(string name) => TypeDescriptor.GetProperties(Config)[name]!;
}
