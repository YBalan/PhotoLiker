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
    /// The copy of a photo or a video is done when it has the bytes of the file AND its dates. A file that merely exists may be half written or still open (the length of a file that is being copied is
    /// there at once), the dates are set a moment after the copy, and the status line is no signal: the copy joins the list at once and the window goes on to load it, which replaces "Saved" within a
    /// few milliseconds. A slow machine (the release run on GitHub) found all three.
    /// </summary>
    private static Task<bool> CopyIsDone(string source, string copy, int milliseconds = 20000) =>
        Ui.Until(() =>
        {
            try
            {
                return File.ReadAllBytes(copy).AsSpan().SequenceEqual(File.ReadAllBytes(source))
                    && File.GetLastWriteTimeUtc(copy) == File.GetLastWriteTimeUtc(source)
                    && File.GetCreationTimeUtc(copy) == File.GetCreationTimeUtc(source);
            }
            catch (IOException)
            {
                return false; // not there yet, or still open
            }
        }, milliseconds);

    /// <summary>A picture that was made and saved (a JPEG, not a copy of the file) is done when it can be opened and has the date of the photo.</summary>
    private static Task<bool> PictureIsDone(string source, string saved, int milliseconds = 20000) =>
        Ui.Until(() =>
        {
            try
            {
                using var stream = new FileStream(saved, FileMode.Open, FileAccess.Read, FileShare.None);
                return File.GetLastWriteTimeUtc(saved) == File.GetLastWriteTimeUtc(source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }, milliseconds);

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
            Assert.True(await CopyIsDone(photo, destination), "the copy is there, whole, with the dates of the photo");

            var test = (TestMainForm)form;
            var asked = Assert.Single(test.Asked);
            Assert.Contains("granny.jpg", asked.Title);
            Assert.Equal(_folder.Path, asked.Folder);
            Assert.Equal("granny.jpg", asked.Name);
            Assert.StartsWith("JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG (*.png)|*.png|", asked.Filter); // its own format first: that is what is chosen, so a plain Save is a copy
            Assert.EndsWith("|*.*", asked.Filter);

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
            Assert.True(await CopyIsDone(video, destination));

            var asked = Assert.Single(((TestMainForm)form).Asked);
            Assert.Equal("holiday.mp4", asked.Name);
            Assert.StartsWith("MP4 (*.mp4)|*.mp4|", asked.Filter);
            Assert.DoesNotContain("PNG", asked.Filter); // a video is copied as it is: no picture formats to choose from
            Assert.Equal(File.ReadAllBytes(video), File.ReadAllBytes(destination));
            Assert.Equal(new DateTime(2013, 7, 7, 12, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(destination));
        }, seconds: 60);
    }

    [Fact]
    public void AVideoNamedAsAnotherKind_IsStillACopyOfTheFile_UnderTheNameTheyTyped()
    {
        var video = _folder.File("holiday.avi");
        File.WriteAllBytes(video, Enumerable.Range(0, 3000).Select(i => (byte)(i * 3)).ToArray());
        var destination = _folder.File("holiday.mkv");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination }, async form =>
        {
            Viewer(form).Tag = video;

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await CopyIsDone(video, destination));
            Assert.Equal(File.ReadAllBytes(video), File.ReadAllBytes(destination));
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
            Assert.True(await CopyIsDone(photo, destination));
            Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(destination));
        }, seconds: 60);
    }

    // ── another format: the photo is converted as it is saved ───────────────

    /// <summary>A converted photo is done when it can be opened as the format it says, and has the dates of the photo (they are set after the file is written).</summary>
    private static Task<bool> ConversionIsDone(string source, string converted, int milliseconds = 20000) =>
        Ui.Until(() =>
        {
            try
            {
                using var stream = new FileStream(converted, FileMode.Open, FileAccess.Read, FileShare.None);
                return stream.Length > 0 && File.GetLastWriteTimeUtc(converted) == File.GetLastWriteTimeUtc(source) && File.GetCreationTimeUtc(converted) == File.GetCreationTimeUtc(source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }, milliseconds);

    [Theory]
    [InlineData("granny.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData("granny.bmp", new byte[] { 0x42, 0x4D })]
    [InlineData("granny.gif", new byte[] { 0x47, 0x49, 0x46, 0x38 })]
    [InlineData("granny.tiff", new byte[] { 0x49, 0x49, 0x2A, 0x00 })]
    public void ANameThatSaysAnotherFormat_ConvertsThePhoto_AndKeepsItsDates(string name, byte[] signature)
    {
        var photo = MakePhoto("granny.jpg");
        var before = File.ReadAllBytes(photo);
        var destination = _folder.File(name);

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await ConversionIsDone(photo, destination), "the converted picture is there, with the dates of the photo");

            var bytes = File.ReadAllBytes(destination);
            Assert.True(bytes.AsSpan().StartsWith(signature), $"{name} holds the format its name says");
            using (var picture = new Bitmap(destination))
                Assert.Equal((W, H), (picture.Width, picture.Height));
            Assert.Equal(File.GetLastWriteTimeUtc(photo), File.GetLastWriteTimeUtc(destination));
            Assert.Equal(File.GetCreationTimeUtc(photo), File.GetCreationTimeUtc(destination));
            Assert.Equal(before, File.ReadAllBytes(photo));     // the photo is as it was
            Assert.Equal(photo, Viewer(form).Tag);
            Assert.True(await Ui.Until(() => Status(form) == string.Format(ToolbarStrings.StatusSaveAsSavedFormat, name), 5000));
        }, seconds: 60);
    }

    [Fact]
    public void APngPhoto_CanBeSavedAsAJpeg_AndItsOwnFormatIsTheFirstKindTheWindowOffers()
    {
        var jpeg = MakePhoto("source.jpg");
        var png = _folder.File("granny.png");
        using (var picture = new Bitmap(jpeg))
            picture.Save(png, ImageFormat.Png);
        File.SetLastWriteTimeUtc(png, new DateTime(2011, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        var converted = _folder.File("granny from png.jpeg");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = converted }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 2, 20000));
            Show(form, png);
            Assert.True(await PhotoShown(form, png));

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await ConversionIsDone(png, converted));
            Assert.True(File.ReadAllBytes(converted).AsSpan().StartsWith(new byte[] { 0xFF, 0xD8 }), "a JPEG, which has the extension .jpeg");

            var asked = Assert.Single(((TestMainForm)form).Asked);
            Assert.StartsWith("PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|", asked.Filter); // the PNG first: it is the photo's own format
        }, seconds: 60);
    }

    [Fact]
    public void AnotherSpellingOfThePhotosOwnFormat_IsStillACopyOfTheBytes_NotDecodedAndCodedAgain()
    {
        var photo = MakePhoto("granny.jpg");
        var copy = _folder.File("granny copy.jpeg");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = copy }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            Button(form, "saveToolStripButton").PerformClick();
            Assert.True(await CopyIsDone(photo, copy));
            Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(copy));
        }, seconds: 60);
    }

    [Fact]
    public void ANameThatSaysNoFormat_GetsTheExtensionOfThePhoto_SoTheFileIsWhatItsNameSays()
    {
        var photo = MakePhoto("granny.jpg");
        var typed = _folder.File("my picture.xyz");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = typed }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            Button(form, "saveToolStripButton").PerformClick();

            var saved = typed + ".jpg";
            Assert.True(await CopyIsDone(photo, saved));
            Assert.False(File.Exists(typed));
        }, seconds: 60);
    }

    [Fact]
    public void ANameThatIsChangedBecauseItSaysNoFormat_IsNotWrittenOverAFileThatIsThere_TheWindowDidNotAskAboutIt()
    {
        var photo = MakePhoto("granny.jpg");
        var typed = _folder.File("my picture.xyz");
        var there = typed + ".jpg";
        File.WriteAllText(there, "precious");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = typed }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) >= 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));

            Button(form, "saveToolStripButton").PerformClick();

            Assert.True(await Ui.Until(() => Status(form) == string.Format(ToolbarStrings.StatusSaveAsExistsFormat, "my picture.xyz.jpg"), 5000));
            Assert.Equal("precious", File.ReadAllText(there));
        }, seconds: 60);
    }

    [Fact]
    public void AFileThatIsNoPicture_IsReportedAndNothingIsLeftBehind()
    {
        var photo = _folder.File("broken.jpg");
        File.WriteAllText(photo, "this is not a picture");
        var destination = _folder.File("broken.png");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination }, async form =>
        {
            Viewer(form).Tag = photo;

            Button(form, "saveToolStripButton").PerformClick();

            Assert.True(await Ui.Until(() => Status(form).StartsWith(ToolbarStrings.StatusSaveAsFailedFormat.Split('{')[0]), 10000));
            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part"));
        }, seconds: 60);
    }

    // ── the picture of the Refocus bar ──────────────────────────────────────

    [Fact]
    public void WithTheRefocusBarWorking_ItSavesThePictureThatIsOnTheScreen_InTheFormatTheNameSays_NotTheFile()
    {
        var photo = MakePhoto("granny.jpg");
        var destination = _folder.File(@"portraits\granny blurred.png"); // a PNG: the picture is converted as it is saved
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = destination, WithDepth = true }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            Button(form, "refocusToolStripButton").PerformClick();
            Assert.True(await Ui.Until(() => Ui.Controls<RefocusBar>(form).FirstOrDefault() is { Visible: true, CanSave: true }, 15000), "the Refocus bar is working");

            Button(form, "saveToolStripButton").PerformClick();

            Assert.True(await PictureIsDone(photo, destination), "the picture is there, as the PNG its name says");
            var asked = Assert.Single(((TestMainForm)form).Asked);
            Assert.Equal("granny (portrait).jpg", asked.Name);                 // the name the bar's own Save a copy would give
            Assert.StartsWith("JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG (*.png)|*.png|", asked.Filter); // a JPEG unless the person says otherwise, and they can
            Assert.Equal(_folder.Path, asked.Folder);

            var bytes = File.ReadAllBytes(destination);
            Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "a PNG");
            Assert.NotEqual(File.ReadAllBytes(photo), bytes);                // not a copy of the file
            using (var picture = new Bitmap(destination))
                Assert.Equal((W, H), (picture.Width, picture.Height));
            Assert.Equal(File.GetLastWriteTimeUtc(photo), File.GetLastWriteTimeUtc(destination)); // it keeps the dates of the photo
            Assert.Equal(File.GetCreationTimeUtc(photo), File.GetCreationTimeUtc(destination));
            Assert.True(File.Exists(photo));
            Assert.Equal(photo, Viewer(form).Tag);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.part")); // nothing half written
        }, seconds: 90);
    }

    [Fact]
    public void WithTheRefocusBarWorking_ANameThatSaysNoFormat_IsAJpeg_AsItAlwaysWas()
    {
        var photo = MakePhoto("granny.jpg");
        var typed = _folder.File("portrait.xyz");

        Ui.Run(() => new TestMainForm(_folder.Path) { Answer = typed, WithDepth = true }, async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 20000));
            Show(form, photo);
            Assert.True(await PhotoShown(form, photo));
            Button(form, "refocusToolStripButton").PerformClick();
            Assert.True(await Ui.Until(() => Ui.Controls<RefocusBar>(form).FirstOrDefault() is { Visible: true, CanSave: true }, 15000), "the Refocus bar is working");

            Button(form, "saveToolStripButton").PerformClick();

            var saved = typed + ".jpg";
            Assert.True(await PictureIsDone(photo, saved));
            var bytes = File.ReadAllBytes(saved);
            Assert.True(bytes[0] == 0xFF && bytes[1] == 0xD8, "a JPEG");
            Assert.False(File.Exists(typed));
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
            Assert.True(await CopyIsDone(photo, destination));
            Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(destination));
            Assert.Equal("granny.jpg", Assert.Single(((TestMainForm)form).Asked).Name);
        }, seconds: 90);
    }

    // ── the pure parts ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|All files (*.*)|*.*", 1, ".png")]
    [InlineData("PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|All files (*.*)|*.*", 2, ".jpg")]   // the first pattern of the kind
    [InlineData("PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|All files (*.*)|*.*", 3, null)]     // every file: no extension to add
    [InlineData("PNG (*.png)|*.png", 2, null)]                                                         // there is no such kind
    [InlineData("PNG (*.png)|*.png", 0, null)]
    [InlineData("", 1, null)]
    public void TheExtensionOfTheKindThatIsPicked_IsTheFirstPatternOfIt(string filter, int index, string? extension) => Assert.Equal(extension, MainForm.ExtensionOfFilter(filter, index));

    /// <summary>
    /// The Save As window of Windows refuses a filter that is not made of pairs "text|pattern" (it throws): the tests above answer the window themselves, so the filters are given to the real
    /// one here. (The all files kind was once only the text, and Save As failed for every file.)
    /// </summary>
    [Theory]
    [InlineData(@"C:\x\a.jpg")]
    [InlineData(@"C:\x\a.png")]
    [InlineData(@"C:\x\a.tiff")]
    [InlineData(@"C:\x\a.webp")]  // can be read and copied, not written
    [InlineData(@"C:\x\a.heic")]
    [InlineData(@"C:\x\a.mp4")]
    [InlineData(@"C:\x\a.avi")]
    [InlineData(@"C:\x\a")]
    public void TheKindsOfFileOfAFile_AreAcceptedByTheRealSaveAsWindow(string file)
    {
        var filter = MainForm.CopyFilter(file);

        using var dialog = new SaveFileDialog();
        dialog.Filter = filter; // throws for a filter the window refuses
        Assert.Equal(filter, dialog.Filter);
    }

    [Fact]
    public void TheKindsOfFileOfAPictureMadeByABar_AreAcceptedByTheRealSaveAsWindow_JpegFirst()
    {
        using var dialog = new SaveFileDialog();
        dialog.Filter = MainForm.EditedFilter;

        Assert.StartsWith("JPEG (*.jpg;*.jpeg)|", dialog.Filter);
        Assert.Contains("TIFF", dialog.Filter);
    }

    [Theory]
    [InlineData(@"C:\x\a.webp", "WEBP (*.webp)|*.webp|JPEG ")]   // its own kind first, then the formats it can become
    [InlineData(@"C:\x\a.png", "PNG (*.png)|*.png|JPEG ")]
    [InlineData(@"C:\x\a.tif", "TIFF (*.tiff;*.tif)|*.tiff;*.tif|JPEG ")]
    [InlineData(@"C:\x\a.jpeg", "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG ")]
    public void APhoto_OffersItsOwnFormatFirst_ThenTheOthers_ThenEveryFile(string file, string start)
    {
        var filter = MainForm.CopyFilter(file);

        Assert.StartsWith(start, filter);
        Assert.EndsWith("|*.*", filter);
        Assert.Equal(1, filter.Split('|').Count(part => part.StartsWith("JPEG ")));
    }
}
