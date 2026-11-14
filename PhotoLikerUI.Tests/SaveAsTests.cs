using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Focus;
using PhotoLiker.Search;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Save button of the toolbar is Save As: for a photo or a video it saves a copy of the file where the person says (keeping its dates), and with the Refocus bar working it saves the picture that is
/// on the screen, as a JPEG, not the file. The Save As window is answered by the test.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SaveAsTests : IDisposable
{
    private const int W = 320, H = 200;

    private readonly TempFolder _folder = new();

    public SaveAsTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private sealed class TestMainForm(string folder) : MainForm(folder)
    {
        public readonly List<(string Title, string Filter, string Folder, string Name)> Asked = [];
        public string? Answer;
        public bool WithDepth;

        protected override Task<FocusPreview?> OpenRefocusAsync(string file) => Task.FromResult<FocusPreview?>(WithDepth ? MakePreview(file) : null);

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
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(40, 120, 200));
            g.FillEllipse(Brushes.OrangeRed, 60, 40, 200, 120);
        }
        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        File.SetCreationTimeUtc(path, new DateTime(2012, 6, 1, 9, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(path, new DateTime(2012, 6, 2, 10, 30, 0, DateTimeKind.Utc));
        return path;
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().BaseType!.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static PictureBox Viewer(Form form) => Field<PictureBox>(form, "pictureBox1");

    private static ToolStripButton Button(Form form, string name) =>
        Ui.Controls<ToolStrip>(form).SelectMany(strip => strip.Items.Cast<ToolStripItem>()).OfType<ToolStripButton>().Single(button => button.Name == name);

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

    /// <summary>
    /// The program says "saved" when the file is written, its dates are set and the folder was looked at again: that is the moment to look at the file. A file that merely exists may be half written or
    /// still open (the length of a file that is being copied is there at once), and its dates are set a moment after the copy: a slow machine (the release run on GitHub) found both.
    /// </summary>
    private static Task<bool> SaidSaved(Form form, string path) =>
        Ui.Until(() => Status(form) == string.Format(ToolbarStrings.StatusSaveAsSavedFormat, Path.GetFileName(path)), 20000);

    // ── a photo or a video ──────────────────────────────────────────────────

    [Fact]
    public void WithNothingShown_ItSaysSo_AndAsksNothing()
    {
        Ui.Run(() => new TestMainForm(_folder.Path), async form =>
        {
            Button(form, "saveToolStripButton").PerformClick();
            await Task.Delay(300);

            Assert.Equal(ToolbarStrings.StatusNothingToSave, Status(form));
            Assert.Empty(((TestMainForm)form).Asked);
        }, seconds: 60);
    }

    [Fact]
    public void ForAPhoto_ItAsksWhereToSave_WithTheNameAndTheFolderOfThePhoto_AndCopiesTheFileWithItsDates()
    {
        var photo = MakePhoto("granny.jpg");
        var destination = _folder.File(@"elsewhere\copy of granny.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await SaidSaved(form, destination), "the copy is there, and the program says so");

            var test = (TestMainForm)form;
            var asked = Assert.Single(test.Asked);
            Assert.Contains("granny.jpg", asked.Title);
            Assert.Equal(_folder.Path, asked.Folder);
            Assert.Equal("granny.jpg", asked.Name);
            Assert.StartsWith("JPG (*.jpg)|*.jpg|", asked.Filter);

            Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(destination));
            Assert.Equal(File.GetLastWriteTimeUtc(photo), File.GetLastWriteTimeUtc(destination));
            Assert.Equal(File.GetCreationTimeUtc(photo), File.GetCreationTimeUtc(destination));
            Assert.True(await Ui.Until(() => Status(form) == string.Format(ToolbarStrings.StatusSaveAsSavedFormat, "copy of granny.jpg"), 5000));
            Assert.True(File.Exists(photo));
        }, seconds: 60);
    }

    [Fact]
    public void ForAVideo_ItCopiesTheFileTheSameWay()
    {
        var video = _folder.File("holiday.mp4");
        File.WriteAllBytes(video, Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray());
        File.SetLastWriteTimeUtc(video, new DateTime(2013, 7, 7, 12, 0, 0, DateTimeKind.Utc));
        var destination = _folder.File("holiday backup.mp4");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination }, async form =>
        {
            Viewer(form).Tag = video; // what is shown: the file of a video (the poster and the player are no business of this test)

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await SaidSaved(form, destination));

            var asked = Assert.Single(((TestMainForm)form).Asked);
            Assert.Equal("holiday.mp4", asked.Name);
            Assert.StartsWith("MP4 (*.mp4)|*.mp4|", asked.Filter);
            Assert.Equal(File.ReadAllBytes(video), File.ReadAllBytes(destination));
            Assert.Equal(new DateTime(2013, 7, 7, 12, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(destination));
        }, seconds: 60);
    }

    [Fact]
    public void WhenTheWindowIsCancelled_NothingIsSaved_AndTheFileThatIsShownIsNeverWrittenOver()
    {
        var photo = MakePhoto("granny.jpg");
        var before = File.ReadAllBytes(photo);

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = null }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            Button(form, "saveToolStripButton").PerformClick();
            await Task.Delay(500);
            Assert.Single(((TestMainForm)form).Asked);
            Assert.Single(Directory.GetFiles(_folder.Path, "*.jpg"));

            // The same file chosen: it says so and does not copy a file over itself
            ((TestMainForm)form).Answer = photo.ToUpperInvariant();
            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await Ui.Until(() => Status(form) == ToolbarStrings.StatusSaveAsSameFile, 5000));
            Assert.Equal(before, File.ReadAllBytes(photo));
        }, seconds: 60);
    }

    [Fact]
    public void AFileThatIsThere_IsWrittenOver_BecauseTheWindowAskedAndThePersonSaidYes()
    {
        var photo = MakePhoto("granny.jpg");
        var destination = _folder.File("old.jpg");
        File.WriteAllText(destination, "something else");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 2 || FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await SaidSaved(form, destination));
            Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(destination));
        }, seconds: 60);
    }

    // ── the picture of the Refocus bar ──────────────────────────────────────

    [Fact]
    public void WithTheRefocusBarWorking_ItSavesThePictureThatIsOnTheScreen_AsAJpeg_NotTheFile()
    {
        var photo = MakePhoto("granny.jpg");
        var destination = _folder.File(@"portraits\granny blurred.png"); // a wrong extension: it is a JPEG all the same
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination, WithDepth = true }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            Button(form, "refocusToolStripButton").PerformClick();
            Assert.True(await Ui.Until(() => Ui.Controls<RefocusBar>(form).FirstOrDefault() is { Visible: true, CanSave: true }, 15000), "the Refocus bar is working");

            Button(form, "saveToolStripButton").PerformClick();

            var saved = destination + ".jpg";
            Assert.True(await SaidSaved(form, saved), "a PNG name gets .jpg: the file is a JPEG, so it says so");
            var asked = Assert.Single(((TestMainForm)form).Asked);
            Assert.Equal("granny (portrait).jpg", asked.Name);                 // the name the bar's own Save a copy would give
            Assert.StartsWith("JPEG (*.jpg;*.jpeg)|", asked.Filter);
            Assert.Equal(_folder.Path, asked.Folder);

            var bytes = File.ReadAllBytes(saved);
            Assert.True(bytes[0] == 0xFF && bytes[1] == 0xD8, "a JPEG");
            Assert.NotEqual(File.ReadAllBytes(photo), bytes);                // not a copy of the file
            using (var picture = new Bitmap(saved))
                Assert.Equal((W, H), (picture.Width, picture.Height));
            Assert.Equal(File.GetLastWriteTimeUtc(photo), File.GetLastWriteTimeUtc(saved)); // it keeps the dates of the photo
            Assert.True(File.Exists(photo));
            Assert.Equal(photo, Viewer(form).Tag);
        }, seconds: 90);
    }

    [Fact]
    public void WithTheRefocusBarOpenButNotWorking_TheSaveButtonSavesTheFile()
    {
        var photo = MakePhoto("granny.jpg");
        var destination = _folder.File("granny again.jpg");

        // No depth model: the bar does not stay (it closes itself), so it is the photo that is saved
        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination, WithDepth = false }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            Button(form, "refocusToolStripButton").PerformClick();
            Assert.True(await Ui.Until(() => Status(form) == RefocusStrings.StatusNoModel, 10000));

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await SaidSaved(form, destination));
            Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(destination));
            Assert.Equal("granny.jpg", Assert.Single(((TestMainForm)form).Asked).Name);
        }, seconds: 90);
    }

    // ── the pure parts ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\x\a.jpg", @"C:\x\a.jpg")]
    [InlineData(@"C:\x\a.JPEG", @"C:\x\a.JPEG")]
    [InlineData(@"C:\x\a.png", @"C:\x\a.png.jpg")]
    [InlineData(@"C:\x\a", @"C:\x\a.jpg")]
    [InlineData(@"C:\x\a.tif", @"C:\x\a.tif.jpg")]
    public void APictureThatIsSaved_IsNamedAJpeg(string typed, string saved) => Assert.Equal(saved, MainForm.JpegName(typed));
}
