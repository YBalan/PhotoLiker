using PhotoLiker.Core;
using System.ComponentModel;
using System.Reflection;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The LookFile row of the settings grid shows the name of the colour table only (no folder, no extension) while the setting keeps the whole path, a name typed in the cell finds the table, and the tip of
/// the row, in the real grid of the real main window, is the whole path.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class LookFileRowTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public LookFileRowTests() => SettingsPresentation.Register(); // as Program does: the converter and the editor of the rows

    public void Dispose() => _folder.Dispose();

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    // ── what the cell shows ─────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\luts\Teal and Orange.cube", "Teal and Orange")]
    [InlineData(@"D:\a\b\c\my.look.v2.cube", "my.look.v2")]       // only the last extension goes
    [InlineData("kodak.CUBE", "kodak")]
    [InlineData("kodak", "kodak")]
    [InlineData("  C:\\luts\\x.cube  ", "x")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void TheCell_ShowsTheNameOfTheFileOnly(string? path, string shown) =>
        Assert.Equal(shown, LutFileConverter.Display(path));

    [Fact]
    public void TheGrid_AsksTheConverterOfTheRow_WhichShowsTheNameAndKeepsThePath()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var properties = TypeDescriptor.GetProperties(new Settings())[nameof(Settings.SlideshowLookFile)]!;

            Assert.IsType<LutFileConverter>(properties.Converter);
            Assert.Equal("Teal and Orange", properties.Converter.ConvertToString(@"C:\apps\PhotoLiker\Luts\Teal and Orange.cube"));
            Assert.Equal(string.Empty, properties.Converter.ConvertToString(string.Empty));
            await Task.CompletedTask;
        }, seconds: 60);
    }

    // ── what a typed name stands for ────────────────────────────────────────

    [Fact]
    public void ATypedName_IsLookedForBesideTheChosenFile_ThenInTheBundledFolder_WithAndWithoutTheExtension()
    {
        Directory.CreateDirectory(_folder.File("mine"));
        Directory.CreateDirectory(_folder.File(@"app\Luts"));
        var mine = _folder.File(@"mine\grade.cube");
        var bundled = _folder.File(@"app\Luts\Teal and Orange.cube");
        File.WriteAllText(mine, "x");
        File.WriteAllText(bundled, "x");
        File.WriteAllText(_folder.File(@"mine\Teal and Orange.cube"), "x"); // the same name beside the chosen file: it wins
        var bundledFolder = Path.GetDirectoryName(bundled)!;

        Assert.Equal(_folder.File(@"mine\Teal and Orange.cube"), LutFileConverter.Resolve("Teal and Orange", mine, bundledFolder));
        Assert.Equal(bundled, LutFileConverter.Resolve("Teal and Orange.cube", null, bundledFolder));
        Assert.Equal(bundled, LutFileConverter.Resolve("  teal and orange ", null, bundledFolder), ignoreCase: true); // the file system does not care about the case
        Assert.Equal(mine, LutFileConverter.Resolve("grade", mine, bundledFolder));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData("\"C:\\somewhere\\x.cube\"", "C:\\somewhere\\x.cube")]  // Explorer's "Copy as path" puts quotes round it
    [InlineData("C:\\somewhere\\x.cube", "C:\\somewhere\\x.cube")]    // a whole path is taken as it is, there or not
    [InlineData("no such table", "no such table")]                    // found nowhere: kept, and the show says it can not use it
    [InlineData("bad|name?", "bad|name?")]
    public void ATypedText_ThatIsNoKnownName_IsKeptAsTyped(string? typed, string kept) =>
        Assert.Equal(kept, LutFileConverter.Resolve(typed, null, Path.Combine(_folder.Path, "Luts")));

    // ── the tip ─────────────────────────────────────────────────────────────

    private static GridItem? Find(GridItem from, string name)
    {
        if (from.PropertyDescriptor?.Name == name)
            return from;
        foreach (GridItem child in from.GridItems)
        {
            if (Find(child, name) is { } found)
                return found;
        }
        return null;
    }

    private static GridItem RootOf(GridItem item)
    {
        while (item.Parent is { } parent)
            item = parent;
        return item;
    }

    [Fact]
    public void TheTipOfTheRow_IsTheWholePath_AndTheTipOfAnotherRowIsNone()
    {
        Directory.CreateDirectory(_folder.File("packs"));
        var table = _folder.File(@"packs\Warm Vintage.cube");
        File.WriteAllText(table, "x");

        Ui.Run(() => new MainForm(), async form =>
        {
            var grid = Ui.Controls<PropertyGrid>(form).Single(g => g.SelectedObject is Settings);
            var tips = Field<GridValueTips>(form, "_gridTips");
            var config = (Settings)grid.SelectedObject!;
            config.SlideshowLookFile = table;
            grid.Refresh();

            var item = Find(RootOf(grid.SelectedGridItem!), nameof(Settings.SlideshowLookFile));
            Assert.NotNull(item);
            grid.SelectedGridItem = item; // opens its group and scrolls to it, so that the row is on the list
            await Task.Delay(200);

            var cell = tips.ValueCellOf(item!);
            Assert.NotNull(cell);
            tips.Update(new Point(cell!.Value.Left + 10, cell.Value.Top + cell.Value.Height / 2));

            Assert.Equal(table, tips.Text);
            Assert.Equal(item, tips.ItemAt(new Point(cell.Value.Left + 10, cell.Value.Top + cell.Value.Height / 2)));
            Assert.Equal("Warm Vintage", item!.PropertyDescriptor!.Converter.ConvertToString(item.Value)); // and the cell says the name

            // The row above it (Effect, Look ...) has no tip: the tip goes when the mouse moves to it
            tips.Update(new Point(cell.Value.Left + 10, cell.Value.Top - cell.Value.Height / 2));
            Assert.Equal(string.Empty, tips.Text);
        }, seconds: 60);
    }

    [Fact]
    public void WithNoTableChosen_TheRowHasNoTip()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var grid = Ui.Controls<PropertyGrid>(form).Single(g => g.SelectedObject is Settings);
            var tips = Field<GridValueTips>(form, "_gridTips");
            ((Settings)grid.SelectedObject!).SlideshowLookFile = string.Empty;
            grid.Refresh();

            var item = Find(RootOf(grid.SelectedGridItem!), nameof(Settings.SlideshowLookFile))!;
            grid.SelectedGridItem = item;
            await Task.Delay(200);

            var cell = tips.ValueCellOf(item)!.Value;
            tips.Update(new Point(cell.Left + 10, cell.Top + cell.Height / 2));
            Assert.Equal(string.Empty, tips.Text);
        }, seconds: 60);
    }
}
