using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Effects.Models;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The bar over a black and white photo in the main window, made for real: it is there for a black and white photo and not for one with colours, the colours of the model are shown in the viewer and
/// change with the sliders, the original comes back, and a copy is saved. The parts that need the colorizing model run where it is (the program's models folder, or PHOTOLIKER_TEST_MODELS).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ColorizeBarTests : IDisposable
{
    private readonly TempFolder _folder = new();

    /// <summary>The bar is on at the start of each test: what an earlier test (or a person) left in the settings of the program does not matter.</summary>
    public ColorizeBarTests() => new GlobalConfig { ColorizeBar = true }.Save();

    public void Dispose() => _folder.Dispose();

    /// <summary>The window, with the question about downloading the model answered "no" (a test must not download 135 MB, nor wait for a person).</summary>
    private sealed class TestMainForm(string folder) : MainForm(folder)
    {
        public int Asked;
        public bool Throws;

        /// <summary>Done while the question is open: a message box lets the window go on working, and a click or a key can come in that asks for the same model again.</summary>
        public Action? WhileAsking;

        protected override bool ConfirmToolModelDownload(string feature, ToolModel model)
        {
            Asked++;
            if (WhileAsking is { } during)
            {
                WhileAsking = null; // once: it is the second click, not a loop
                during();
            }
            if (Throws)
                throw new InvalidOperationException("no way to ask");
            return false;
        }
    }

    private static string RealModels =>
        Environment.GetEnvironmentVariable("PHOTOLIKER_TEST_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");

    /// <summary>Puts the colorizing model where the window under test looks for it (its data folder is a temp one), copying it once; false when the machine has none.</summary>
    private static bool ModelAvailable()
    {
        if (!ToolModels.Colorizer.IsPresentIn(RealModels))
            return false;

        var source = ToolModels.Colorizer.PathIn(RealModels);
        var target = ToolModels.Colorizer.PathIn(Path.Combine(AppPaths.DataFolder, "models"));
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length) // not one that another test left cut short or changed
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
        return true;
    }

    /// <summary>Takes the colorizing model out of the data folder of the tests, so that the window has to ask for it (it is copied back by <see cref="ModelAvailable"/> when a test needs it).</summary>
    private static string TakeTheModelAway()
    {
        var target = ToolModels.Colorizer.PathIn(Path.Combine(AppPaths.DataFolder, "models"));
        File.Delete(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return target;
    }

    // ── photos ─────────────────────────────────────────────────────────────

    private string GreyPhoto(string name)
    {
        using var bitmap = new Bitmap(480, 320, PixelFormat.Format24bppRgb);
        for (var y = 0; y < 320; y++)
        for (var x = 0; x < 480; x++)
        {
            var dx = (x - 240.0) / 120;
            var dy = (y - 160.0) / 100;
            var light = dx * dx + dy * dy < 1 ? 200 - 60 * (dx * dx + dy * dy) : 70 + 80.0 * y / 320;
            var level = (int)Math.Clamp(light + 10 * Math.Sin(x / 5.0), 0, 255);
            bitmap.SetPixel(x, y, Color.FromArgb(level, level, level));
        }
        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private string ColourPhoto(string name)
    {
        using var bitmap = new Bitmap(480, 320, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(30, 120, 40));
            g.FillEllipse(Brushes.OrangeRed, 100, 60, 280, 200);
            g.FillRectangle(Brushes.RoyalBlue, 0, 250, 480, 70);
        }
        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    /// <summary>How much colour the viewer shows: the average of how far red, green and blue are from each other.</summary>
    private static double Colourfulness(Image? image)
    {
        if (image is null)
            return -1;
        using var copy = new Bitmap(image, 120, 80);
        double sum = 0;
        for (var y = 0; y < copy.Height; y++)
        for (var x = 0; x < copy.Width; x++)
        {
            var c = copy.GetPixel(x, y);
            sum += Math.Abs(c.R - c.G) + Math.Abs(c.G - c.B) + Math.Abs(c.R - c.B);
        }
        return sum / (copy.Width * copy.Height);
    }

    // ── reaching into the window ───────────────────────────────────────────

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().BaseType!.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static PictureBox Viewer(Form form) => Field<PictureBox>(form, "pictureBox1");

    private static ColorizeBar? BarOf(Form form) => Ui.Controls<ColorizeBar>(form).FirstOrDefault();

    private static int FileCount(Form form)
    {
        var config = Field<object>(form, "_currentConfig");
        return ((System.Collections.ICollection)config.GetType().GetProperty("Files")!.GetValue(config)!).Count;
    }

    private static void Show(Form form, string path) =>
        typeof(MainForm).GetMethod("LoadImage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [path]);

    private static async Task<bool> PhotoShown(Form form, string path) =>
        await Ui.Until(() => Viewer(form).Tag as string == path && Viewer(form).Image is not null, 20000);

    private static T In<T>(Control root, string text) where T : ButtonBase => Ui.Controls<T>(root).First(b => b.Text == text);

    private static void Press(Button button)
    {
        Assert.True(button.Enabled, $"the button {button.Text} is on");
        button.PerformClick();
    }

    private static TrackBar Slider(Control bar, int index) => Ui.Controls<TrackBar>(bar).ElementAt(index);

    private static void Save(Form form, string name)
    {
        if (Environment.GetEnvironmentVariable("PL_COLORIZE_SHOT") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        using var shot = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(shot, new Rectangle(0, 0, form.Width, form.Height));

        // DrawToBitmap leaves out what floats over a sibling: the bar is drawn by itself, where it is
        if (BarOf(form) is { Visible: true } bar)
        {
            using var barShot = new Bitmap(bar.Width, bar.Height);
            bar.DrawToBitmap(barShot, new Rectangle(Point.Empty, bar.Size));
            var at = form.PointToClient(bar.PointToScreen(Point.Empty));
            var frame = form.PointToClient(form.Location); // the window's border and title bar are in the picture too
            using var g = Graphics.FromImage(shot);
            g.DrawImage(barShot, at.X - frame.X, at.Y - frame.Y);
        }
        shot.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheBar_IsThereForABlackAndWhitePhoto_AndNotForOneWithColours()
    {
        var colour = ColourPhoto("a_colour.jpg");
        var grey = GreyPhoto("b_grey.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 2, 20000), "the folder is listed");
            Show(form, colour);
            Assert.True(await PhotoShown(form, colour));
            await Task.Delay(900);
            Assert.True(BarOf(form) is null or { Visible: false }, "no bar over a photo with colours");

            Show(form, grey);
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000), "the bar comes for a black and white photo");
            var bar = BarOf(form)!;
            Assert.Equal(ColorizeStrings.BarStatusReady, bar.Status);
            Assert.True(In<Button>(bar, ColorizeStrings.BarColorize).Enabled);
            Assert.False(In<Button>(bar, ColorizeStrings.BarSave).Enabled, "nothing to save before the colours are there");
            Assert.False(Slider(bar, 0).Enabled);
            Save(form, "bar-ready");

            // The dark theme (only to look at, when pictures are asked for): the labels and the sliders sit on the bar like the rest
            if (Environment.GetEnvironmentVariable("PL_COLORIZE_SHOT") is { Length: > 0 })
            {
                typeof(MainForm).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [true]);
                Save(form, "bar-ready-dark");
                typeof(MainForm).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [false]);
            }

            Show(form, colour);
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: false }, 10000), "the bar goes with the photo");
        }, seconds: 90);
    }

    [Fact]
    public void ClosingTheBar_KeepsItAway_UntilTheToolbarButtonBringsItBack()
    {
        var first = GreyPhoto("a.jpg");
        var second = GreyPhoto("b.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 2, 20000));
            Show(form, first);
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
            var bar = BarOf(form)!;

            Press(Ui.Controls<Button>(bar).First(b => b.Text == "✕"));
            Assert.False(bar.Visible);

            Show(form, second);
            await Task.Delay(900);
            Assert.False(bar.Visible, "it stays away from the next black and white photo");

            // The toolbar button brings it back (and asks for the model, which this window answers no)
            var toolbar = Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));
            var asked = ((TestMainForm)form).Asked;
            ((ToolStripButton)toolbar.Items["colorizeToolStripButton"]!).PerformClick();
            Assert.True(await Ui.Until(() => bar.Visible, 5000));
            if (!ModelAvailable())
                Assert.True(await Ui.Until(() => ((TestMainForm)form).Asked > asked, 5000), "it asked for the model");
        }, seconds: 90);
    }

    [Fact]
    public void WhenTheModelCannotBeHad_BecauseOfAnError_TheBarSaysWhy_AndIsNotLeftWorking()
    {
        var photo = GreyPhoto("a.jpg");
        TakeTheModelAway(); // it must be missing for it to be asked for (other tests copy it into the data folder of the tests)

        Ui.Run(() => new TestMainForm(_folder.Path) { Throws = true }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
            var bar = BarOf(form)!;

            Press(In<Button>(bar, ColorizeStrings.BarColorize));

            Assert.True(await Ui.Until(() => bar.Status.Contains("no way to ask"), 10000), $"the bar says why ({bar.Status})");
            Assert.False(bar.ColoursShown);
            Assert.True(In<Button>(bar, ColorizeStrings.BarColorize).Enabled, "it can be tried again");
        }, seconds: 60);
    }

    [Fact]
    public void TheQuestionAboutAModel_IsAskedOnce_AlsoWhenTheSameModelIsAskedForWhileItIsOpen()
    {
        TakeTheModelAway();

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            var test = (TestMainForm)form;
            var ensure = typeof(MainForm).GetMethod("EnsureToolModelAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Task<bool> Ask() => (Task<bool>)ensure.Invoke(form, [ToolModels.Colorizer, "Colorize", true])!;

            Task<bool>? second = null;
            test.WhileAsking = () => second ??= Ask(); // the second click, while the question is on the screen

            Assert.False(await Ask());
            Assert.NotNull(second);
            Assert.False(await second!);
            Assert.Equal(1, test.Asked); // one question, and one answer for both

            // Once it is over, a new request asks again (the person said no, and may have changed their mind)
            test.WhileAsking = null;
            Assert.False(await Ask());
            Assert.Equal(2, test.Asked);
        }, seconds: 60);
    }

    [Fact]
    public void TheCheckOfAModelFile_SaysYesForTheTestedFile_NoForAnyOther_AndDoesNotRememberAYesForAFileThatChanged()
    {
        if (!ModelAvailable()) return;

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            var check = typeof(MainForm).GetMethod("IsTestedFileAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Task<bool> Tested() => (Task<bool>)check.Invoke(form, [ToolModels.Colorizer])!;
            var path = ToolModels.Colorizer.PathIn(Path.Combine(AppPaths.DataFolder, "models"));

            Assert.True(await Tested(), "the model that was tested");
            Assert.True(await Tested(), "and again (the answer is kept)");

            File.WriteAllBytes(path, [1, 2, 3, 4]); // another file in its place: not the answer that was kept
            Assert.False(await Tested());

            File.Delete(path);
            Assert.False(await Tested(), "nothing there is not the model either");
        }, seconds: 60);
    }

    [Fact]
    public void AModelFileThatIsNotTheOneThatWasTested_IsAskedForAgain_NotUsed()
    {
        var target = TakeTheModelAway();
        File.WriteAllBytes(target, [1, 2, 3, 4]); // there, but not the model: cut short, or changed by something else
        var photo = GreyPhoto("a.jpg");

        try
        {
            Ui.Run(() => new TestMainForm(_folder.Path), async form =>
            {
                Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
                Show(form, photo);
                Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
                var bar = BarOf(form)!;

                Press(In<Button>(bar, ColorizeStrings.BarColorize));

                Assert.True(await Ui.Until(() => ((TestMainForm)form).Asked == 1, 10000), "it was asked for again");
                Assert.False(bar.ColoursShown);
            }, seconds: 60);
        }
        finally
        {
            File.Delete(target); // what the next test finds is no junk (ModelAvailable copies the real model back)
        }
    }

    [Fact]
    public void TheToolbarButton_WithTheBarThere_StartsTheColours_AndDoesNotCloseTheBar()
    {
        var photo = GreyPhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
            var bar = BarOf(form)!;
            var asked = ((TestMainForm)form).Asked;

            var toolbar = Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));
            ((ToolStripButton)toolbar.Items["colorizeToolStripButton"]!).PerformClick();

            // The model is asked for (this window says no), or runs: the colours are started either way, and the bar stays
            Assert.True(await Ui.Until(() => ((TestMainForm)form).Asked > asked || bar.ColoursShown, 60000), "the colours were asked for");
            Assert.True(bar.Visible);
            Assert.True(GlobalConfig.Load().ColorizeBar, "the bar is not turned off by the button");
        }, seconds: 90);
    }

    [Fact]
    public void TheBatchWindow_StartsWithTheStrengthAndTheWarmthOfTheBar()
    {
        var photo = GreyPhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
            var bar = BarOf(form)!;
            bar.Values = (140, -30);

            // The window is modal: it is opened from the message loop, and the test goes on while it is open
            form.BeginInvoke(new Action(() => In<Button>(bar, ColorizeStrings.BarMore).PerformClick()));
            Assert.True(await Ui.Until(() => Ui.Find("ColorizeDialog") is not null, 10000), "the window for many photos is open");
            var dialog = Ui.Find("ColorizeDialog")!;
            var boxes = Ui.Controls<NumericUpDown>(dialog).Select(box => box.Value).ToList();
            Assert.Contains(140m, boxes);
            Assert.Contains(-30m, boxes);
            dialog.Close();
        }, seconds: 60);
    }

    [Fact]
    public void WhereTheSlidersAreLeft_IsKeptForTheNextTime_AndTheBarGoesBackToWhereItWas()
    {
        var photo = GreyPhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
            var bar = BarOf(form)!;

            var strength = Slider(bar, 0);
            var warmth = Slider(bar, 1);
            strength.Value = 150;
            warmth.Value = -45;
            // The person lets go of the mouse
            typeof(Control).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(warmth, [new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)]);

            var kept = GlobalConfig.Load();
            Assert.Equal(150, kept.ColorizeStrength);
            Assert.Equal(-45, kept.ColorizeWarmth);
            Assert.True(kept.ColorizeBar);

            // A double click on a slider puts it back
            typeof(Control).GetMethod("OnMouseDoubleClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(strength, [new MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0)]);
            Assert.Equal(100, strength.Value);
            Assert.Equal(100, GlobalConfig.Load().ColorizeStrength);
        }, seconds: 60);
    }

    [Fact]
    public void ThePhotoOfTheCache_IsNeverTouched_WhileAPhotoIsShownWithColours()
    {
        if (!ModelAvailable()) return;
        var photo = GreyPhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
            var bar = BarOf(form)!;
            var viewer = Viewer(form);
            var original = viewer.Image!;
            var before = Colourfulness(original);

            Press(In<Button>(bar, ColorizeStrings.BarColorize));
            Assert.True(await Ui.Until(() => bar.ColoursShown && !ReferenceEquals(viewer.Image, original), 60000));
            Assert.Equal(before, Colourfulness(original)); // the cached picture is as it was: grey

            // The photo is loaded again (a rotation, a new version of the file): the colours stay over it while it is the same file
            var again = typeof(MainForm).GetMethod("LoadImage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            again.Invoke(form, [photo]);
            await Task.Delay(800);
            Assert.True(bar.ColoursShown, "the same photo: the colours stay");
            Assert.True(Colourfulness(viewer.Image) > 4);

            // Another photo is chosen (no more photos here: the bar goes with the photo that is cleared)
            File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddHours(-5)); // the file is not what the colours were made of any more
            again.Invoke(form, [photo]);
            Assert.True(await Ui.Until(() => Colourfulness(viewer.Image) < 3 || !bar.ColoursShown, 10000), "a changed file is shown as it is");
        }, seconds: 120);
    }

    [Fact]
    public void WithTheModel_TheColoursAreShown_ChangeWithTheSliders_GoBack_AndAreSaved()
    {
        if (!ModelAvailable()) return;
        var first = GreyPhoto("a_first.jpg");
        var second = GreyPhoto("b_second.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 2, 20000));
            Show(form, first);
            Assert.True(await PhotoShown(form, first));
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: true }, 10000));
            var bar = BarOf(form)!;
            var viewer = Viewer(form);
            var original = viewer.Image;
            Assert.True(Colourfulness(original) < 3, "a grey photo");

            // The model runs, and the viewer shows the photo with colours
            Press(In<Button>(bar, ColorizeStrings.BarColorize));
            Assert.True(await Ui.Until(() => bar.ColoursShown && viewer.Image is not null && !ReferenceEquals(viewer.Image, original), 60000), $"coloured ({bar.Status})");
            Assert.True(await Ui.Until(() => bar.Status == ColorizeStrings.BarStatusShown, 20000));
            var asGiven = Colourfulness(viewer.Image);
            Assert.True(asGiven > 4, $"it has colours: {asGiven}");
            Assert.True(In<Button>(bar, ColorizeStrings.BarSave).Enabled);
            Assert.Equal(ColorizeStrings.BarOriginal, Ui.Controls<Button>(bar).First(b => b.Text == ColorizeStrings.BarOriginal).Text);
            Save(form, "bar-coloured");

            // The strength: paler, and stronger
            var strength = Slider(bar, 0);
            strength.Value = 30;
            Assert.True(await Ui.Until(() => Colourfulness(viewer.Image) < asGiven * 0.7, 10000), "paler");
            var pale = Colourfulness(viewer.Image);
            strength.Value = 180;
            Assert.True(await Ui.Until(() => Colourfulness(viewer.Image) > asGiven * 1.2, 10000), "stronger");
            strength.Value = 100;
            Assert.True(await Ui.Until(() => Math.Abs(Colourfulness(viewer.Image) - asGiven) < asGiven * 0.1, 10000), "as given again");

            // The warmth: with no strength left, the photo gets one tone
            strength.Value = 20;
            Slider(bar, 1).Value = 80;
            Assert.True(await Ui.Until(() => Colourfulness(viewer.Image) > pale, 10000), "the warmth adds a tone");
            Slider(bar, 1).Value = 0;
            strength.Value = 100;
            Assert.True(await Ui.Until(() => Math.Abs(Colourfulness(viewer.Image) - asGiven) < asGiven * 0.1, 10000));

            // The original and back: instantly, without the model
            Press(In<Button>(bar, ColorizeStrings.BarOriginal));
            Assert.True(await Ui.Until(() => ReferenceEquals(viewer.Image, original), 5000), "the photo itself is in the viewer again");
            Assert.False(In<Button>(bar, ColorizeStrings.BarSave).Enabled);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Press(In<Button>(bar, ColorizeStrings.BarColorize));
            Assert.True(await Ui.Until(() => Colourfulness(viewer.Image) > 4, 5000), "the colours are back");
            Assert.True(clock.ElapsedMilliseconds < 3000, $"without the model: {clock.ElapsedMilliseconds} ms");

            // A copy is saved next to the photo, and joins the list; the photo stays on screen
            var files = FileCount(form);
            Press(In<Button>(bar, ColorizeStrings.BarSave));
            var copy = _folder.File("a_first (colorized).jpg");
            Assert.True(await Ui.Until(() => File.Exists(copy), 30000), "the copy is there");
            Assert.True(await Ui.Until(() => FileCount(form) == files + 1, 20000), "the copy is in the list");
            Assert.Equal(first, viewer.Tag as string);
            Assert.True(await Ui.Until(() => bar.Status.StartsWith(ColorizeStrings.BarStatusSavedFormat.Split('{')[0]), 5000));
            Assert.True(File.Exists(first));

            // The wish for colours goes on to the next black and white photo
            Show(form, second);
            Assert.True(await PhotoShown(form, second));
            Assert.True(await Ui.Until(() => bar.ColoursShown && Colourfulness(viewer.Image) > 4, 60000), "the next photo is coloured too");
            Save(form, "bar-next");
        }, seconds: 240);
    }
}
