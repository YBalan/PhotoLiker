using System.Globalization;
using PhotoLiker.Core;
using PhotoLiker.Search;
using PhotoLiker.Search.Tests;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Align dates window and its "Use a date..." question, opened for real: what the buttons and lists do and say, and what a run started from the window does to the files. Each test
/// is a thing a person ran into (the answers to Selected and to the date of the file were grey or empty, the files vanished from the list without a word, the tick's text was cut off).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class DateAlignmentDialogTests : IDisposable
{
    private static readonly DateTime CopyDate = new(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private DateAlignmentDialog Create(VideoCatalog? videos = null) =>
        new(_folder.Path, true, [".jpg", ".mp4"], _folder.Liked, null, videos, dark: false);

    private static ListView ListOf(Form align) => Ui.Controls<ListView>(align).Single();

    /// <summary>Presses "Use a date..." and waits for the question it opens.</summary>
    private static async Task<Form> OpenQuestion(Form align)
    {
        align.BeginInvoke(() => Ui.ButtonStartingWith(align, "Use a date").PerformClick());
        Assert.True(await Ui.Until(() => Ui.Find("AssignDateDialog") is not null), "the button opens the question");
        return Ui.Find("AssignDateDialog")!;
    }

    private static async Task Answer(Form question)
    {
        var ok = Ui.ButtonStartingWith(question, "Use this date");
        Assert.True(ok.Enabled, "the date can be used");
        ok.PerformClick();
        Assert.True(await Ui.Until(() => Ui.Find("AssignDateDialog") is null), "the question closes");
    }

    private static int RowOf(ListView list, string photo)
    {
        for (var i = 0; i < list.VirtualListSize; i++)
        {
            if (list.Items[i].Text == photo)
                return i;
        }
        throw new InvalidOperationException(photo + " is not in the list");
    }

    private static string DisplayLocal(DateTime utc) => DateText.Display(utc.ToLocalTime());

    private static bool Same(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) <= 2;

    private static DateTime? OriginalOf(string path)
    {
        using var stream = File.OpenRead(path);
        return ExifReader.Read(stream).Original;
    }

    // ── "Selected" was grey for files that already had a date ──────────

    [Fact]
    public void SelectedFilesThatAlreadyHaveADate_CanBeGivenAnother_AndARunWritesIt()
    {
        var first = _folder.Write("good1.jpg", ExifJpeg.Build(original: "2009:09:01 14:30:00"), CopyDate);
        _folder.Write("good2.jpg", ExifJpeg.Build(original: "2010:10:02 10:00:00"), CopyDate);
        var given = new DateTime(2012, 7, 14, 9, 30, 0);

        Ui.Run(() => Create(), async align =>
        {
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Will change (2)")), "both photos are to be changed to their own dates");
            var list = ListOf(align);
            list.SelectedIndices.Add(RowOf(list, "good1.jpg"));

            var question = await OpenQuestion(align);
            var selected = Ui.RadioStartingWith(question, "The 1 selected");
            Assert.True(selected.Enabled && selected.Checked, "Selected is on and ticked, though the photo has a usable date");
            Assert.False(Ui.RadioStartingWith(question, "All ").Enabled, "no photo waits for a date, so there is nobody for All");
            Ui.Controls<DateTimePicker>(question).Single().Value = given;
            Assert.True(Ui.RadioStartingWith(question, "This date").Checked, "typing a date chooses it");
            await Answer(question);

            Assert.Equal("14 Jul 2012 09:30:00 (given)", list.Items[RowOf(list, "good1.jpg")].SubItems[1].Text);
            Assert.StartsWith("Gave 1 photo a date taken", Ui.LabelStartingWith(align, "Gave"));

            Ui.ButtonStartingWith(align, "Change ").PerformClick();
            Assert.True(await Ui.Until(() => Ui.LabelStartingWith(align, "Done").Length > 0), "the run finishes");

            Assert.True(Same(File.GetCreationTimeUtc(first), DateAlignment.ToUtc(given)) && Same(File.GetLastWriteTimeUtc(first), DateAlignment.ToUtc(given)), "its file dates are the given date");
            Assert.Equal(given, OriginalOf(first));

            // The given date is in the file now: nothing is "given" any more, and the list agrees with the disk
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Already match (2)") && Ui.HasRadio(align, "Will change (0)")), "both photos match now");
            Ui.RadioStartingWith(align, "All").Checked = true;
            await Ui.Until(() => list.VirtualListSize == 2);
            for (var i = 0; i < list.VirtualListSize; i++)
                Assert.DoesNotContain("(given)", list.Items[i].SubItems[1].Text);
        });
    }

    [Fact]
    public void WithNothingSelected_TheQuestionIsForThePhotosThatWaitForADate()
    {
        _folder.Write("lost.jpg", ExifJpeg.Build(original: "2001:01:01 00:00:05"), CopyDate); // a camera that lost its clock

        Ui.Run(() => Create(), async align =>
        {
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "No usable EXIF date (1)")), "the photo waits for a date");

            var question = await OpenQuestion(align);

            var selected = Ui.RadioStartingWith(question, "The 0 selected");
            var all = Ui.RadioStartingWith(question, "All 1 photos");
            Assert.False(selected.Enabled, "nothing is selected");
            Assert.True(all.Enabled && all.Checked, "so the question is for all that wait");
            Ui.ButtonStartingWith(question, "Cancel").PerformClick();
            await Ui.Until(() => Ui.Find("AssignDateDialog") is null);
        });
    }

    // ── "The date of the file" gave nothing for files from a card with a reset clock ─────

    [Fact]
    public void AFileDateBefore1995_CanBeUsedAsTheDate_BecauseThePersonChoseIt()
    {
        var fileDate = new DateTime(1987, 8, 23, 12, 18, 35, DateTimeKind.Utc);
        _folder.Write(@"2015-05-01\lost.jpg", ExifJpeg.Build(original: "2001:01:01 00:00:05"), fileDate);

        Ui.Run(() => Create(), async align =>
        {
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "No usable EXIF date (1)")));
            Ui.RadioStartingWith(align, "No usable").Checked = true;
            var list = ListOf(align);
            Assert.True(await Ui.Until(() => list.VirtualListSize == 1));
            list.SelectedIndices.Add(0);

            var question = await OpenQuestion(align);
            Ui.RadioStartingWith(question, "The date of the file").Checked = true;

            Assert.Contains(Ui.Controls<Label>(question), l => l.Text == $"1 of 1, for example {DisplayLocal(fileDate)}");
            await Answer(question);
            Assert.StartsWith("Gave 1 photo a date taken", Ui.LabelStartingWith(align, "Gave"));
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "No usable EXIF date (0)")), "the photo is not waiting for a date any more");
        });
    }

    // ── the files vanished from "Will change" without a word ───────────

    [Fact]
    public void WhenTheGivenDateIsAlreadyTheFileDates_TheStatusLineSaysWhyNothingWillChange()
    {
        _folder.Write("clip.jpg", ExifJpeg.Build(original: "2003:01:31 18:06:00"), CopyDate);

        Ui.Run(() => Create(), async align =>
        {
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Will change (1)")));
            ListOf(align).SelectedIndices.Add(0);

            var question = await OpenQuestion(align);
            Ui.RadioStartingWith(question, "The date of the file").Checked = true;
            Ui.Controls<CheckBox>(question).Single().Checked = false; // nothing is written into the photo: only its file dates would change, and they are that date
            await Answer(question);

            var status = Ui.LabelStartingWith(align, "Gave");
            Assert.Contains("nothing to change", status);
            Assert.Contains("Already match", status);
            Assert.True(Ui.HasRadio(align, "Already match (1)") && Ui.HasRadio(align, "Will change (0)"), "the photo went to the list of those that match");
        });
    }

    // ── the text of the tick was cut off ───────────────────────────────

    [Fact]
    public void TheTextOfTheTick_IsShownInFull_AndDoesNotRunIntoTheButtons()
    {
        _folder.Write("a.jpg", ExifJpeg.Build(original: "2009:09:01 14:30:00"), CopyDate);

        Ui.Run(() => Create(), async align =>
        {
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Will change (1)")));
            ListOf(align).SelectedIndices.Add(0);

            var question = await OpenQuestion(align);
            var tick = Ui.Controls<CheckBox>(question).Single();
            var ok = Ui.ButtonStartingWith(question, "Use this date");
            var tickBox = question.RectangleToClient(tick.RectangleToScreen(tick.ClientRectangle));
            var okBox = question.RectangleToClient(ok.RectangleToScreen(ok.ClientRectangle));

            Assert.True(tick.Height >= tick.Font.Height * 2, $"the tick has room for more than one line (it is {tick.Height} high, a line {tick.Font.Height})");
            Assert.True(tickBox.Bottom <= okBox.Top, "it ends above the buttons");
            Assert.True(tickBox.Right <= question.ClientSize.Width, "and inside the window");
            var needed = TextRenderer.MeasureText(tick.Text, tick.Font, new Size(tick.Width - 22, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            Assert.True(tick.Height >= needed, $"its text is {needed} high, the tick {tick.Height}");

            Ui.ButtonStartingWith(question, "Cancel").PerformClick();
            await Ui.Until(() => Ui.Find("AssignDateDialog") is null);
        });
    }

    // ── videos ─────────────────────────────────────────────────────────

    [Fact]
    public void AGivenDateIsWrittenIntoAVideo_TheCatalogAndTheListFollow_AndUndoPutsEverythingBack()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var path = TestFfmpeg.MakeMp4(_folder.Path, "SDV_0667.mp4", "2003-01-31T15:06:00Z");
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        var original = File.ReadAllBytes(path);
        var wanted = DateTime.SpecifyKind(CopyDate, DateTimeKind.Utc).ToLocalTime(); // the date of the file, which the person says is the date
        using var catalog = VideoCatalog.Open(_folder.Path, _folder.File("catalog.db"));

        Ui.Run(() => Create(catalog), async align =>
        {
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Will change (1)")), "the video, dated by what it says (2003), is to have its file dates changed to that");
            var list = ListOf(align);
            list.SelectedIndices.Add(0);

            var question = await OpenQuestion(align);
            Assert.True(Ui.RadioStartingWith(question, "The 1 selected").Enabled);
            Assert.True(Ui.Controls<CheckBox>(question).Single().Checked, "the date is written into the file unless that is turned off");
            Ui.RadioStartingWith(question, "The date of the file").Checked = true;
            await Answer(question);

            // Its file dates are the date, so only the date it says it was recorded is left to change: it stays among the files to change
            Assert.True(Ui.HasRadio(align, "Will change (1)"));
            Assert.Contains("date recorded", list.Items[0].SubItems[4].Text);

            Ui.ButtonStartingWith(align, "Change ").PerformClick();
            Assert.True(await Ui.Until(() => Ui.LabelStartingWith(align, "Done").Length > 0), "the run finishes");

            var written = File.ReadAllBytes(path);
            Assert.Equal(original.Length, written.Length);
            Assert.NotEqual(original, written);
            Assert.True(Same(File.GetCreationTimeUtc(path), CopyDate) && Same(File.GetLastWriteTimeUtc(path), CopyDate), "its file dates are as they were");
            Assert.True(Same((await VideoProbe.ReadAsync(path)).Recorded!.Value, wanted), "ffmpeg reads the new date from the file itself");
            Assert.True(Same((await catalog.GetAsync(path)).Date.Taken!.Value, wanted), "and the catalog knows the video by it");
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Already match (1)")), "the list agrees");

            // Undo
            var dialog = (DateAlignmentDialog)align;
            dialog.ConfirmUndo = _ => true;
            align.BeginInvoke(() => Ui.ButtonStartingWith(align, "Undo").PerformClick());
            Assert.True(await Ui.Until(() => Ui.LabelStartingWith(align, "Put back").Length > 0), "the undo finishes");
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(2003, (await catalog.GetAsync(path)).Date.Taken!.Value.Year);
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Will change (1)")), "the video is to be changed again, as at the start");
        });
    }

    [Fact]
    public void WithTheTickOff_AVideoOnlyGetsItsFileDates()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var path = TestFfmpeg.MakeMp4(_folder.Path, "clip.mp4", "2003-01-31T15:06:00Z");
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        var original = File.ReadAllBytes(path);
        var given = new DateTime(2012, 7, 14, 9, 30, 0);
        using var catalog = VideoCatalog.Open(_folder.Path, _folder.File("catalog.db"));

        Ui.Run(() => Create(catalog), async align =>
        {
            Assert.True(await Ui.Until(() => Ui.HasRadio(align, "Will change (1)")));
            ListOf(align).SelectedIndices.Add(0);

            var question = await OpenQuestion(align);
            Ui.Controls<DateTimePicker>(question).Single().Value = given;
            Ui.Controls<CheckBox>(question).Single().Checked = false;
            await Answer(question);
            Assert.DoesNotContain("date recorded", ListOf(align).Items[0].SubItems[4].Text);

            Ui.ButtonStartingWith(align, "Change ").PerformClick();
            Assert.True(await Ui.Until(() => Ui.LabelStartingWith(align, "Done").Length > 0), "the run finishes");

            Assert.Equal(original, File.ReadAllBytes(path)); // the video itself was not touched
            Assert.True(Same(File.GetCreationTimeUtc(path), DateAlignment.ToUtc(given)) && Same(File.GetLastWriteTimeUtc(path), DateAlignment.ToUtc(given)), "only its file dates changed");
        });
    }
}
