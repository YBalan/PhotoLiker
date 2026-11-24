using System.Drawing.Imaging;
using System.IO.Compression;
using System.Xml.Linq;
using PhotoLiker.Core;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.PhotoBook;
using TextLine = PhotoLiker.Drawing.Ocr.TextLine;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The pages a Scan manuscripts window starts with (the photos that are loaded, less the ones that were taken out), where the photo stays when a line is selected, and the pictures that are found on
/// the pages: found with a stand-in for the models (the machines that build the program have none), outlined on the photo, cut out and put into the text between its lines, kept with the folder, and in the book where they stand.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptPagesAndPicturesTests : IDisposable
{
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    private readonly TempFolder _folder = new();

    public ManuscriptPagesAndPicturesTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string Photo(string name, int width = 320, int height = 240)
    {
        var path = _folder.File(name);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(240, 236, 220));
            graphics.FillRectangle(Brushes.DarkRed, 240, 130, 60, 70); // the picture on the page, at the right
        }

        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private ManuscriptDialog Dialog(IReadOnlyList<string> start, IReadOnlyList<string>? loaded = null, Func<Task<bool>>? ensurePictures = null)
    {
        var dialog = new ManuscriptDialog(
            _folder.Path, start, new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true), photoChanged: null, ensureCutoutAsync: null, loaded, ensurePictures);
        dialog.AskPagePaths = () => null;
        dialog.AskRemovePage = _ => true;
        dialog.AskReplaceText = () => true;
        dialog.AskBookPath = () => null;
        return dialog;
    }

    private static TextLine Line(string text, float x, float y) => new(text, 0.9f, TextRegion.FromCentre(new PointF(x, y), 200, 30, 0));

    private static ScanResult Three() => new([Line("перший рядок", 160, 40), Line("другий рядок", 160, 80), Line("третій рядок", 160, 120)]);

    private static void StandIn(ManuscriptDialog dialog, Func<ScanResult>? result = null) =>
        dialog.ScanOverride = (_, _, _, _, progress) =>
        {
            progress?.Report((1, 1));
            return (result ?? Three)();
        };

    /// <summary>A picture below the text (a red block of the photo, at the right) and one above it (a strip along the top).</summary>
    private static IReadOnlyList<PagePicture> TwoPictures() =>
    [
        new PagePicture([new PointF(240, 130), new PointF(300, 130), new PointF(300, 200), new PointF(240, 200)]),
        new PagePicture([new PointF(130, 2), new PointF(190, 2), new PointF(190, 20), new PointF(130, 20)]),
    ];

    // ── The pages the window starts with ────────────────────────────────────

    [Fact]
    public void TheWindowStartsWithAllTheLoadedPhotosAsItsPages_InTheirOrder_AndOpensTheShownOne()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var clip = _folder.File("clip.mp4");
        File.WriteAllBytes(clip, [0, 1, 2]);
        var notes = _folder.File("notes.txt");
        File.WriteAllText(notes, "not a photo");

        Ui.Run(() => Dialog([b], [c, clip, a, notes, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(["c.jpg", "a.jpg", "b.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray()); // as the program lists them, with no video and no file that is not a photo
            Assert.Equal(2, dialog.CurrentIndex); // the photo that is shown is the page that is open
            Assert.Equal(3, dialog.PageList.Items.Count);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
        });
    }

    [Fact]
    public void AWindowWithNoLoadedPhotos_StartsWithTheShownOne_AsItDidBefore()
    {
        var a = Photo("a.jpg");
        Photo("b.jpg");

        Ui.Run(() => Dialog([a]), form =>
        {
            Assert.Single(((ManuscriptDialog)form).Pages);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void APhotoTakenOutOnPurpose_DoesNotComeBackWhenTheWindowIsOpenedAgain_AndAddPagesBringsItBack()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        string[] loaded = [a, b, c];

        Ui.Run(() => Dialog([], loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.SelectPage(1);
            dialog.RemoveCurrentPage(); // b.jpg is no page of this manuscript
            Assert.Equal(["a.jpg", "c.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray());
            dialog.SaveWork();
        });

        Assert.Contains("b.jpg", File.ReadAllText(ManuscriptFile.GetPath(_folder.Path)));
        Ui.Run(() => Dialog([], loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.Equal(["a.jpg", "c.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray()); // the loaded photos are all there, and not that one

            dialog.AskPagePaths = () => [b];
            dialog.AddPagesButton.PerformClick(); // asked for by name: a page again
            Assert.Equal(["a.jpg", "c.jpg", "b.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray());
            dialog.SaveWork();
            await Task.CompletedTask;
        });

        Ui.Run(() => Dialog([], loaded), form =>
        {
            Assert.Equal(3, ((ManuscriptDialog)form).Pages.Count);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void APhotoThatIsNewInTheFolder_JoinsAManuscriptThatWasMadeBefore()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([], [a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            await dialog.ScanPageAsync();
        });

        Ui.Run(() => Dialog([], [a, b]), form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.Equal(["a.jpg", "b.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray());
            Assert.True(dialog.Pages[0].IsScanned); // what was done is still there
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AWindowThatWasOnlyLookedAt_LeavesNothingInTheFolder_AndOneThatWasUsedLeavesItsWork()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([], [a, b]), async form =>
        {
            Assert.True(await Ui.Until(() => ((ManuscriptDialog)form).View.HasImage));
        });
        Assert.False(File.Exists(ManuscriptFile.GetPath(_folder.Path)));

        Ui.Run(() => Dialog([], [a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.SelectPage(1);
        });
        Assert.True(File.Exists(ManuscriptFile.GetPath(_folder.Path)));
    }

    [Fact]
    public void AFolderOfManyPhotos_GivesItsFirstOnesAsPages_AndSaysSo()
    {
        var names = Enumerable.Range(0, ManuscriptDialog.MaximumLoadedPages + 25).Select(i => _folder.File($"p{i:D4}.jpg")).ToList();
        string? status = null;

        Ui.Run(() =>
        {
            var dialog = Dialog([], names);
            status = dialog.StatusText;
            return dialog;
        }, form =>
        {
            Assert.Equal(ManuscriptDialog.MaximumLoadedPages, ((ManuscriptDialog)form).Pages.Count);
            Assert.Equal(string.Format(ManuscriptStrings.StatusPagesCappedFormat, ManuscriptDialog.MaximumLoadedPages), status);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AWindowWithHundredsOfPhotos_OpensAtOnce_ReadsTheThumbnailsOfThePagesInSightOnly_AndTheRestWhenTheListIsScrolledToThem()
    {
        // 400 photos: one is made and copied (the window is about the list, not the pictures)
        var first = Photo("p000.jpg");
        var names = new List<string> { first };
        for (var i = 1; i < 400; i++)
        {
            var copy = _folder.File($"p{i:D3}.jpg");
            File.Copy(first, copy);
            names.Add(copy);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Ui.Run(() =>
        {
            var dialog = Dialog([], names);
            Assert.True(watch.ElapsedMilliseconds < 20_000, "the window is made at once, the photos are read in the background");
            return dialog;
        }, async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.Equal(400, dialog.Pages.Count);
            Assert.True(await Ui.Until(() => dialog.PageList.Items[0].ImageIndex > 0), "the first page has its thumbnail");
            await Task.Delay(1500); // time enough for the rest, if it were asked for
            Assert.True(dialog.ThumbnailCount < 100, $"{dialog.ThumbnailCount} thumbnails of 400 pages are read: only the pages in sight are");
            Assert.Equal(0, dialog.PageList.Items[399].ImageIndex);

            dialog.PageList.Items[399].EnsureVisible();
            Assert.True(await Ui.Until(() => dialog.PageList.Items[399].ImageIndex > 0), "scrolled to, a page gets its thumbnail");
        });
    }

    // ── The photo stays in the middle ───────────────────────────────────────

    [Fact]
    public void AFittedPhoto_StaysWhereItIs_WhenALineNearItsEdgeIsSelected()
    {
        using var view = new ManuscriptView { Size = new Size(600, 700) };
        using var picture = new Bitmap(400, 600);
        view.SetImage(new Bitmap(picture), 400);
        view.Lines = [new TextLine("first", 0.9f, TextRegion.FromCentre(new PointF(120, 12), 200, 30, 0))]; // the first line of a page is at the top of the photo
        var origin = view.Origin;

        view.SelectedLine = 0;

        Assert.True(view.IsFitted);
        Assert.Equal(origin, view.Origin);
    }

    [Fact]
    public void ScanningAPage_DoesNotMoveItsPhotoOutOfTheMiddleOfTheWindow()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog, () => new ScanResult([Line("first line at the very top", 160, 8), Line("second", 160, 60)]));
            var origin = dialog.View.Origin;

            await dialog.ScanPageAsync(); // the caret is on the first line, which is selected on the photo

            Assert.Equal(0, dialog.View.SelectedLine);
            Assert.True(dialog.View.IsFitted);
            Assert.Equal(origin, dialog.View.Origin);
        });
    }

    [Fact]
    public void ASelectedLineThatIsOutsideAZoomedPhoto_IsStillBroughtIntoView()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };
        view.SetImage(new Bitmap(2000, 1500), 2000);
        view.Lines = [new TextLine("t", 0.9f, TextRegion.FromCentre(new PointF(1800, 1400), 100, 20, 0))];
        view.ZoomBy(8f);

        view.SelectedLine = 0;

        Assert.Equal(0, view.LineAt(new Point(view.ClientSize.Width / 2, view.ClientSize.Height / 2)));
        Assert.False(view.IsFitted);
    }

    // ── Finding the pictures ────────────────────────────────────────────────

    private static RichDocument TextOf(ManuscriptDialog dialog) => RtfParser.Parse(dialog.Editor.Rtf);

    /// <summary>The text of a page as its paragraphs: a line is itself, a picture is its size and where it sits.</summary>
    private static string Shape(RichDocument document) =>
        string.Join("|", document.Paragraphs.Select(paragraph => paragraph.Picture is { } picture ? $"[{picture.PixelWidth}x{picture.PixelHeight} {paragraph.Align}]" : paragraph.PlainText));

    private static Bitmap Decode(RichPicture picture)
    {
        using var memory = new MemoryStream(picture.Data);
        return new Bitmap(memory);
    }

    [Fact]
    public void AScan_FindsThePicturesToo_AndPutsThemIntoTheTextWhereTheyAreBetweenItsLines()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.PicturesOverride = (_, _) => TwoPictures();
            Assert.True(dialog.ScanPicturesCheck.Checked, "pictures are looked for unless the person turns it off");

            await dialog.ScanPageAsync();

            Assert.Equal(2, dialog.Pages[0].Pictures!.Count);
            Assert.Equal(2, dialog.View.Pictures.Count);
            Assert.Equal((320, 240), (dialog.Pages[0].Width, dialog.Pages[0].Height));
            // The strip at the top is before the first line, in the middle; the red block at the right is after the last
            Assert.Equal("[60x18 Center]|перший рядок|другий рядок|третій рядок|[60x70 Right]", Shape(TextOf(dialog)));
            Assert.Equal(string.Format(ManuscriptStrings.StatusScannedFormat, 1, 3, 90) + " " + string.Format(ManuscriptStrings.StatusPicturesInTextFormat, 2), dialog.StatusText);
        });

        // The text and the pictures in it are kept with the folder
        var stored = ManuscriptFile.Load(_folder.Path)!.Pages![0];
        Assert.Equal(2, stored.Pictures!.Count);
        Assert.Equal(new PointF(240, 130), stored.Pictures[0].ToPicture()!.Outline[0]);
        Assert.Equal("[60x18 Center]|перший рядок|другий рядок|третій рядок|[60x70 Right]", Shape(RtfParser.Parse(stored.Rtf)));
    }

    [Fact]
    public void ThePictureThatIsInTheTextHasTheSeeThroughBackgroundOfItsContour_AlsoAfterThePageIsKept()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            // A triangle round the red block: its other half is paper
            dialog.PicturesOverride = (_, _) => [new PagePicture([new PointF(240, 130), new PointF(300, 130), new PointF(270, 200)])];

            await dialog.FindPicturesAsync();
            dialog.SaveWork();

            var kept = RtfParser.Parse(dialog.Pages[0].Rtf);
            using var picture = Decode(Assert.Single(kept.Paragraphs).Picture!);
            Assert.Equal((60, 70), (picture.Width, picture.Height));
            Assert.Equal(0, picture.GetPixel(2, 66).A); // the bottom left corner is outside the contour
            Assert.Equal(255, picture.GetPixel(30, 12).A);
            Assert.True(picture.GetPixel(30, 12).R > 100 && picture.GetPixel(30, 12).G < 60, "the colour is the photo's");
        });
    }

    [Fact]
    public void AScan_DoesNotLookForPictures_WhenThePersonTurnedItOff_AndTheChoiceIsKept()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            var searches = 0;
            dialog.PicturesOverride = (_, _) => { searches++; return TwoPictures(); };
            dialog.ScanPicturesCheck.Checked = false;

            await dialog.ScanPageAsync();

            Assert.Equal(0, searches);
            Assert.True(dialog.Pages[0].IsScanned);
            Assert.Null(dialog.Pages[0].Pictures); // never looked for: not the same as looked for and none
            Assert.Equal("перший рядок|другий рядок|третій рядок", Shape(TextOf(dialog)));
        });

        Ui.Run(() => Dialog([a]), form =>
        {
            Assert.False(((ManuscriptDialog)form).ScanPicturesCheck.Checked);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AScanAsksForTheModelsOnce_AndGoesOnWithTheTextOnly_WhenTheyAreNotDownloaded_AndDoesNotAskAgainInTheWindow()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var asked = 0;

        Ui.Run(() => Dialog([a, b], ensurePictures: () => { asked++; return Task.FromResult(false); }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);

            await dialog.ScanAllAsync(); // two pages, one question

            Assert.Equal(1, asked);
            Assert.All(dialog.Pages, page => Assert.True(page.IsScanned));
            Assert.All(dialog.Pages, page => Assert.Null(page.Pictures));
            Assert.EndsWith(ManuscriptStrings.StatusPicturesDeclined, dialog.StatusText);

            dialog.Pages[0].Rtf = null;
            dialog.Pages[0].Scanned = null;
            dialog.SelectPage(1);
            dialog.SelectPage(0);
            await dialog.ScanPageAsync();
            Assert.Equal(1, asked); // a no is a no while the window is open

            await dialog.FindPicturesAsync(); // the button asks again
            Assert.Equal(2, asked);
            Assert.Equal(ManuscriptStrings.StatusPicturesDeclined, dialog.StatusText);
        });
    }

    [Fact]
    public void ThePicturesCanBeFoundOnAPageThatHasNoText_AndItIsAPageOfTheBook()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("book.epub");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog, () => new ScanResult([])); // only a picture on it
            dialog.PicturesOverride = (_, _) => TwoPictures();

            await dialog.ScanPageAsync();

            Assert.False(dialog.Editor.IsEmpty, "a picture is no blank");
            Assert.Equal("[60x18 Center]|[60x70 Right]", Shape(TextOf(dialog)));
            Assert.False(ManuscriptText.HasText(dialog.Pages[0]));
            Assert.True(ManuscriptText.HasContent(dialog.Pages[0]));

            dialog.AskBookPath = () => path;
            await dialog.SaveBookAsync();
            Assert.Equal(path, dialog.MadeBookPath);
        });

        using var zip = Open(path);
        Assert.Equal(2, zip.Entries.Count(entry => entry.FullName.StartsWith("OEBPS/images/picture", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheFindPicturesButton_FindsThePicturesOfThePageThatIsOpen_PutsThemInTheText_AndSaysHowMany()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            await dialog.ScanPageAsync(); // with the text there, the pictures go between its lines
            dialog.PicturesOverride = (_, _) => TwoPictures();

            await dialog.FindPicturesAsync();

            Assert.Equal(2, dialog.Pages[0].Pictures!.Count);
            Assert.Equal(2, dialog.View.Pictures.Count);
            Assert.Equal(string.Format(ManuscriptStrings.StatusPicturesFoundFormat, 1, 2), dialog.StatusText);
            Assert.False(dialog.IsBusy);
            Assert.Equal("[60x18 Center]|перший рядок|другий рядок|третій рядок|[60x70 Right]", Shape(TextOf(dialog)));

            // Again: the pictures that were there are replaced, not added to
            await dialog.FindPicturesAsync();
            Assert.Equal("[60x18 Center]|перший рядок|другий рядок|третій рядок|[60x70 Right]", Shape(TextOf(dialog)));

            dialog.PicturesOverride = (_, _) => [];
            await dialog.FindPicturesAsync();
            Assert.Empty(dialog.Pages[0].Pictures!); // looked for, and there are none
            Assert.Empty(dialog.View.Pictures);
            Assert.Equal(string.Format(ManuscriptStrings.StatusNoPicturesFormat, 1), dialog.StatusText);
            Assert.Equal("перший рядок|другий рядок|третій рядок", Shape(TextOf(dialog)));
        });
    }

    [Fact]
    public void APictureBetweenTwoLinesOfThePage_IsBetweenTheirRowsInTheText_AndTheTextStaysTheSame()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            await dialog.ScanPageAsync();
            var before = ManuscriptText.Normalize(ManuscriptText.PlainOf(dialog.Pages[0].Rtf));
            dialog.PicturesOverride = (_, _) => [new PagePicture([new PointF(20, 90), new PointF(80, 90), new PointF(80, 110), new PointF(20, 110)])]; // at the left, level with the 2nd and 3rd lines

            await dialog.FindPicturesAsync();

            Assert.Equal("перший рядок|другий рядок|[60x20 Left]|третій рядок", Shape(TextOf(dialog)));
            Assert.Equal(before, ManuscriptText.Normalize(ManuscriptText.PlainOf(dialog.Pages[0].Rtf))); // a picture is no words
            Assert.Equal(PageState.Scanned, ManuscriptText.StateOf(dialog.Pages[0]));
        });
    }

    [Fact]
    public void APictureThePersonDeletedFromTheText_IsNotInTheBook()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("book.epub");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.PicturesOverride = (_, _) => TwoPictures();
            await dialog.ScanPageAsync();

            // Not a picture after all: it is taken out of the text like any text
            dialog.Editor.Rtf = RtfWriter.Write(new RichDocument([.. TextOf(dialog).Paragraphs.Take(4)])); // all but the last paragraph, which is the red block
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.Equal(path, dialog.MadeBookPath);
        });

        using var zip = Open(path);
        Assert.Single(zip.Entries, entry => entry.FullName.StartsWith("OEBPS/images/picture", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePicturesAreOutlinedAgainWhenThePageIsOpenedAgain_AndNotWhenTheLinesAreHidden()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.PicturesOverride = (_, _) => TwoPictures();
            await dialog.FindPicturesAsync();

            dialog.SelectPage(1);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            Assert.Empty(dialog.View.Pictures);
            Assert.Equal(string.Empty, dialog.Editor.PlainText.Trim());
            dialog.SelectPage(0);
            Assert.True(await Ui.Until(() => dialog.View.Pictures.Count == 2));
            Assert.Equal("[60x18 Center]|[60x70 Right]", Shape(TextOf(dialog))); // and the pictures are in its text again

            // The outlines go with the boxes of the lines when they are hidden
            Assert.True(dialog.ShowLinesButton.Checked);
            dialog.ShowLinesButton.PerformClick();
            Assert.False(dialog.View.ShowLines);
        });
    }

    [Fact]
    public void TheFindPicturesButton_AsksForTheModels_AndNothingIsDoneWhenTheyAreNotDownloaded()
    {
        var a = Photo("a.jpg");
        var asked = 0;

        Ui.Run(() => Dialog([a], ensurePictures: () => { asked++; return Task.FromResult(false); }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            await dialog.FindPicturesAsync();

            Assert.Equal(1, asked);
            Assert.Equal(ManuscriptStrings.StatusPicturesDeclined, dialog.StatusText);
            Assert.Null(dialog.Pages[0].Pictures);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void TurningThePhoto_TurnsTheOutlinesOfItsPicturesWithIt()
    {
        var page = new ManuscriptPage
        {
            Width = 320,
            Height = 240,
            Pictures = [StoredPicture.From(new PagePicture([new PointF(240, 130), new PointF(300, 130), new PointF(300, 200)]))],
        };

        ManuscriptDialog.TurnLines(page, 1); // a quarter clockwise: a point (x, y) goes to (height - y, x)

        Assert.Equal((240, 320), (page.Width, page.Height));
        Assert.Equal([110f, 240f, 110f, 300f, 40f, 300f], page.Pictures![0].Outline!);
    }

    // ── The book ────────────────────────────────────────────────────────────

    private static ZipArchive Open(string path) => ZipFile.OpenRead(path);

    [Fact]
    public void TheBook_HasThePicturesWhereTheyStandInTheText_CutOutAlongTheirContours()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("book.epub");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.PicturesOverride = (_, _) => TwoPictures();
            await dialog.ScanPageAsync();
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.Equal(path, dialog.MadeBookPath);
            Assert.Equal(string.Format(ManuscriptStrings.StatusBookMadeFormat, path, dialog.MadeBookPages), dialog.StatusText);
        });

        using var zip = Open(path);
        var page = XDocument.Parse(new StreamReader(zip.GetEntry("OEBPS/photo0001.xhtml")!.Open()).ReadToEnd()).Descendants(Xhtml + "div").First(div => div.Attribute("class")?.Value == "page");
        var order = page.Elements().Select(e => e.Name.LocalName == "div" ? "picture:" + e.Attribute("class")!.Value : e.Name.LocalName).ToList();
        Assert.Equal(["picture:picture", "p", "p", "p", "picture:picture right"], order); // the strip at the top, the lines, the block at the right below them

        // The block at the right is cut out of the photo along its outline: red inside
        var below = zip.Entries.Single(entry => entry.FullName == "OEBPS/images/picture0001_002.png");
        using var stream = below.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        using var picture = new Bitmap(memory);
        Assert.Equal((60, 70), (picture.Width, picture.Height));
        Assert.Equal(255, picture.GetPixel(30, 35).A);
        Assert.True(picture.GetPixel(30, 35).R > 100 && picture.GetPixel(30, 35).G < 60, "the colour is the photo's");
    }

    [Fact]
    public void ThePicturesAreInThePdfToo()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("book.pdf");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.PicturesOverride = (_, _) => TwoPictures();
            await dialog.ScanPageAsync();
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.Equal(path, dialog.MadeBookPath);
        });

        var bytes = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.True(System.Text.RegularExpressions.Regex.Matches(bytes, "/Subtype ?/Image").Count >= 2, "the two pictures are images of the file");
    }

    [Fact]
    public void TheLanguageOfTheBook_IsTheOneMostOfItsPagesWereScannedIn()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var path = _folder.File("book.epub");

        Ui.Run(() => Dialog([a, b, c]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.ScanPicturesCheck.Checked = false;
            dialog.SelectLanguage(ScanLanguage.Russian);
            await dialog.ScanPageAsync(); // page 1: Russian
            dialog.SelectPage(1);
            dialog.SelectLanguage(ScanLanguage.Ukrainian);
            await dialog.ScanPageAsync(); // page 2: Ukrainian
            dialog.SelectPage(2);
            await dialog.ScanPageAsync(); // page 3: Ukrainian
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();
        });

        using var zip = Open(path);
        var package = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open()).ReadToEnd();
        Assert.Contains("<dc:language>ua</dc:language>", package);
    }
}
