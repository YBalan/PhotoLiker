using System.ComponentModel;
using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Ctrl+D in the main window, pressed for real (through the window's own key handling): the dialog of the "..." of the grid opens for the date taken of a JPEG or the date a video was
/// recorded, starts from the date the file has, and saves what is picked as typing it into that row does. A file that is not a JPEG or a video, and an empty window, only say so in the
/// status bar. The video test is made with a real video from the ffmpeg of the program (it returns early without it).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class DateShortcutTests : IDisposable
{
    private static readonly DateTime CopyDate = new(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Typed = new(2012, 7, 14, 9, 30, 15);
    private const string TakenName = "Date Taken (Original)";

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static PropertyGrid GridOf(Form form) => Field<PropertyGrid>(form, "imageMetaPropertyGrid");

    private static string StatusOf(Form form) => Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text;

    private static string? RowText(Form form, string name) =>
        GridOf(form).SelectedObject is ICustomTypeDescriptor wrapper && wrapper.GetProperties().Cast<PropertyDescriptor>().FirstOrDefault(p => p.Name == name) is { } row
            ? row.GetValue(GridOf(form).SelectedObject) as string
            : null;

    private static bool Same(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) <= 2;

    private static DateTime? OriginalOf(string path)
    {
        using var stream = File.OpenRead(path);
        return ExifReader.Read(stream).Original;
    }

    /// <summary>Presses the keys in the window, the way a key press reaches it (a modal dialog opened by the key runs inside this call, so it is called from the message queue).</summary>
    private static void Press(Form form, Keys keys) =>
        form.BeginInvoke(() =>
        {
            typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [new Message(), keys]);
        });

    private static async Task<Form> PressAndWaitForDialog(Form form, Keys keys)
    {
        Press(form, keys);
        Assert.True(await Ui.Until(() => Ui.Find("DateEditDialog") is not null), "the key opens the date dialog");
        return Ui.Find("DateEditDialog")!;
    }

    /// <summary>A photo the window can show (a header alone would not decode) that says it was taken on 1 Sep 2009 14:30:00, with the dates of a copy for its file.</summary>
    private string RealJpeg(string name)
    {
        var path = _folder.File(name);
        using (var bitmap = new Bitmap(64, 48))
            bitmap.Save(path, ImageFormat.Jpeg);
        JpegExif.Save(path, new ExifChange { Original = new DateTime(2009, 9, 1, 14, 30, 0) });
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        return path;
    }

    private string MakeVideo(string name)
    {
        var path = TestFfmpeg.MakeMp4(_folder.Path, name, "2003-01-31T15:06:00Z");
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        new Settings { CurrentFolder = _folder.Path, SupportVideo = true }.Write();
        return path;
    }

    [Fact]
    public void CtrlD_OnAJpeg_AsksForItsDateTaken_StartingFromTheOneItHas_AndWritesWhatIsPicked()
    {
        var path = RealJpeg("a.jpg");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, TakenName) is not null), "the grid shows the rows of the photo");

            var dialog = await PressAndWaitForDialog(form, Keys.Control | Keys.D);
            Assert.Equal(TakenName, dialog.Text);
            var picker = Ui.Controls<DateTimePicker>(dialog).Single();
            Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), picker.Value);

            picker.Value = Typed;
            Ui.ButtonStartingWith(dialog, "OK").PerformClick();

            Assert.True(await Ui.Until(() => RowText(form, TakenName) == DateText.Display(Typed)), "the grid shows the new date: " + RowText(form, TakenName));
            Assert.Equal(Typed, OriginalOf(path));
            var typedUtc = DateAlignment.ToUtc(Typed);
            Assert.True(Same(File.GetCreationTimeUtc(path), typedUtc) && Same(File.GetLastWriteTimeUtc(path), typedUtc), "the dates of the file follow it");
            Assert.Contains(TakenName, StatusOf(form));
        }, seconds: 120);
    }

    [Fact]
    public void CtrlD_ThenCancel_ChangesNothing()
    {
        var path = RealJpeg("a.jpg");
        var original = File.ReadAllBytes(path);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, TakenName) is not null), "the grid shows the rows of the photo");

            var dialog = await PressAndWaitForDialog(form, Keys.Control | Keys.D);
            Ui.Controls<DateTimePicker>(dialog).Single().Value = Typed;
            Ui.ButtonStartingWith(dialog, "Cancel").PerformClick();

            Assert.True(await Ui.Until(() => Ui.Find("DateEditDialog") is null), "the dialog closes");
            await Task.Delay(300);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.True(Same(File.GetLastWriteTimeUtc(path), CopyDate));
            Assert.DoesNotContain(TakenName + " is", StatusOf(form));
        }, seconds: 120);
    }

    [Fact]
    public void OnlyCtrlD_OpensTheDialog_WithShiftAlsoPressedItDoesNot()
    {
        RealJpeg("a.jpg");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, TakenName) is not null), "the grid shows the rows of the photo");

            Press(form, Keys.Control | Keys.Shift | Keys.D);
            await Task.Delay(500);

            Assert.Null(Ui.Find("DateEditDialog"));
        }, seconds: 60);
    }

    [Fact]
    public void CtrlD_OnAVideo_AsksForWhenItWasRecorded_AndWritesItIntoTheVideoAndItsFile()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var path = MakeVideo("SDV_0667.mp4");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, VideoMetadata.RecordedName) is not null), "the grid shows the rows of the video");
            Assert.True(DateText.TryParseTyped(RowText(form, VideoMetadata.RecordedName), out var recorded), "the video says when it was recorded");

            var dialog = await PressAndWaitForDialog(form, Keys.Control | Keys.D);
            Assert.Equal(VideoMetadata.RecordedName, dialog.Text);
            var picker = Ui.Controls<DateTimePicker>(dialog).Single();
            Assert.True(Same(recorded, picker.Value), "the dialog starts from the date the video has");

            picker.Value = Typed;
            Ui.ButtonStartingWith(dialog, "OK").PerformClick();

            Assert.True(await Ui.Until(() => RowText(form, VideoMetadata.RecordedName)?.StartsWith("2012-07-14 09:30:15") == true), "the grid shows the new date: " + RowText(form, VideoMetadata.RecordedName));
            var typedUtc = DateAlignment.ToUtc(Typed);
            Assert.True(Same(File.GetCreationTimeUtc(path), typedUtc) && Same(File.GetLastWriteTimeUtc(path), typedUtc), "the dates of the file are that moment too");
            Assert.True(Same((await VideoProbe.ReadAsync(path)).Recorded!.Value, Typed), "the video itself says it now");
        }, seconds: 120);
    }

    [Fact]
    public void CtrlD_OnAFileThatIsNotAJpegOrAVideo_OpensNothing_AndSaysWhy()
    {
        using (var bitmap = new Bitmap(32, 24))
            bitmap.Save(_folder.File("a.png"), ImageFormat.Png);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => Field<PictureBox>(form, "pictureBox1").Tag is string), "the photo is shown");

            Press(form, Keys.Control | Keys.D);
            Assert.True(await Ui.Until(() => StatusOf(form).Contains("is not a JPEG or a video")), "the status says why: " + StatusOf(form));
            Assert.Contains("a.png", StatusOf(form));
            Assert.Null(Ui.Find("DateEditDialog"));
        }, seconds: 60);
    }

    [Fact]
    public void CtrlD_WithNothingShown_OpensNothing_AndSaysSo()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            Press(form, Keys.Control | Keys.D);
            Assert.True(await Ui.Until(() => StatusOf(form) == MainFormStrings.StatusDateNoPhoto), "the status says there is nothing: " + StatusOf(form));
            Assert.Null(Ui.Find("DateEditDialog"));
        }, seconds: 60);
    }
}
