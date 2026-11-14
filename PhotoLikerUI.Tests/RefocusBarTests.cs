using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Focus;
using PhotoLiker.Search;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Refocus bar in the main window, made for real (the depth is made up: the left three quarters near, the right quarter far; what the blur looks like is the business of the tests of the effects library):
/// the toolbar button opens and closes it, it floats over the photo as the Colorize bar does, it works out the photo, the viewer shows the refocused picture, a click on the photo puts the focus there,
/// the sliders change the picture and are kept for the next photo and the next run, it goes on to the next photo, and Save a copy writes the copy.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class RefocusBarTests : IDisposable
{
    private const int W = 320, H = 200;

    private readonly TempFolder _folder = new();

    public RefocusBarTests() => ResetConfig();

    public void Dispose()
    {
        _folder.Dispose();
        ResetConfig();
    }

    private static void ResetConfig() => new GlobalConfig { RefocusBlur = 50, RefocusRange = 30, RefocusHaze = 0, ColorizeBar = false }.Save();

    /// <summary>The window, with the work of the depth model done by a made-up one, and the Save As window answered by the test.</summary>
    private sealed class TestMainForm(string folder) : MainForm(folder)
    {
        public int Opens;
        public readonly List<string> Opened = [];
        public readonly List<(string Title, string Filter, string Folder, string Name)> Asked = [];
        public string? Answer;
        public bool NoModel;

        protected override Task<FocusPreview?> OpenRefocusAsync(string file)
        {
            Opens++;
            Opened.Add(file);
            return Task.FromResult(NoModel ? null : MakePreview(file));
        }

        protected override string? AskSaveAsPath(string title, string filter, string folder, string fileName)
        {
            Asked.Add((title, filter, folder, fileName));
            return Answer;
        }
    }

    private static FocusPreview MakePreview(string path)
    {
        var info = new FileInfo(path);
        var bytes = File.ReadAllBytes(path);
        using var bitmap = new Bitmap(new MemoryStream(bytes));
        var bgr = new byte[W * H * 3];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            var i = (y * W + x) * 3;
            bgr[i] = pixel.B;
            bgr[i + 1] = pixel.G;
            bgr[i + 2] = pixel.R;
        }

        var depth = new DepthMap(16, 10, Enumerable.Range(0, 160).Select(i => i % 16 < 12 ? (byte)255 : (byte)0).ToArray());
        return new FocusPreview(path, bytes, new DecodedPhoto(W, H, bgr), depth, [], info.Length, info.LastWriteTimeUtc);
    }

    private string MakePhoto(string name)
    {
        using var bitmap = new Bitmap(W, H);
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            bitmap.SetPixel(x, y, (x / 4 + y / 4) % 2 == 0 ? Color.FromArgb(225, 225, 225) : Color.FromArgb(35, 35, 35));

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    // ── reaching into the window ───────────────────────────────────────────

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().BaseType!.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static PictureBox Viewer(Form form) => Field<PictureBox>(form, "pictureBox1");

    private static RefocusBar? BarOf(Form form) => Ui.Controls<RefocusBar>(form).FirstOrDefault();

    private static ToolStripButton ButtonOf(Form form) =>
        Ui.Controls<ToolStrip>(form).SelectMany(strip => strip.Items.Cast<ToolStripItem>()).OfType<ToolStripButton>().Single(button => button.Name == "refocusToolStripButton");

    private static ToolStripButton SaveButtonOf(Form form) =>
        Ui.Controls<ToolStrip>(form).SelectMany(strip => strip.Items.Cast<ToolStripItem>()).OfType<ToolStripButton>().Single(button => button.Name == "saveToolStripButton");

    private static int FileCount(Form form)
    {
        var config = Field<object>(form, "_currentConfig");
        return ((System.Collections.ICollection)config.GetType().GetProperty("Files")!.GetValue(config)!).Count;
    }

    private static void Show(Form form, string path) =>
        typeof(MainForm).GetMethod("LoadImage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [path]);

    private static async Task<bool> PhotoShown(Form form, string path) =>
        await Ui.Until(() => Viewer(form).Tag as string == path && Viewer(form).Image is not null, 20000);

    private static string Status(Form form) => Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text ?? string.Empty;

    private static void FocusAt(Form form, double x, double y) =>
        typeof(MainForm).GetMethod("FocusRefocusAt", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [x, y]);

    private static TrackBar Slider(Control bar, int index) => Ui.Controls<TrackBar>(bar).ElementAt(index);

    private static async Task<bool> BarReady(Form form) => await Ui.Until(() => BarOf(form) is { Visible: true, CanSave: true }, 15000);

    /// <summary>How much fine detail the viewer shows between the columns: the mean difference of neighbours, a measure of how sharp it is.</summary>
    private static double Detail(Image? image, double fromShare, double toShare)
    {
        using var copy = new Bitmap(image!);
        double sum = 0;
        var count = 0;
        for (var y = 0; y < copy.Height; y++)
        for (var x = (int)(copy.Width * fromShare); x < (int)(copy.Width * toShare) - 1; x++)
        {
            sum += Math.Abs(copy.GetPixel(x, y).R - copy.GetPixel(x + 1, y).R);
            count++;
        }
        return sum / count;
    }

    // ── the button ─────────────────────────────────────────────────────────

    [Fact]
    public void TheMainWindow_Opens_WithTheRefocusButtonRightAfterColorize_AndNeitherNewNorPaste()
    {
        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            var strip = Ui.Controls<ToolStrip>(form).Single(s => s is not StatusStrip && s.Items.Cast<ToolStripItem>().Any(item => item.Name == "refocusToolStripButton"));
            var names = strip.Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.Equal(names.IndexOf("colorizeToolStripButton") + 1, names.IndexOf("refocusToolStripButton"));
            Assert.DoesNotContain("newToolStripButton", names);
            Assert.DoesNotContain("pasteToolStripButton", names);
            Assert.Contains("openToolStripButton", names);
            Assert.Contains("saveToolStripButton", names);
            Assert.Contains("copyToolStripButton", names);

            var button = ButtonOf(form);
            Assert.Equal(RefocusStrings.ButtonText, button.Text);
            Assert.Equal(RefocusStrings.ButtonTooltip, button.ToolTipText);
            Assert.NotNull(button.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void PressingRefocus_WithNoPhotoShown_SaysSo_AndOpensNoBar()
    {
        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            ButtonOf(form).PerformClick();
            await Task.Delay(300);

            Assert.True(BarOf(form) is null or { Visible: false });
            Assert.Equal(RefocusStrings.StatusNoPhoto, Status(form));
        }, seconds: 60);
    }

    // ── the bar ────────────────────────────────────────────────────────────

    [Fact]
    public void TheButton_OpensTheBarOverThePhoto_ShowsTheRefocusedPicture_AndClosesIt_AndTheBarIsAtTheTop()
    {
        var photo = MakePhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000), "the folder is listed");
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            var original = Viewer(form).Image;
            var drawn = Detail(original, 0.9, 1.0);

            ButtonOf(form).PerformClick();
            Assert.True(await BarReady(form), "the bar is there and the photo is ready");
            var bar = BarOf(form)!;
            var test = (TestMainForm)form;
            Assert.Equal(photo, test.Opened.Single());
            Assert.Equal(RefocusStrings.StatusReady, bar.Status);
            var photoTop = Field<Control>(form, "scrollPanel").Top;
            Assert.True(bar.Top >= photoTop && bar.Top < photoTop + 40, $"at the top of the photo, below the search strip ({bar.Top} against the photo at {photoTop}): the Colorize bar is at the bottom");
            Assert.Equal(3, Ui.Controls<TrackBar>(bar).Count());

            // The viewer shows a picture of its own, with the far quarter blurred (the middle of the photo is near: that is where the focus began)
            Assert.True(await Ui.Until(() => !ReferenceEquals(Viewer(form).Image, original), 10000), "the viewer shows the refocused picture");
            Assert.True(Detail(Viewer(form).Image, 0.9, 1.0) < drawn * 0.5, "the far part is blurred");

            ButtonOf(form).PerformClick(); // the button again closes it, and the photo itself comes back
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: false }, 5000));
            Assert.True(await Ui.Until(() => Viewer(form).Image is not null && Detail(Viewer(form).Image, 0.9, 1.0) > drawn * 0.9, 5000), "the photo is shown as it is again");
        }, seconds: 90);
    }

    [Fact]
    public void AClickOnThePhoto_PutsTheFocusThere_AndTheSlidersChangeThePicture()
    {
        var photo = MakePhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            ButtonOf(form).PerformClick();
            Assert.True(await BarReady(form));
            var bar = BarOf(form)!;

            // Focus on the far quarter: the near part is blurred now
            Assert.True(await Ui.Until(() => Viewer(form).Image is not null && Detail(Viewer(form).Image, 0.0, 0.4) > 20, 10000), "the near part is sharp to begin with");
            FocusAt(form, 0.92, 0.5);
            Assert.True(await Ui.Until(() => Detail(Viewer(form).Image, 0.0, 0.4) < 12, 10000), "the near part is blurred when the focus is on the far");
            Assert.True(await Ui.Until(() => Detail(Viewer(form).Image, 0.92, 1.0) > 20, 10000), "and the far part is sharp");

            // The blur slider to nothing: nothing is blurred any more
            Slider(bar, 0).Value = 0;
            Assert.True(await Ui.Until(() => Detail(Viewer(form).Image, 0.0, 0.4) > 20, 10000), "no blur, no difference");
        }, seconds: 90);
    }

    /// <summary>The place on the viewer (the picture box) where this share of the photo is drawn, and the viewer's own mouse events put there.</summary>
    private static void Click(Form form, double x, double y, int releasedAwayBy = 0)
    {
        var viewer = Viewer(form);
        var scale = Math.Min(viewer.ClientSize.Width / (double)viewer.Image!.Width, viewer.ClientSize.Height / (double)viewer.Image.Height);
        var left = (viewer.ClientSize.Width - viewer.Image.Width * scale) / 2;
        var top = (viewer.ClientSize.Height - viewer.Image.Height * scale) / 2;
        var at = new Point((int)(left + x * viewer.Image.Width * scale), (int)(top + y * viewer.Image.Height * scale));
        var raise = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var release = typeof(Control).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        raise.Invoke(viewer, [new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0)]);
        release.Invoke(viewer, [new MouseEventArgs(MouseButtons.Left, 1, at.X + releasedAwayBy, at.Y, 0)]);
    }

    [Fact]
    public void ARealClickOnThePicture_IsMappedToThePhoto_AndADragIsNotAClick()
    {
        var photo = MakePhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            ButtonOf(form).PerformClick();
            Assert.True(await BarReady(form));
            Assert.True(await Ui.Until(() => Detail(Viewer(form).Image, 0.0, 0.4) > 20, 10000), "the near part is sharp to begin with");

            // The button went down and came up 40 pixels away: the photo was pulled about, not clicked
            Click(form, 0.92, 0.5, releasedAwayBy: 40);
            await Task.Delay(600);
            Assert.True(Detail(Viewer(form).Image, 0.0, 0.4) > 20, "a drag does not put the focus anywhere");

            // A click in the far quarter puts the focus there
            Click(form, 0.92, 0.5);
            Assert.True(await Ui.Until(() => Detail(Viewer(form).Image, 0.0, 0.4) < 12, 10000), "the focus is on the far part: the near part is blurred");
        }, seconds: 90);
    }

    [Fact]
    public void TheSliders_AreKept_ForTheNextPhoto_AndForTheNextRun_AndBeginAtTheKeptValues()
    {
        var photo = MakePhoto("a.jpg");
        new GlobalConfig { RefocusBlur = 70, RefocusRange = 40, RefocusHaze = 10 }.Save();

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            ButtonOf(form).PerformClick();
            Assert.True(await BarReady(form));
            var bar = BarOf(form)!;

            Assert.Equal((70, 40, 10), bar.Values); // what the last run kept

            Slider(bar, 0).Value = 20;
            Slider(bar, 1).Value = 55;
            Slider(bar, 2).Value = 35;
            typeof(Control).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Slider(bar, 0), [new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)]); // the person lets go

            var kept = GlobalConfig.Load();
            Assert.Equal((20, 55, 35), (kept.RefocusBlur, kept.RefocusRange, kept.RefocusHaze));
        }, seconds: 90);
    }

    [Fact]
    public void TheBarStaysOpen_AndGoesOnToTheNextPhoto_WithTheSameSliders_UntilItIsClosed()
    {
        var first = MakePhoto("a.jpg");
        var second = MakePhoto("b.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 2, 20000));
            var test = (TestMainForm)form;
            Show(form, first);
            Assert.True(await PhotoShown(form, first));
            ButtonOf(form).PerformClick();
            Assert.True(await BarReady(form));
            var bar = BarOf(form)!;
            Slider(bar, 0).Value = 80;

            Show(form, second);
            Assert.True(await Ui.Until(() => test.Opened.Contains(second), 15000), "the next photo is worked out");
            Assert.True(await BarReady(form), "and the bar is ready for it");
            Assert.Equal(80, BarOf(form)!.Values.Blur); // the sliders are as they were

            // Closed by its ✕: it stays away from the next photo
            var close = Ui.Controls<Button>(bar).Single(b => b.Text == "✕");
            close.PerformClick();
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: false }, 5000));
            var opens = test.Opens;
            Show(form, first);
            Assert.True(await PhotoShown(form, first));
            await Task.Delay(1000);
            Assert.Equal(opens, test.Opens);
            Assert.True(BarOf(form) is { Visible: false });
        }, seconds: 90);
    }

    [Fact]
    public void WithoutTheDepthModel_TheMainWindowSaysSo_AndTheBarGoesAway()
    {
        var photo = MakePhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path) { NoModel = true }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            ButtonOf(form).PerformClick();

            Assert.True(await Ui.Until(() => Status(form) == RefocusStrings.StatusNoModel, 10000));
            Assert.True(BarOf(form) is null or { Visible: false }, "the bar is not left over a photo it can not work on");
        }, seconds: 60);
    }

    [Fact]
    public void SaveACopy_WritesNameBracketPortraitNextToThePhoto_AndTheCopyJoinsTheList()
    {
        var photo = MakePhoto("granny.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            ButtonOf(form).PerformClick();
            Assert.True(await BarReady(form));

            var save = Ui.Controls<Button>(BarOf(form)!).Single(b => b.AccessibleName == RefocusStrings.SaveButton);
            save.PerformClick();

            var copy = _folder.File("granny (portrait).jpg");
            Assert.True(await Ui.Until(() => File.Exists(copy), 15000), "the copy is there");
            Assert.True(await Ui.Until(() => FileCount(form) == 2, 15000), "and in the list");
            Assert.True(File.Exists(photo));
            Assert.Equal(photo, Viewer(form).Tag); // the photo that is shown stays where it is
        }, seconds: 90);
    }

    [Fact]
    public void WhileTheColorizeBarHasColours_OpeningTheRefocusBarGivesTheViewerBack_AndOpeningColorizeClosesIt()
    {
        var photo = MakePhoto("a.jpg");

        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            ButtonOf(form).PerformClick();
            Assert.True(await BarReady(form));

            // The Colorize button (a photo is shown, so it works on that photo) takes the viewer: the Refocus bar is closed
            var colorize = Ui.Controls<ToolStrip>(form).SelectMany(s => s.Items.Cast<ToolStripItem>()).OfType<ToolStripButton>().Single(b => b.Name == "colorizeToolStripButton");
            colorize.PerformClick();
            Assert.True(await Ui.Until(() => BarOf(form) is { Visible: false }, 10000), "the Refocus bar is closed by Colorize");
        }, seconds: 90);
    }

    // ── the control ────────────────────────────────────────────────────────

    [Fact]
    public void TheBarControl_HoldsTheValues_TellsWhatThePersonDid_AndBringsASliderBackByADoubleClick()
    {
        using var bar = new RefocusBar();
        var changes = 0;
        var commits = 0;
        bar.LookChanged += () => changes++;
        bar.LookCommitted += () => commits++;

        Assert.Equal((50, 30, 0), bar.Values);
        bar.Values = (90, 80, 70); // set by the code: not a change the person made
        Assert.Equal((90, 80, 70), bar.Values);
        Assert.Equal(0, changes);

        bar.Values = (500, -5, 40); // out of range: brought in
        Assert.Equal((100, 0, 40), bar.Values);

        Ui.Controls<TrackBar>(bar).First().Value = 33;
        Assert.Equal(1, changes);

        Assert.False(bar.CanSave);
        bar.Ready = true;
        Assert.True(bar.CanSave);
        bar.Busy = true;
        Assert.False(bar.CanSave, "it waits while something is going on");
        bar.Busy = false;
        Assert.True(bar.CanSave);
        bar.Ready = false;
        Assert.False(bar.CanSave);
        _ = commits;
    }
}
