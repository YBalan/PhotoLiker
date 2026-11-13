using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The date taken in the bottom right corner of the viewer, made for real: it is there while the panel of properties is folded into its tab, drawn on the picture (looked at in the
/// pixels the picture box paints), and it is not there while the panel is open. The video test needs the ffmpeg of the program (it returns early without it).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class DateMarkTests : IDisposable
{
    private static readonly string ConfigFile = Path.Combine(AppPaths.DataFolder, "global-config.json");
    private static readonly DateTime CopyDate = new(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);
    private const string TakenName = "Date Taken (Original)";

    private readonly TempFolder _folder = new();

    public DateMarkTests() => DeleteConfig(); // every test starts with the panel open, as the program does

    public void Dispose()
    {
        _folder.Dispose();
        DeleteConfig();
    }

    private static void DeleteConfig()
    {
        try { File.Delete(ConfigFile); } catch (IOException) { }
    }

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static PropertiesDock Dock(Form form) => Field<PropertiesDock>(form, "_dock");

    private static PropertiesTab Tab(Form form) => Field<PropertiesTab>(form, "_dockTab");

    private static PictureBox Viewer(Form form) => Field<PictureBox>(form, "pictureBox1");

    private static PropertyGrid GridOf(Form form) => Field<PropertyGrid>(form, "imageMetaPropertyGrid");

    private static string? DateMark(Form form) => ((MainForm)form).DateMarkText();

    private static string? RowText(Form form, string name) =>
        GridOf(form).SelectedObject is System.ComponentModel.ICustomTypeDescriptor wrapper
        && wrapper.GetProperties().Cast<System.ComponentModel.PropertyDescriptor>().FirstOrDefault(p => p.Name == name) is { } row
            ? row.GetValue(GridOf(form).SelectedObject) as string
            : null;

    private static async Task Settle() => await Task.Delay(150);

    private static void Click(Control control) =>
        typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [EventArgs.Empty]);

    /// <summary>A photo the window can show, that says it was taken on 1 Sep 2009 14:30:00 (a copy: its file says 4 Mar 2025).</summary>
    private string RealJpeg(string name, DateTime? taken = null)
    {
        var path = _folder.File(name);
        using (var bitmap = new Bitmap(64, 48))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Black);
            bitmap.Save(path, ImageFormat.Jpeg);
        }
        if (taken is { } date)
            JpegExif.Save(path, new ExifChange { Original = date });
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        return path;
    }

    /// <summary>What the picture box paints now, as pixels (its Paint event is what draws the marks).</summary>
    private static Bitmap Painted(Form form)
    {
        var box = Viewer(form);
        var bitmap = new Bitmap(box.Width, box.Height);
        box.DrawToBitmap(bitmap, new Rectangle(0, 0, box.Width, box.Height));
        return bitmap;
    }

    /// <summary>How many pure white pixels the bottom right corner has: the white text of the mark (the photo is black, the box is gray, nothing else there is white).</summary>
    private static int WhiteInTheCorner(Bitmap picture) => WhiteIn(picture, new Rectangle(picture.Width - 320, picture.Height - 70, 320, 70));

    private static int WhiteIn(Bitmap picture, Rectangle area)
    {
        var white = 0;
        for (var y = Math.Max(0, area.Top); y < Math.Min(picture.Height, area.Bottom); y++)
        {
            for (var x = Math.Max(0, area.Left); x < Math.Min(picture.Width, area.Right); x++)
            {
                var pixel = picture.GetPixel(x, y);
                if (pixel.R == 255 && pixel.G == 255 && pixel.B == 255)
                    white++;
            }
        }
        return white;
    }

    [Fact]
    public void WhileThePanelIsOpen_NoDateIsWritten_AndFoldingItWritesTheDateTaken()
    {
        RealJpeg("a.jpg", new DateTime(2009, 9, 1, 14, 30, 0));

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, TakenName) is not null), "the grid shows the rows of the photo");
            Assert.True(Dock(form).Visible);
            Assert.Null(DateMark(form)); // the date is in the grid

            Dock(form).CollapseItem.PerformClick();
            await Settle();

            Assert.False(Dock(form).Visible);
            Assert.Equal("01 Sep 2009 14:30:00", DateMark(form)); // the date of the photo, not the 2025 of its file
            Assert.Equal(RowText(form, TakenName), DateMark(form)); // the very text the grid had

            Click(Tab(form)); // the panel is back
            await Settle();

            Assert.True(Dock(form).Visible);
            Assert.Null(DateMark(form));
        }, seconds: 60);
    }

    [Fact]
    public void TheDate_IsDrawnInTheBottomRightCornerOfThePhoto_OnlyWhileThePanelIsFolded()
    {
        RealJpeg("a.jpg", new DateTime(2009, 9, 1, 14, 30, 0));

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, TakenName) is not null && Viewer(form).Image is not null), "the photo is shown");
            await Settle();
            using (var open = Painted(form))
                Assert.Equal(0, WhiteInTheCorner(open)); // nothing white in that corner: the photo is black, the box is gray

            Dock(form).CollapseItem.PerformClick();
            await Settle();

            using var folded = Painted(form);
            Assert.True(WhiteInTheCorner(folded) > 40, "the white letters of the date are in the corner: " + WhiteInTheCorner(folded));

            // ... and not at the top of the same side
            Assert.Equal(0, WhiteIn(folded, new Rectangle(folded.Width - 320, 0, 320, 40)));
        }, seconds: 60);
    }

    [Fact]
    public void APhotoWithNoDateInIt_ShowsTheDateOfItsFile()
    {
        RealJpeg("plain.jpg");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => Viewer(form).Image is not null && GridOf(form).SelectedObject is not null), "the photo is shown");
            Dock(form).CollapseItem.PerformClick();
            await Settle();

            Assert.Equal(DateText.Display(DateTime.SpecifyKind(CopyDate, DateTimeKind.Utc).ToLocalTime()), DateMark(form));
        }, seconds: 60);
    }

    [Fact]
    public void TheFoldedState_IsRemembered_SoTheNextStartWritesTheDateFromTheFirstPhoto()
    {
        RealJpeg("a.jpg", new DateTime(2009, 9, 1, 14, 30, 0));

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, TakenName) is not null), "the grid shows the rows of the photo");
            Dock(form).CollapseItem.PerformClick();
            await Settle();
        }, seconds: 60);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => DateMark(form) is not null), "the date is in the corner at once, the panel being folded");
            Assert.Equal("01 Sep 2009 14:30:00", DateMark(form));
        }, seconds: 60);
    }

    [Fact]
    public void AVideo_ShowsTheDateItWasRecorded_AndTheDateDoesNotCoverTheLengthOnItsPoster()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();
        var path = TestFfmpeg.MakeMp4(_folder.Path, "SDV_0667.mp4", "2003-01-31T15:06:00Z");
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        new Settings { CurrentFolder = _folder.Path, SupportVideo = true }.Write();

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => RowText(form, VideoMetadata.RecordedName) is not null && Viewer(form).Image is not null), "the grid shows the rows of the video");
            Dock(form).CollapseItem.PerformClick();
            await Settle();

            Assert.Equal(RowText(form, VideoMetadata.RecordedName), DateMark(form)); // the text of the Recorded row

            // The poster has its length in that corner: the date is above it, the two do not share the pixels
            using var folded = Painted(form);
            var lengthBand = new Rectangle(folded.Width - 140, folded.Height - 40, 120, 30);
            var dateBand = new Rectangle(folded.Width - 320, folded.Height - 90, 310, 40);
            Assert.True(WhiteIn(folded, lengthBand) > 10, "the length of the video is still there");
            Assert.True(WhiteIn(folded, dateBand) > 40, "and the date above it");
        }, seconds: 120);
    }

}
