using System.ComponentModel;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The "Recorded" row of a video in the metadata grid of the main window, edited for real: the date typed there is written into the video and into the dates of its file, the grid and the
/// catalog show it, and a video that has no place for a date inside gets the dates of its file and the status says so. The window is made with a real video from the ffmpeg of the program
/// (the tests return early without it).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class VideoDateEditTests : IDisposable
{
    private static readonly DateTime CopyDate = new(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Typed = new(2012, 7, 14, 9, 30, 15);
    private const string Clip = "clip-vit-b32";

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static PropertyGrid GridOf(Form form) => Field<PropertyGrid>(form, "imageMetaPropertyGrid");

    private static string StatusOf(Form form) => Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text ?? string.Empty;

    private static PropertyDescriptor? Row(Form form, string name) =>
        GridOf(form).SelectedObject is ICustomTypeDescriptor wrapper
            ? wrapper.GetProperties().Cast<PropertyDescriptor>().FirstOrDefault(p => p.Name == name)
            : null;

    private static string? RecordedText(Form form) => Row(form, VideoMetadata.RecordedName) is { } row ? row.GetValue(GridOf(form).SelectedObject) as string : null;

    private static bool Same(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) <= 2;

    /// <summary>A video made by ffmpeg that says it was recorded in 2003, with the dates of a copy; the folder is set to list videos.</summary>
    private string MakeVideo(string name)
    {
        var path = TestFfmpeg.MakeMp4(_folder.Path, name, "2003-01-31T15:06:00Z"); // the container follows the extension
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        new Settings { CurrentFolder = _folder.Path, SupportVideo = true }.Write();
        return path;
    }

    [Fact]
    public void TheRecordedRowOfAnMp4_IsEditable_AndWhatIsTypedThereIsWrittenIntoTheVideoAndItsFile()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var path = MakeVideo("SDV_0667.mp4");
        var original = File.ReadAllBytes(path);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RecordedText(form) is not null), "the grid shows the rows of the video");
            Assert.False(Row(form, VideoMetadata.RecordedName)!.IsReadOnly, "the date it was recorded can be edited");
            Assert.True(Row(form, VideoMetadata.DurationName)!.IsReadOnly, "the other rows cannot");
            Assert.True(Row(form, VideoMetadata.FrameSizeName)!.IsReadOnly);
            Assert.True(Row(form, VideoMetadata.FileDatesName)!.IsReadOnly);
            Assert.DoesNotContain("2012", RecordedText(form));

            // The video has been analyzed (moments, speech): that is hours of work on a real one, and a date must not undo it
            var catalog = VideoLibrary.Current!;
            var video = await catalog.GetAsync(path);
            catalog.SaveAnalysis(video.Id, Clip, [new MomentData(0, [1, 0, 0], [0xFF, 0xD8, 0xFF, 0xD9]), new MomentData(1000, [0, 1, 0], [0xFF, 0xD8, 0xFF, 0xD9])]);
            catalog.SaveTranscript(video.Id, "small", "ru", [new SpeechSegment(0, 900, "раз")]);

            // As a person types it into the row: the grid hands the text to the row
            Row(form, VideoMetadata.RecordedName)!.SetValue(GridOf(form).SelectedObject, DateText.Display(Typed));

            Assert.True(await Ui.Until(() => RecordedText(form) == DateText.Display(Typed)), "the grid shows the new date: " + RecordedText(form));
            Assert.Contains("Recorded", StatusOf(form));
            Assert.Contains("dates of the file", StatusOf(form));

            var written = File.ReadAllBytes(path);
            Assert.Equal(original.Length, written.Length); // no byte moves
            Assert.NotEqual(original, written);
            var typedUtc = DateAlignment.ToUtc(Typed);
            Assert.True(Same(File.GetCreationTimeUtc(path), typedUtc) && Same(File.GetLastWriteTimeUtc(path), typedUtc), "the dates of the file are that moment too");
            Assert.True(Same((await VideoProbe.ReadAsync(path)).Recorded!.Value, Typed), "ffmpeg reads the new date from the file itself");
            var known = await catalog.GetAsync(path);
            Assert.True(Same(known.Date.Taken!.Value, Typed), "and the catalog knows the video by it");
            Assert.Equal(video.Id, known.Id); // the same video, not one that was read again
            Assert.Equal((1, 2), catalog.CountAnalyzed(Clip));
            Assert.Equal((1, 1), catalog.CountTranscribed("small"));
            Assert.Equal(DateText.Display(Typed) + " (= " + VideoMetadata.RecordedName + ")", Row(form, VideoMetadata.FileDatesName)!.GetValue(GridOf(form).SelectedObject)); // the grid itself says the file is as old as the recording
        }, seconds: 120);
    }

    [Fact]
    public void AVideoThatCannotHoldADate_GetsTheDatesOfItsFile_AndTheStatusSaysSo()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var path = MakeVideo("clip.mkv"); // Matroska: nothing is written into it
        var original = File.ReadAllBytes(path);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RecordedText(form) is not null), "the grid shows the rows of the video");

            Row(form, VideoMetadata.RecordedName)!.SetValue(GridOf(form).SelectedObject, DateText.Display(Typed));

            Assert.True(await Ui.Until(() => StatusOf(form).Contains("no place for a date")), "the status says why only the file changed: " + StatusOf(form));
            var typedUtc = DateAlignment.ToUtc(Typed);
            Assert.True(Same(File.GetLastWriteTimeUtc(path), typedUtc) && Same(File.GetCreationTimeUtc(path), typedUtc), "the dates of the file are the date");
            Assert.Equal(original, File.ReadAllBytes(path));
        }, seconds: 120);
    }

    [Fact]
    public void ADateThatIsNotADate_IsRefusedByTheRow_AndNothingChanges()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var path = MakeVideo("a.mp4");
        var original = File.ReadAllBytes(path);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RecordedText(form) is not null), "the grid shows the rows of the video");

            Assert.Throws<FormatException>(() => Row(form, VideoMetadata.RecordedName)!.SetValue(GridOf(form).SelectedObject, "yesterday"));

            await Task.Delay(300);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.True(Same(File.GetLastWriteTimeUtc(path), CopyDate));
        }, seconds: 120);
    }
}
