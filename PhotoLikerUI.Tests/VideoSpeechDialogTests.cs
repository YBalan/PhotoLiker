using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The table of the Video Speech window, opened for real on a catalog of real videos (made by the ffmpeg of the program; the tests return early without it): the column of the date each
/// video was recorded, and what sorting by it does.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class VideoSpeechDialogTests : IDisposable
{
    private const string Clip = "clip-vit-b32";
    private const int RecordedColumn = 2;

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string MakeClip(string name, string recordedUtc, DateTime? fileDateUtc = null)
    {
        var path = TestFfmpeg.MakeMp4(_folder.Path, name, recordedUtc);
        if (fileDateUtc is { } date)
        {
            File.SetCreationTimeUtc(path, date);
            File.SetLastWriteTimeUtc(path, date);
        }
        return path;
    }

    private static string[] Column(ListView list, int column) =>
        [.. Enumerable.Range(0, list.VirtualListSize).Select(i => list.Items[i].SubItems[column].Text)];

    private static string[] Names(ListView list) => Column(list, 0);

    private static void Sort(VideoSpeechDialog dialog, int column) =>
        typeof(VideoSpeechDialog).GetMethod("SortBy", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [column]);

    [Fact]
    public void TheRecordedColumn_ShowsWhenEachVideoWasRecorded_AndSortsByIt_WithTheUndatedOnesLastEitherWay()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();

        // The names are in another order than the dates, so that sorting by the name and by the date are told apart
        var paths = new[]
        {
            MakeClip("a-2010.mp4", "2010-05-06T07:08:09Z"),
            MakeClip("b-2020.mp4", "2020-09-10T11:12:13Z"),
            MakeClip("c-2003.mp4", "2003-01-31T15:06:00Z"),
            MakeClip("d-nodate.mp4", "1970-01-01T00:00:00Z", fileDateUtc: new DateTime(1987, 3, 4, 5, 6, 7, DateTimeKind.Utc)), // a clock that was reset, in a file that is as old
        };

        Ui.Run(() => new Form(), async host =>
        {
            using var catalog = VideoCatalog.Open(_folder.Path);
            var taken = new Dictionary<string, DateTime?>();
            foreach (var path in paths)
                taken[Path.GetFileName(path)] = (await catalog.GetAsync(path)).Date.Taken;
            Assert.Null(taken["d-nodate.mp4"]); // what the test stands on: this video has no date that can be believed
            Assert.All(new[] { "a-2010.mp4", "b-2020.mp4", "c-2003.mp4" }, name => Assert.NotNull(taken[name]));

            using var dialog = new VideoSpeechDialog(_folder.Path, catalog, Clip, SpeechModels.Small, _folder.Path, string.Empty, () => 1, () => false, dark: false);
            dialog.Show(host);
            var list = Ui.Controls<ListView>(dialog).Single();
            Assert.True(await Ui.Until(() => list.VirtualListSize == 4), "the four videos are listed");

            // The column is there, with the date as the program writes dates everywhere, and nothing for the video with no date
            Assert.StartsWith("Recorded", list.Columns[RecordedColumn].Text);
            Assert.Equal(["a-2010.mp4", "b-2020.mp4", "c-2003.mp4", "d-nodate.mp4"], Names(list)); // by name to begin with
            Assert.Equal(
                [DateText.Display(taken["a-2010.mp4"]!.Value), DateText.Display(taken["b-2020.mp4"]!.Value), DateText.Display(taken["c-2003.mp4"]!.Value), string.Empty],
                Column(list, RecordedColumn));

            Sort(dialog, RecordedColumn); // oldest first
            Assert.Equal(["c-2003.mp4", "a-2010.mp4", "b-2020.mp4", "d-nodate.mp4"], Names(list));
            Assert.Equal("Recorded  ▲", list.Columns[RecordedColumn].Text);

            Sort(dialog, RecordedColumn); // newest first: the one with no date is still last
            Assert.Equal(["b-2020.mp4", "a-2010.mp4", "c-2003.mp4", "d-nodate.mp4"], Names(list));
            Assert.Equal("Recorded  ▼", list.Columns[RecordedColumn].Text);

            Sort(dialog, 0); // back to the names: the other columns still sort as they did, and the cells of the others are where they were
            Assert.Equal(["a-2010.mp4", "b-2020.mp4", "c-2003.mp4", "d-nodate.mp4"], Names(list));
            Assert.Equal(VideoSpeechStrings.StatusNoSound, list.Items[0].SubItems[3].Text); // the status is the column after the date now
            dialog.Close();
        }, seconds: 120);
    }

    private const int StatusColumn = 3, ModelsColumn = 5, SpeechColumn = 7;

    [Fact]
    public void TheModelsColumn_ListsWhoHeardEachVideo_WithATickOnTheOneThatIsTheCaption_AndTheModelBoxSwitchesTheTable()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var a = TestFfmpeg.MakeMp4(_folder.Path, "a.mp4", "2010-05-06T07:08:09Z", sound: true);
        var b = TestFfmpeg.MakeMp4(_folder.Path, "b.mp4", "2011-05-06T07:08:09Z", sound: true);
        var c = TestFfmpeg.MakeMp4(_folder.Path, "c.mp4", "2012-05-06T07:08:09Z", sound: true);

        Ui.Run(() => new Form(), async host =>
        {
            using var catalog = VideoCatalog.Open(_folder.Path);
            var videoA = await catalog.GetAsync(a);
            var videoB = await catalog.GetAsync(b);
            await catalog.GetAsync(c); // no model has heard this one
            catalog.SaveTranscript(videoA.Id, "small", "ru", [new SpeechSegment(0, 900, "small a")]);
            catalog.SaveTranscript(videoA.Id, "base", "ru", [new SpeechSegment(0, 900, "base a one"), new SpeechSegment(1000, 1900, "base a two")]);
            catalog.SaveTranscript(videoB.Id, "base", "ru", [new SpeechSegment(0, 900, "base b")]);

            // The model that is set is small
            using var dialog = new VideoSpeechDialog(_folder.Path, catalog, Clip, SpeechModels.Small, _folder.Path, string.Empty, () => 1, () => false, dark: false);
            dialog.Show(host);
            var list = Ui.Controls<ListView>(dialog).Single();
            var box = Ui.Controls<ComboBox>(dialog).Single(combo => combo.Name == "modelBox");
            Assert.True(await Ui.Until(() => list.VirtualListSize == 3), "the three videos are listed");

            // Who heard each video, with how much; the tick is on the model whose words are the captions: small where it heard anything, else the one that heard most
            Assert.StartsWith(VideoSpeechStrings.ColumnModels, list.Columns[ModelsColumn].Text);
            Assert.Equal(["base (2), small (1) ✓", "base (1) ✓", string.Empty], Column(list, ModelsColumn));

            // The box lists the models with how many videos each has speech for, and shows the one the table is of
            Assert.Equal(["Whisper tiny (0)", "Whisper base (2)", "Whisper small (1)"], box.Items.Cast<object>().Select(item => item.ToString()).ToArray());
            Assert.Equal(2, box.SelectedIndex);
            Assert.Equal(["small a", string.Empty, string.Empty], Column(list, SpeechColumn));
            Assert.Equal([VideoSpeechStrings.StatusHeard, VideoSpeechStrings.StatusNotRead, VideoSpeechStrings.StatusNotRead], Column(list, StatusColumn));

            // Another model: the table is of what that model heard (what it has not heard is "not read yet"), the other texts stay, and the tick does not move
            box.SelectedIndex = 1;
            Assert.True(await Ui.Until(() => Column(list, SpeechColumn)[0] == "base a one base a two"), "the table is of the base model: " + string.Join(" | ", Column(list, SpeechColumn)));
            Assert.Equal(["base a one base a two", "base b", string.Empty], Column(list, SpeechColumn));
            Assert.Equal([VideoSpeechStrings.StatusHeard, VideoSpeechStrings.StatusHeard, VideoSpeechStrings.StatusNotRead], Column(list, StatusColumn));
            Assert.Equal(["base (2), small (1) ✓", "base (1) ✓", string.Empty], Column(list, ModelsColumn));

            box.SelectedIndex = 0; // a model that has heard nothing yet: every video is "not read yet", ready to be heard with it
            Assert.True(await Ui.Until(() => Column(list, StatusColumn).All(status => status == VideoSpeechStrings.StatusNotRead)));
            Assert.Equal([string.Empty, string.Empty, string.Empty], Column(list, SpeechColumn));

            // Videos can be heard again with a model of their own from the right-click menu
            var hearWith = list.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == VideoSpeechStrings.MenuHearWith);
            Assert.Equal(["Whisper tiny", "Whisper base", "Whisper small"], hearWith.DropDownItems.Cast<ToolStripItem>().Select(item => item.Text).ToArray());
            dialog.Close();
        }, seconds: 120);
    }

    [Fact]
    public void TheQuestionOfHearingAgain_NamesTheModelTheVideosAreHeardWith()
    {
        Ui.Run(() => new Form(), async host =>
        {
            using var chooser = new SpeechLanguageDialog("3 videos", "1 min", string.Empty, "ru", "Whisper base", dark: false);
            chooser.Show(host);
            await Task.Delay(100);

            Assert.Contains(Ui.Controls<Label>(chooser), label => label.Text.Contains("Whisper base") && label.Text.Contains("3 videos"));
            chooser.Close();
        });
    }
}
