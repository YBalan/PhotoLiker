using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Effects.Models;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Starting the slideshow from the main window (Alt+Enter) made for real: the start is not at once (the Parallax effect has the depth model looked at first), and in that time the key can come
/// again (it repeats while it is held, and a person presses it twice): that must not start a second show on top of the first, which Esc would end one at a time and which would work the
/// photos twice.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowStartTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static string RealModels =>
        Environment.GetEnvironmentVariable("PHOTOLIKER_TEST_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");

    /// <summary>Puts the depth model where the window under test looks for it (its data folder is a temp one); false when the machine has none.</summary>
    private static bool DepthModelAvailable()
    {
        if (!ToolModels.Depth.IsPresentIn(RealModels))
            return false;

        var source = ToolModels.Depth.PathIn(RealModels);
        var target = ToolModels.Depth.PathIn(Path.Combine(AppPaths.DataFolder, "models"));
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
        return true;
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

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static int FileCount(Form form)
    {
        var config = Field<object>(form, "_currentConfig");
        return ((System.Collections.ICollection)config.GetType().GetProperty("Files")!.GetValue(config)!).Count;
    }

    private static List<Form> Screens() => Application.OpenForms.Cast<Form>().Where(f => f.GetType().Name == "SlideshowScreen").ToList();

    [Fact]
    public void AKeyThatComesWhileTheShowIsStarting_DoesNotStartAnotherShow()
    {
        if (!DepthModelAvailable()) return;
        MakePhotos(3);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 3, 20000), "the folder is listed");

            // The Parallax effect: the start looks at the depth model first (it is read once to check it), so it is not at once
            var config = Field<object>(form, "_currentConfig");
            config.GetType().GetProperty("SlideshowEffect")!.SetValue(config, SlideshowEffect.Parallax);

            var start = typeof(MainForm).GetMethod("StartSlideshow", BindingFlags.NonPublic | BindingFlags.Instance)!;
            form.BeginInvoke(new Action(() => start.Invoke(form, null))); // Alt+Enter
            form.BeginInvoke(new Action(() => start.Invoke(form, null))); // and again: the key repeats while it is held

            Assert.True(await Ui.Until(() => Screens().Count >= 1, 20000), "the show is on the screen");
            await Task.Delay(2000); // a second one would be there by now

            Assert.Single(Screens());
        }, seconds: 90);
    }

    [Fact]
    public void AfterAShowIsOver_AnotherCanBeStarted()
    {
        MakePhotos(2);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 2, 20000));
            var start = typeof(MainForm).GetMethod("StartSlideshow", BindingFlags.NonPublic | BindingFlags.Instance)!;

            for (var round = 1; round <= 2; round++)
            {
                form.BeginInvoke(new Action(() => start.Invoke(form, null)));
                Assert.True(await Ui.Until(() => Screens().Count == 1, 20000), $"show {round} is on the screen");

                Screens()[0].Close(); // Esc
                Assert.True(await Ui.Until(() => Screens().Count == 0 && Field<object?>(form, "_slideshow") is null, 20000), $"show {round} is over");
            }
        }, seconds: 90);
    }
}
