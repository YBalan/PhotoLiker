using System.Drawing.Imaging;
using System.IO.Compression;
using PdfSharp.Pdf.IO;
using PhotoLiker.Core;
using PhotoLiker.PhotoBook;

namespace PhotoLikerUI.Tests;

/// <summary>The Make a book window opened for real: its sources, the order of the photos, what is written for them, what is kept with the folder, and the PDF and the EPUB it makes.</summary>
[Collection(WindowsCollection.Name)]
public sealed class BookDialogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public BookDialogTests() => new GlobalConfig().Save();

    public void Dispose() => _folder.Dispose();

    /// <summary>A photo in the liked folder (the source the window starts with), made a day later than the day before it so that the order by date is the order of the days.</summary>
    private string Photo(string name, int day, string? folder = null, int width = 240, int height = 160)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(30 + day * 20 % 200, 100, 180));
            graphics.FillEllipse(Brushes.White, width / 4, height / 4, width / 2, height / 2);
        }

        var path = Path.Combine(folder ?? _folder.Liked, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Jpeg);
        var date = new DateTime(2020, 5, day, 12, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(path, date);
        File.SetLastWriteTimeUtc(path, date);
        return path;
    }

    private BookDialog Create(IReadOnlyList<PhotoFile>? current = null, PhotoSortOrder order = PhotoSortOrder.OldestFirst) =>
        new(_folder.Path, current ?? [], _folder.Liked, new Settings().Extensions, order, dark: false);

    private static async Task Loaded(BookDialog dialog, int count) =>
        Assert.True(await Ui.Until(() => !dialog.IsLoading && dialog.Entries.Count == count, 30000), $"the {count} photos were listed (there are {dialog.Entries.Count})");

    private static string Names(BookDialog dialog) => string.Join(" ", dialog.Entries.Select(entry => Path.GetFileNameWithoutExtension(entry.Path)));

    private static void PickFormat(BookDialog dialog, BookFormat format)
    {
        var prefix = format switch { BookFormat.Pdf => "PDF", BookFormat.Docx => "DOCX", _ => "EPUB" };
        var index = dialog.FormatCombo.Items.Cast<object>().ToList().FindIndex(item => item.ToString()!.StartsWith(prefix));
        dialog.FormatCombo.SelectedIndex = index;
    }

    // ── The photos ──────────────────────────────────────────────────────────

    [Fact]
    public void TheLikedFolder_IsTheFirstSource_ItsPhotosAreInTheSortOrder_AndTheFirstIsPicked()
    {
        Photo("c.jpg", 3);
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Ui.Run(() => Create(), async dialog =>
        {
            var book = (BookDialog)dialog;
            await Loaded(book, 3);

            Assert.Equal("a b c", Names(book));
            Assert.Equal([0], book.View.SelectedIndices);
            Assert.Contains("a.jpg", book.PickedText);
            Assert.Contains("1", book.PickedText);
            Assert.True(await Ui.Until(() => book.PreviewImage is not null, 15000), "the photo that is picked has a preview");
            Assert.Equal("3 in the book, 0 left out", book.CountsText);
        });
    }

    [Fact]
    public void TheSources_AreTheLoadedPhotos_TheLikedFolder_AndEachGroup()
    {
        Photo("l1.jpg", 1);
        Photo("g1.jpg", 2, Path.Combine(_folder.Liked, "A - Group Family"));
        Photo("g2.jpg", 3, Path.Combine(_folder.Liked, "A - Group Family"));
        var loadedPath = Photo("x.jpg", 4, _folder.Path);
        PhotoFile[] loaded = [new(loadedPath, string.Empty) { Length = new FileInfo(loadedPath).Length, LastWriteTimeUtc = File.GetLastWriteTimeUtc(loadedPath), CreationTimeUtc = File.GetCreationTimeUtc(loadedPath) }];

        Ui.Run(() => Create(loaded), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);
            Assert.True(await Ui.Until(() => dialog.SourceCombo.Items.Count == 3 && dialog.SourceCombo.Items.Cast<object>().All(item => !item.ToString()!.Contains("...")), 30000), "the sources were counted");

            var names = dialog.SourceCombo.Items.Cast<object>().Select(item => item.ToString()).ToList();
            Assert.Equal(["Current (1)", "Liked (1)", "A - Group Family (2)"], names);
            Assert.Equal(0, dialog.SourceCombo.SelectedIndex); // the loaded photos are the source the window starts on
            Assert.Equal("x", Names(dialog));

            dialog.SourceCombo.SelectedIndex = 2;
            await Loaded(dialog, 2);
            Assert.Equal("g1 g2", Names(dialog));

            dialog.SourceCombo.SelectedIndex = 1;
            await Loaded(dialog, 1);
            Assert.Equal("l1", Names(dialog));

            dialog.SourceCombo.SelectedIndex = 0;
            await Loaded(dialog, 1);
            Assert.Equal("x", Names(dialog));
        });
    }

    [Fact]
    public void TheWindow_StartsOnTheLoadedPhotos_AndPutsTheBookInTheirFolder()
    {
        Photo("l1.jpg", 1); // a liked photo, which is not what the window starts on
        var loadedPath = Photo("x.jpg", 4, _folder.Path);
        PhotoFile[] loaded = [new(loadedPath, string.Empty) { Length = new FileInfo(loadedPath).Length, LastWriteTimeUtc = File.GetLastWriteTimeUtc(loadedPath), CreationTimeUtc = File.GetCreationTimeUtc(loadedPath) }];

        Ui.Run(() => Create(loaded), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);

            Assert.Equal(0, dialog.SourceCombo.SelectedIndex);
            Assert.Equal("x", Names(dialog));
            Assert.Equal(_folder.Path, dialog.OutputFolderBox.Text);
        });
    }

    [Fact]
    public void WithNoPhotosLoaded_TheWindowStartsOnTheLikedFolder()
    {
        Photo("l1.jpg", 1);

        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);

            Assert.Equal(1, dialog.SourceCombo.SelectedIndex);
            Assert.Equal("l1", Names(dialog));
            Assert.Equal(_folder.Liked, dialog.OutputFolderBox.Text);
        });
    }

    [Fact]
    public void AnEmptyFolder_SaysSo_AndThereIsNothingToMake()
    {
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Task.Delay(300);
            await Ui.Until(() => !dialog.IsLoading, 15000);

            Assert.Empty(dialog.Entries);
            Assert.False(dialog.MakeButton.Enabled);
            Assert.Contains("No photos", Ui.Controls<Label>(dialog).Select(label => label.Text).FirstOrDefault(text => text.StartsWith("No photos")) ?? "");
        });
    }

    // ── The order ───────────────────────────────────────────────────────────

    [Fact]
    public void ThePhotos_AreMovedByTheButtons_AndSortedAgain()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Photo("c.jpg", 3);
        Photo("d.jpg", 4);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 4);

            ToolStripButton Button(string text) => dialog.ViewTools.Items.OfType<ToolStripButton>().First(item => item.Text?.Contains(text) == true);

            dialog.View.Select(0);
            Button("Later").PerformClick();
            Assert.Equal("b a c d", Names(dialog));

            Button("To the end").PerformClick();
            Assert.Equal("b c d a", Names(dialog));

            Button("Reverse").PerformClick();
            Assert.Equal("a d c b", Names(dialog));

            Button("Undo").PerformClick();
            Assert.Equal("b c d a", Names(dialog));

            // Sort: by name, from the combo
            dialog.SortCombo.SelectedIndex = dialog.SortCombo.Items.Cast<object>().ToList().FindIndex(item => item.ToString() == "By name");
            dialog.SortButton.PerformClick();
            Assert.Equal("a b c d", Names(dialog));
        });
    }

    [Fact]
    public void APhotoThatIsLeftOut_IsCounted_AndTheButtonSaysPutBack()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            var leave = dialog.ViewTools.Items.OfType<ToolStripButton>().First(item => item.Text is "Leave out" or "Put back");

            dialog.View.Select(1);
            Assert.Equal("Leave out", leave.Text);
            leave.PerformClick();

            Assert.True(dialog.Entries[1].LeftOut);
            Assert.Equal("1 in the book, 1 left out", dialog.CountsText);
            Assert.Equal("Put back", leave.Text);

            leave.PerformClick();
            Assert.False(dialog.Entries[1].LeftOut);
            Assert.Equal("Leave out", leave.Text);
        });
    }

    [Fact]
    public void WithAllPhotosLeftOut_NothingCanBeMade()
    {
        Photo("a.jpg", 1);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);
            Assert.True(dialog.MakeButton.Enabled);

            dialog.View.Select(0);
            dialog.View.ToggleLeftOut();

            Assert.False(dialog.MakeButton.Enabled);
        });
    }

    // ── What is written ─────────────────────────────────────────────────────

    [Fact]
    public void WhatIsTyped_BelongsToThePhotoThatIsPicked_AndComesBackWithIt()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);

            dialog.AboveEditor.Box.Text = "Привіт, море";
            dialog.BelowEditor.Box.Text = "Below the first";
            Assert.Equal("Привіт, море", RtfParser.Parse(dialog.Entries[0].AboveRtf).PlainText);
            Assert.Equal("Below the first", RtfParser.Parse(dialog.Entries[0].BelowRtf).PlainText);
            Assert.Null(dialog.Entries[1].AboveRtf);

            dialog.View.Select(1);
            Assert.True(dialog.AboveEditor.IsEmpty);
            Assert.True(dialog.BelowEditor.IsEmpty);
            dialog.AboveEditor.Box.Text = "Second";

            dialog.View.Select(0);
            Assert.Equal("Привіт, море", dialog.AboveEditor.PlainText);
            Assert.Equal("Below the first", dialog.BelowEditor.PlainText);
            Assert.Equal("Second", RtfParser.Parse(dialog.Entries[1].AboveRtf).PlainText);

            // A text that is cleared is no text
            dialog.AboveEditor.Box.Clear();
            Assert.Null(dialog.Entries[0].AboveRtf);
        });
    }

    [Fact]
    public void ThePhotoThatMoves_TakesItsTextsWithIt()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            dialog.AboveEditor.Box.Text = "about a";

            dialog.View.MoveSelectedToEnd(); // a goes behind b

            Assert.Equal("b a", Names(dialog));
            Assert.Equal("about a", dialog.AboveEditor.PlainText); // still the picked one, at its new place
            Assert.Contains("2", dialog.PickedText);
            Assert.Equal("about a", RtfParser.Parse(dialog.Entries[1].AboveRtf).PlainText);
        });
    }

    [Fact]
    public void SeveralPhotosPicked_HaveNoTextToEdit()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            dialog.AboveEditor.Box.Text = "text";

            dialog.View.Select(0, 1);

            Assert.True(dialog.AboveEditor.Box.ReadOnly && dialog.BelowEditor.Box.ReadOnly);
            Assert.True(dialog.AboveEditor.IsEmpty);
            Assert.Contains("2", dialog.PickedText);
            Assert.Equal("text", RtfParser.Parse(dialog.Entries[0].AboveRtf).PlainText); // not lost

            dialog.View.Select(1, 0);
            dialog.View.Select(0);
            Assert.Equal("text", dialog.AboveEditor.PlainText);
        });
    }

    // ── What is kept with the folder ────────────────────────────────────────

    [Fact]
    public void TheOrderTheTextsAndTheSettings_AreKeptWithTheFolder_AndComeBackNextTime()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Photo("c.jpg", 3);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 3);

            dialog.View.Select(0);
            dialog.View.MoveSelectedToEnd();
            dialog.View.Select(0); // b
            dialog.View.ToggleLeftOut();
            dialog.AboveEditor.Box.Text = "Заголовок";
            dialog.TitleBox.Text = "Моя книга";
            dialog.AuthorBox.Text = "Юрій";
            PickFormat(dialog, BookFormat.Epub);
            dialog.Zoom.Value = 200;
        });

        Assert.True(File.Exists(Path.Combine(_folder.Liked, BookSettingsFile.FileName)), "the book is kept in the folder of the photos");

        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 3);

            Assert.Equal("b c a", Names(dialog));
            Assert.True(dialog.Entries[0].LeftOut);
            Assert.Equal("Заголовок", RtfParser.Parse(dialog.Entries[0].AboveRtf).PlainText);
            Assert.Equal("Моя книга", dialog.TitleBox.Text);
            Assert.Equal("Юрій", dialog.AuthorBox.Text);
            Assert.EndsWith(".epub", dialog.FileNameBox.Text);
            Assert.Equal(200, dialog.Zoom.Value);
            Assert.Equal(200, dialog.View.ThumbnailSize);
        });
    }

    [Fact]
    public void ANewPhotoGoesToTheEnd_APhotoThatIsGoneIsDropped_WhenTheFolderIsOpenedAgain()
    {
        var a = Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Photo("c.jpg", 3);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 3);
            dialog.View.Select(2);
            dialog.View.MoveSelectedToStart(); // c a b
            dialog.View.Select(1);
            dialog.AboveEditor.Box.Text = "about a";
        });

        File.Delete(Path.Combine(_folder.Liked, "b.jpg"));
        Photo("0new.jpg", 9);

        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 3);

            Assert.Equal("c a 0new", Names(dialog));
            Assert.Equal("about a", RtfParser.Parse(dialog.Entries[1].AboveRtf).PlainText);
        });
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void ThePickOfAnotherSource_KeepsTheBookThatWasShown()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        var other = Photo("o.jpg", 3, _folder.Path);
        PhotoFile[] loaded = [new(other, string.Empty) { CreationTimeUtc = File.GetCreationTimeUtc(other), LastWriteTimeUtc = File.GetLastWriteTimeUtc(other) }];

        Ui.Run(() => Create(loaded), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1); // the window starts on the loaded photos
            dialog.SourceCombo.SelectedIndex = 1; // the liked folder
            await Loaded(dialog, 2);
            dialog.View.Select(0);
            dialog.View.MoveSelectedToEnd();

            dialog.SourceCombo.SelectedIndex = 0; // the loaded photos
            await Loaded(dialog, 1);

            Assert.True(File.Exists(Path.Combine(_folder.Liked, BookSettingsFile.FileName)), "the book of the liked folder was kept before the other was shown");
            var kept = BookSettingsFile.Load(_folder.Liked)!.ToEntries();
            Assert.Equal(["b.jpg", "a.jpg"], kept.Select(entry => Path.GetFileName(entry.Path)));
        });
    }

    [Fact]
    public void ABookThatWasNotChanged_WritesNoFile()
    {
        Photo("a.jpg", 1);
        Ui.Run(() => Create(), async form =>
        {
            await Loaded((BookDialog)form, 1);
        });

        Assert.False(File.Exists(Path.Combine(_folder.Liked, BookSettingsFile.FileName)));
    }

    // ── The format ──────────────────────────────────────────────────────────

    [Fact]
    public void AnEpub_SwitchesOffThePaperFields_AndTheNameFollowsTheFormat()
    {
        Photo("a.jpg", 1);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);
            Assert.EndsWith(".pdf", dialog.FileNameBox.Text);
            Assert.All(dialog.PdfOnlyFields, field => Assert.True(field.Enabled));

            PickFormat(dialog, BookFormat.Epub);
            Assert.EndsWith(".epub", dialog.FileNameBox.Text);
            Assert.All(dialog.PdfOnlyFields, field => Assert.False(field.Enabled));

            // a name of your own keeps its name and changes its extension
            dialog.FileNameBox.Text = "holiday.epub";
            PickFormat(dialog, BookFormat.Pdf);
            Assert.Equal("holiday.pdf", dialog.FileNameBox.Text);
            Assert.All(dialog.PdfOnlyFields, field => Assert.True(field.Enabled));
        });
    }

    [Fact]
    public void TheFormats_AreAPdfAnEpubAndAWordDocument_AWordDocumentIsPagedLikeAPdf_AndTheNameFollowsIt()
    {
        Photo("a.jpg", 1);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);
            Assert.Equal([BookStrings.FormatPdf, BookStrings.FormatEpub, BookStrings.FormatDocx], dialog.FormatCombo.Items.Cast<object>().Select(item => item.ToString()).ToArray());

            PickFormat(dialog, BookFormat.Docx);
            Assert.EndsWith(".docx", dialog.FileNameBox.Text);
            Assert.All(dialog.PdfOnlyFields, field => Assert.True(field.Enabled)); // the paper, the margins, the font and the numbers are those of a Word document too

            dialog.FileNameBox.Text = "holiday.docx";
            PickFormat(dialog, BookFormat.Epub);
            Assert.Equal("holiday.epub", dialog.FileNameBox.Text);
            Assert.All(dialog.PdfOnlyFields, field => Assert.False(field.Enabled));

            PickFormat(dialog, BookFormat.Docx);
            Assert.Equal("holiday.docx", dialog.FileNameBox.Text);
            Assert.All(dialog.PdfOnlyFields, field => Assert.True(field.Enabled));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AWordDocument_IsMade_WithTheTextsOfThePhotosInTheirOrder_AndThePaperThatWasPicked()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2, width: 160, height: 240);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            dialog.AboveEditor.Box.Text = "about a";
            dialog.View.Select(1);
            dialog.BelowEditor.Box.Text = "about b";
            dialog.View.MoveSelectedToStart(); // b a
            dialog.TitleBox.Text = "Книга";
            PickFormat(dialog, BookFormat.Docx);

            dialog.MakeButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.MadeBookPath is not null && dialog.MakeButton.Enabled, 60000), "the book was made: " + dialog.StatusText);

            Assert.EndsWith(".docx", dialog.MadeBookPath);
            using var zip = ZipFile.OpenRead(dialog.MadeBookPath!);
            var document = System.Xml.Linq.XDocument.Load(zip.GetEntry("word/document.xml")!.Open());
            var w = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
            var texts = document.Descendants(w + "t").Select(element => element.Value).Where(text => text.StartsWith("about", StringComparison.Ordinal)).ToArray();
            Assert.Equal(["about b", "about a"], texts);
            Assert.Equal(3, document.Descendants(w + "sectPr").Count()); // the title, b and a
            Assert.Contains("Done", dialog.StatusText);
        });
    }

    [Fact]
    public void TheBook_IsSavedWhereThePhotosAre_UntilAnotherFolderIsTyped()
    {
        Photo("a.jpg", 1);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);

            Assert.Equal(_folder.Liked, dialog.OutputFolderBox.Text);
        });
    }

    // ── Making the book ─────────────────────────────────────────────────────

    [Fact]
    public void APdf_IsMade_OfTheListInItsOrder_WithoutTheLeftOutPhotos()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2, width: 160, height: 240);
        Photo("c.jpg", 3);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 3);
            dialog.View.Select(2);
            dialog.View.ToggleLeftOut(); // c is left out
            dialog.View.Select(0);
            dialog.View.MoveSelectedToEnd(); // b a c
            dialog.AboveEditor.Box.Text = "Привіт";
            dialog.TitleBox.Text = "Книга";
            dialog.FileNameBox.Text = "result.pdf";

            dialog.MakeButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.MadeBookPath is not null && dialog.MakeButton.Enabled, 60000), "the book was made: " + dialog.StatusText);

            var path = dialog.MadeBookPath!;
            Assert.Equal(Path.Combine(_folder.Liked, "result.pdf"), path);
            using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal(3, document.PageCount); // the title, b, a
            Assert.Equal("Книга", document.Info.Title);
            Assert.True(document.Pages[1].Width.Point < document.Pages[1].Height.Point, "b is a portrait photo and comes first");
            Assert.True(document.Pages[2].Width.Point > document.Pages[2].Height.Point);
            Assert.Contains("Done", dialog.StatusText);
            Assert.True(dialog.OpenButton.Visible);
            Assert.False(dialog.Progress.Visible);
        });
    }

    [Fact]
    public void AnEpub_IsMade_WithTheTextsOfThePhotosInTheirOrder()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            dialog.AboveEditor.Box.Text = "about a";
            dialog.View.Select(1);
            dialog.BelowEditor.Box.Text = "about b";
            dialog.View.MoveSelectedToStart(); // b a
            PickFormat(dialog, BookFormat.Epub);

            dialog.MakeButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.MadeBookPath is not null && dialog.MakeButton.Enabled, 60000), "the book was made: " + dialog.StatusText);

            Assert.EndsWith(".epub", dialog.MadeBookPath);
            using var zip = ZipFile.OpenRead(dialog.MadeBookPath!);
            string Page(string name) => new StreamReader(zip.GetEntry(name)!.Open()).ReadToEnd();
            Assert.Contains("about b", Page("OEBPS/photo0001.xhtml"));
            Assert.Contains("about a", Page("OEBPS/photo0002.xhtml"));
        });
    }

    [Fact]
    public void WhileTheBookIsMade_NothingCanBeChanged_AndAfterwardsItCan()
    {
        for (var i = 1; i <= 6; i++)
            Photo($"p{i}.jpg", i, width: 1600, height: 1200);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 6);

            dialog.MakeButton.PerformClick();
            Assert.False(dialog.MakeButton.Enabled);
            Assert.False(dialog.View.Enabled);
            Assert.False(dialog.TitleBox.Enabled);
            Assert.Equal("Cancel", dialog.CloseButton.Text);
            Assert.True(dialog.Progress.Visible);

            Assert.True(await Ui.Until(() => dialog.MadeBookPath is not null && dialog.MakeButton.Enabled, 60000), "the book was made");
            Assert.True(dialog.View.Enabled);
            Assert.True(dialog.TitleBox.Enabled);
            Assert.Equal("Close", dialog.CloseButton.Text);
        });
    }

    [Fact]
    public void Cancel_StopsTheWork_AndLeavesNoBook()
    {
        for (var i = 1; i <= 8; i++)
            Photo($"p{i}.jpg", i, width: 3000, height: 2000);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 8);

            dialog.MakeButton.PerformClick();
            await Task.Delay(30);
            dialog.CloseButton.PerformClick(); // Cancel, while it is made
            Assert.True(await Ui.Until(() => dialog.MakeButton.Enabled, 60000), "the work stopped");

            // It may have been done before the click got there; if it was stopped, there is no book
            if (dialog.MadeBookPath is null)
            {
                Assert.Contains("Cancelled", dialog.StatusText);
                Assert.Empty(Directory.GetFiles(_folder.Liked, "photobook_*"));
            }
        });
    }

    // ── The button of the main window ───────────────────────────────────────

    [Fact]
    public void TheMainWindow_HasTheMakeBookButton_AfterHelpAndASeparator_AndBeforeTheTraceGifStickerDrawAndManuscriptButtons()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var toolbar = Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));
            var names = toolbar.Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            var help = names.IndexOf("helpToolStripButton");
            var book = names.IndexOf("makeBookToolStripButton");
            Assert.True(help >= 0 && book == names.Count - 6, "the last button but the ones of Trace Bitmap, Video to GIF, the Telegram sticker, Draw and Scan manuscripts");
            Assert.Equal("makeBookToolStripSeparator", names[book - 1]);
            Assert.Equal(help, book - 2); // Help, a separator, Make a book (and then Trace Bitmap, Video to GIF, the Telegram sticker, Draw and Scan manuscripts, the last)
            var button = toolbar.Items["makeBookToolStripButton"]!;
            Assert.Equal(BookStrings.ButtonText, button.Text);
            Assert.Equal(BookStrings.ButtonTooltip, button.ToolTipText);
            Assert.NotNull(button.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void ThePressOfTheButton_OpensTheWindow()
    {
        Photo("a.jpg", 1);
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(500);
            var button = (ToolStripButton)Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items["makeBookToolStripButton"] is not null).Items["makeBookToolStripButton"]!;

            form.BeginInvoke(() => button.PerformClick());
            Assert.True(await Ui.Until(() => Ui.Find("BookDialog") is not null, 30000), "the window opened");

            var dialog = (BookDialog)Ui.Find("BookDialog")!;
            Assert.Equal(BookStrings.DialogTitle, dialog.Text);
            dialog.Close();
            Assert.True(await Ui.Until(() => Ui.Find("BookDialog") is null, 10000), "the window closed");
        }, seconds: 90);
    }

    // ── Turning ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheRotateButtons_TurnThePickedPhotos_InTheBook_AndTheirPictureFollows()
    {
        Photo("a.jpg", 1, width: 300, height: 150);
        Photo("b.jpg", 2, width: 300, height: 150);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            Assert.True(await Ui.Until(() => dialog.PreviewImage is not null, 15000), "the preview came");
            Assert.True(dialog.PreviewImage!.Width > dialog.PreviewImage.Height);
            Assert.NotNull(dialog.RotateRightButton.Image);
            Assert.NotNull(dialog.RotateLeftButton.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, dialog.RotateRightButton.DisplayStyle);

            dialog.RotateRightButton.PerformClick();

            Assert.Equal(90, dialog.Entries[0].Rotation);
            Assert.Equal(0, dialog.Entries[1].Rotation);
            Assert.True(await Ui.Until(() => dialog.PreviewImage is { } picture && picture.Height > picture.Width, 15000), "the picture of the photo is on its side");

            dialog.RotateLeftButton.PerformClick();
            dialog.RotateLeftButton.PerformClick();
            Assert.Equal(270, dialog.Entries[0].Rotation);
            Assert.True(await Ui.Until(() => dialog.PreviewImage is { } picture && picture.Height > picture.Width, 15000));

            dialog.View.HandleKey(Keys.Oemplus | Keys.Control);
            Assert.Equal(0, dialog.Entries[0].Rotation);
            Assert.True(await Ui.Until(() => dialog.PreviewImage is { } picture && picture.Width > picture.Height, 15000), "back as it was");
        });
    }

    [Fact]
    public void TheRotateButtons_AreOffWithNothingPicked_AndOnWithSeveralPicked()
    {
        Photo("a.jpg", 1);
        Photo("b.jpg", 2);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            Assert.True(dialog.RotateRightButton.Enabled && dialog.RotateLeftButton.Enabled);

            dialog.View.Select();
            Assert.False(dialog.RotateRightButton.Enabled || dialog.RotateLeftButton.Enabled);

            dialog.View.Select(0, 1);
            Assert.True(dialog.RotateRightButton.Enabled);
            dialog.RotateRightButton.PerformClick();
            Assert.Equal([90, 90], dialog.Entries.Select(entry => entry.Rotation));
        });
    }

    [Fact]
    public void Turning_DoesNotDisturbTheTextThatIsBeingWritten()
    {
        Photo("a.jpg", 1);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 1);
            dialog.AboveEditor.Box.Text = "some words";
            dialog.AboveEditor.Box.Select(5, 3); // "wor"

            dialog.RotateRightButton.PerformClick();

            Assert.Equal("some words", dialog.AboveEditor.PlainText);
            Assert.Equal((5, 3), (dialog.AboveEditor.Box.SelectionStart, dialog.AboveEditor.Box.SelectionLength)); // the cursor and the pick are where they were
            Assert.Contains("a.jpg", dialog.PickedText);
        });
    }

    [Fact]
    public void TheTurnOfAPhoto_IsKeptWithTheFolder_AndInTheBookThatIsMade()
    {
        var a = Photo("a.jpg", 1, width: 300, height: 150);
        Photo("b.jpg", 2, width: 300, height: 150);
        var before = File.ReadAllBytes(a);
        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            dialog.RotateRightButton.PerformClick(); // a
        });

        Assert.Contains("\"Rotation\": 90", File.ReadAllText(Path.Combine(_folder.Liked, BookSettingsFile.FileName)));
        Assert.Equal(before, File.ReadAllBytes(a)); // only the book is turned

        Ui.Run(() => Create(), async form =>
        {
            var dialog = (BookDialog)form;
            await Loaded(dialog, 2);
            Assert.Equal([90, 0], dialog.Entries.Select(entry => entry.Rotation));
            dialog.FileNameBox.Text = "turned.pdf";

            dialog.MakeButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.MadeBookPath is not null && dialog.MakeButton.Enabled, 60000), "the book was made: " + dialog.StatusText);

            using var document = PdfReader.Open(dialog.MadeBookPath!, PdfDocumentOpenMode.Import);
            Assert.True(document.Pages[0].Width.Point < document.Pages[0].Height.Point, "a, turned, is a portrait page");
            Assert.True(document.Pages[1].Width.Point > document.Pages[1].Height.Point);
        });
    }
}
