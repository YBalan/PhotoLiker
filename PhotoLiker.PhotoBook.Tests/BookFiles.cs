namespace PhotoLiker.PhotoBook.Tests;

/// <summary>Progress that is noted at once, on the thread that reports it (<see cref="Progress{T}"/> posts it to another thread, which a test cannot wait for).</summary>
internal sealed class Reported : IProgress<int>
{
    public List<int> Values { get; } = [];

    public void Report(int value) => Values.Add(value);
}

/// <summary>A folder in the temp folder for a test to write in, which goes away with it; and the pages a book is made of.</summary>
internal sealed class BookFiles : IDisposable
{
    public BookFiles()
    {
        Folder = Path.Combine(Path.GetTempPath(), "PhotoBookTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    public string Path_(string name) => Path.Combine(Folder, name);

    public void Dispose() => Directory.Delete(Folder, recursive: true);

    /// <summary>A page with a landscape photo (64 x 48) or a portrait one, and the descriptions as plain text (null: none).</summary>
    public BookPage Page(string name, string? above = null, string? below = null, bool portrait = false)
    {
        var file = Path_(name + ".jpg");
        File.WriteAllBytes(file, portrait ? TestImages.PortraitJpeg() : TestImages.LandscapeJpeg());
        return new BookPage(
            file,
            portrait ? TestImages.PortraitWidth : TestImages.LandscapeWidth,
            portrait ? TestImages.PortraitHeight : TestImages.LandscapeHeight,
            RichDocument.FromPlainText(above),
            RichDocument.FromPlainText(below),
            name);
    }

    /// <summary>A picture with a see-through background (a PNG of 40 by 30), which is a paragraph of the text of a page.</summary>
    public static RichParagraph Picture(double share = 0.5, TextAlign align = TextAlign.Center) =>
        RichParagraph.OfPicture(RichPicture.FromFile(TestImages.SeeThroughPng(40, 30), share)!, align);

    /// <summary>A text as paragraphs of plain text, each a line, with pictures among them: a string is a line of text, a paragraph is kept as it is.</summary>
    public static RichDocument Text(params object[] parts) =>
        new([.. parts.Select(part => part as RichParagraph ?? new RichParagraph(TextAlign.Left, [new RichRun((string)part, default)]))]);

    public BookPage Page(string name, RichDocument above, RichDocument below, bool portrait = false) =>
        Page(name, portrait: portrait) with { Above = above, Below = below };
}
