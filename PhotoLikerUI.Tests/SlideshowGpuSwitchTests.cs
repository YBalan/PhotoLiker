using PhotoLiker.Core;
using PhotoLiker.Effects.Gpu;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Reflection;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Gpu row of the settings grid, in the real grid of the real main window: it is in the group 6. Slideshow, on by default, says what it does, and it decides whether the show that Alt+Enter starts has
/// the graphics chip (on: the chip when the computer has a usable one; off: the processor does everything, as before the chip was used).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowGpuSwitchTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SlideshowGpuSwitchTests() => SettingsPresentation.Register(); // as Program does

    public void Dispose() => _folder.Dispose();

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

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

    /// <summary>Whether this computer has a Direct3D 11 chip that the show would use.</summary>
    private static bool ThisComputerHasAChip()
    {
        using var renderer = GpuRenderer.TryCreate();
        return renderer is not null;
    }

    private void MakePhotos(int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(40 + i * 60, 90, 200 - i * 50));
            bitmap.Save(_folder.File($"photo{i}.jpg"), ImageFormat.Jpeg);
        }
    }

    private static List<Form> Screens() => Application.OpenForms.Cast<Form>().Where(f => f.GetType().Name == "SlideshowScreen").ToList();

    private static int FileCount(Form form)
    {
        var config = Field<object>(form, "_currentConfig");
        return ((System.Collections.ICollection)config.GetType().GetProperty("Files")!.GetValue(config)!).Count;
    }

    [Fact]
    public void TheRow_IsInTheSlideshowGroup_IsOnByDefault_AndSaysWhatItDoes()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var grid = Ui.Controls<PropertyGrid>(form).Single(g => g.SelectedObject is Settings);
            var config = (Settings)grid.SelectedObject!;

            var item = Find(RootOf(grid.SelectedGridItem!), nameof(Settings.SlideshowGpu));
            Assert.NotNull(item);
            grid.SelectedGridItem = item;
            await Task.Delay(100);

            Assert.Equal("Gpu", item!.Label);
            Assert.Equal("6. Slideshow", item.Parent!.Label);
            Assert.Equal(true, item.Value);                                           // GPU by default
            Assert.True(config.SlideshowGpu);
            Assert.Contains("graphics chip", item.PropertyDescriptor!.Description);   // the text of the row, from the language file
            Assert.Contains("False: the processor does everything", item.PropertyDescriptor.Description);

            // The switch: the value of the row is the setting
            item.PropertyDescriptor.SetValue(config, false);
            Assert.False(config.SlideshowGpu);
            item.PropertyDescriptor.SetValue(config, true);
            Assert.True(config.SlideshowGpu);
        }, seconds: 60);
    }

    [Fact]
    public void TheSwitch_DecidesWhetherTheNextShowHasTheChip_AndTheShowsAfterItFollowTheSwitchAgain()
    {
        MakePhotos(3);
        var chip = ThisComputerHasAChip();

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 3, 20000), "the folder is listed");
            var config = Field<Settings>(form, "_currentConfig");
            var start = typeof(MainForm).GetMethod("StartSlideshow", BindingFlags.NonPublic | BindingFlags.Instance)!;

            foreach (var gpu in new[] { false, true, false })
            {
                config.SlideshowGpu = gpu;
                form.BeginInvoke(new Action(() => start.Invoke(form, null))); // Alt+Enter
                Assert.True(await Ui.Until(() => Screens().Count == 1, 20000), $"the show is on the screen (Gpu {gpu})");

                var show = Field<SlideshowSession>(form, "_slideshow");
                var started = show.Gpu.WaitUntilStarted();
                Assert.Equal(gpu && chip, started);               // off: never; on: the chip the computer has
                Assert.Equal(gpu && chip, show.Gpu.Renderer is not null);

                Screens()[0].Close(); // Esc
                Assert.True(await Ui.Until(() => Screens().Count == 0 && Field<object?>(form, "_slideshow") is null, 20000), "the show is over");
            }
        }, seconds: 120);
    }

    [Fact]
    public void TheSwitch_IsKeptWithTheFolder_AndAFolderThatNeverSetItHasItOn()
    {
        new Settings { CurrentFolder = _folder.Path, SlideshowGpu = false }.Save(_folder.Path);
        Assert.False(Settings.Load(_folder.Path).SlideshowGpu);

        File.WriteAllText(Settings.GetFilePath(_folder.Path), """{ "SlideshowTimeout": 4 }"""); // a file written before the chip was used
        Assert.True(Settings.Load(_folder.Path).SlideshowGpu);
    }
}
