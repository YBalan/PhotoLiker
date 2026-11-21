namespace PhotoLiker.PhotoBook.Tests;

public sealed class BookSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PhotoBookSettings_" + Guid.NewGuid().ToString("N"));

    public BookSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string P(params string[] parts) => Path.Combine([_folder, .. parts]);

    [Fact]
    public void AFolderWithoutABook_HasNoSettings()
    {
        Assert.Null(BookSettingsFile.Load(_folder));
        Assert.Null(BookSettingsFile.Load(P("not-there")));
        Assert.Null(BookSettingsFile.Load(""));
    }

    [Fact]
    public void WhatIsSaved_IsReadBack_WithThePathsFull()
    {
        var settings = new BookSettings
        {
            Format = nameof(BookFormat.Epub),
            Title = "Summer 2026",
            Author = "Yurii",
            PageFormat = nameof(PageFormat.A5),
            Orientation = nameof(PageOrientation.Landscape),
            MarginMillimetres = 12.5,
            FontFamily = "Georgia",
            PageNumbers = false,
            Quality = nameof(PictureQuality.Print),
            OutputFolder = P("out"),
            OutputFileName = "book.epub",
            ThumbnailSize = 180,
        };
        BookEntry[] photos =
        [
            new(P("one.jpg")) { AboveRtf = @"{\rtf1 above\par}", BelowRtf = @"{\rtf1 below\par}" },
            new(P("sub", "two.jpg")) { LeftOut = true },
        ];

        Assert.True(BookSettingsFile.TrySave(_folder, settings, photos, out var error), error);
        var loaded = BookSettingsFile.Load(_folder)!;

        Assert.Equal(BookFormat.Epub, loaded.GetFormat());
        Assert.Equal("Summer 2026", loaded.Title);
        Assert.Equal("Yurii", loaded.Author);
        Assert.Equal(PageFormat.A5, loaded.GetPageFormat());
        Assert.Equal(PageOrientation.Landscape, loaded.GetOrientation());
        Assert.Equal(12.5, loaded.MarginMillimetres);
        Assert.Equal("Georgia", loaded.FontFamily);
        Assert.False(loaded.PageNumbers);
        Assert.Equal(PictureQuality.Print, loaded.GetQuality());
        Assert.Equal(P("out"), loaded.OutputFolder);
        Assert.Equal("book.epub", loaded.OutputFileName);
        Assert.Equal(180, loaded.ThumbnailSize);

        var entries = loaded.ToEntries();
        Assert.Equal([P("one.jpg"), P("sub", "two.jpg")], entries.Select(entry => entry.Path));
        Assert.Equal(@"{\rtf1 above\par}", entries[0].AboveRtf);
        Assert.Equal(@"{\rtf1 below\par}", entries[0].BelowRtf);
        Assert.False(entries[0].LeftOut);
        Assert.True(entries[1].LeftOut);
        Assert.Null(entries[1].AboveRtf);
    }

    [Fact]
    public void ThePathsInTheFile_AreRelativeToTheFolder_SoItCanBeMoved()
    {
        BookEntry[] photos = [new(P("sub", "two.jpg"))];
        Assert.True(BookSettingsFile.TrySave(_folder, new BookSettings { OutputFolder = P("out") }, photos, out _));

        var json = File.ReadAllText(BookSettingsFile.GetPath(_folder));

        Assert.DoesNotContain(_folder.Replace("\\", "\\\\"), json);
        Assert.Contains("sub", json);
        Assert.Contains("two.jpg", json);

        // Moved to another place: the paths follow it
        var moved = Path.Combine(Path.GetTempPath(), "PhotoBookMoved_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(moved);
        try
        {
            File.Copy(BookSettingsFile.GetPath(_folder), BookSettingsFile.GetPath(moved));
            var loaded = BookSettingsFile.Load(moved)!;

            Assert.Equal(Path.Combine(moved, "sub", "two.jpg"), loaded.ToEntries().Single().Path);
            Assert.Equal(Path.Combine(moved, "out"), loaded.OutputFolder);
        }
        finally
        {
            Directory.Delete(moved, recursive: true);
        }
    }

    [Fact]
    public void EmptyTexts_AreNotWritten()
    {
        BookEntry[] photos = [new(P("a.jpg")) { AboveRtf = "  ", BelowRtf = null }];
        Assert.True(BookSettingsFile.TrySave(_folder, new BookSettings(), photos, out _));

        var json = File.ReadAllText(BookSettingsFile.GetPath(_folder));

        Assert.DoesNotContain("Above", json);
        Assert.DoesNotContain("Below", json);
        Assert.DoesNotContain("LeftOut", json);
    }

    [Fact]
    public void AFolderThatIsGone_IsNotBroughtBack()
    {
        var gone = P("gone");

        Assert.False(BookSettingsFile.TrySave(gone, new BookSettings(), [], out var error));
        Assert.NotNull(error);
        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void ASaveThatSucceeds_LeavesNoTemporaryFile_AndWritesOverTheOldOne()
    {
        Assert.True(BookSettingsFile.TrySave(_folder, new BookSettings { Title = "one" }, [], out _));
        Assert.True(BookSettingsFile.TrySave(_folder, new BookSettings { Title = "two" }, [], out _));

        Assert.Equal("two", BookSettingsFile.Load(_folder)!.Title);
        Assert.Equal([BookSettingsFile.FileName], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Title\": 5}")]
    [InlineData("{\"Photos\": \"nope\"}")]
    [InlineData("")]
    public void AFileThatCannotBeRead_IsNoSettings(string content)
    {
        File.WriteAllText(BookSettingsFile.GetPath(_folder), content);

        Assert.Null(BookSettingsFile.Load(_folder));
    }

    [Fact]
    public void ValuesThisVersionDoesNotKnow_AreNotThere()
    {
        File.WriteAllText(BookSettingsFile.GetPath(_folder), "{\"Format\":\"Mobi\",\"PageFormat\":\"A0\",\"Quality\":\"7\",\"Version\":99,\"Later\":true,\"Photos\":[{\"File\":\"\"},{\"File\":\"x.jpg\",\"Above\":\"  \"}]}");

        var loaded = BookSettingsFile.Load(_folder)!;

        Assert.Null(loaded.GetFormat());
        Assert.Null(loaded.GetPageFormat());
        Assert.Null(loaded.GetQuality());
        Assert.Null(loaded.GetOrientation());
        var entry = Assert.Single(loaded.ToEntries()); // the photo with no file is not one
        Assert.Null(entry.AboveRtf);
    }

    [Fact]
    public void TheTurnOfAPhoto_IsKept_AndWrittenOnlyWhenThereIsOne()
    {
        BookEntry[] photos = [new(P("a.jpg")) { Rotation = 90 }, new(P("b.jpg")), new(P("c.jpg")) { Rotation = 270 }];
        Assert.True(BookSettingsFile.TrySave(_folder, new BookSettings(), photos, out _));

        var json = File.ReadAllText(BookSettingsFile.GetPath(_folder));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(json, "\"Rotation\"").Count); // b is not turned: nothing is written for it

        var entries = BookSettingsFile.Load(_folder)!.ToEntries();
        Assert.Equal([90, 0, 270], entries.Select(entry => entry.Rotation));
    }

    [Fact]
    public void ATurnThatIsNotAQuarter_IsBroughtIn_WhenTheFileIsRead()
    {
        File.WriteAllText(BookSettingsFile.GetPath(_folder), "{\"Photos\":[{\"File\":\"a.jpg\",\"Rotation\":-90},{\"File\":\"b.jpg\",\"Rotation\":45},{\"File\":\"c.jpg\"}]}");

        var entries = BookSettingsFile.Load(_folder)!.ToEntries();

        Assert.Equal([270, 0, 0], entries.Select(entry => entry.Rotation));
    }
}
