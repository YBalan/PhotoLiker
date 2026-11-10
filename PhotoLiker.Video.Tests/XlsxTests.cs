using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

public class XlsxWriterTests
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static byte[] Make(params XlsxSheet[] sheets)
    {
        using var stream = new MemoryStream();
        XlsxWriter.Write(stream, sheets);
        return stream.ToArray();
    }

    private static XDocument Part(byte[] file, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        using var stream = (zip.GetEntry(name) ?? throw new InvalidOperationException("no part " + name)).Open();
        return XDocument.Load(stream);
    }

    private static IEnumerable<XElement> Cells(XDocument sheet) => sheet.Descendants(Main + "c");

    private static string TextOf(XElement cell) => string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));

    private static XlsxSheet Sheet(string name = "Videos", params object?[][] rows) =>
        new(name, [new XlsxColumn("Video", 40), new XlsxColumn("Speech", 80, XlsxFormat.Wrapped)], rows);

    [Fact]
    public void TheFile_IsAZipWithTheParts_ExcelNeeds()
    {
        var file = Make(Sheet(), Sheet("Speech"));

        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml"], names);
        var types = Part(file, "[Content_Types].xml").Descendants().Where(e => e.Name.LocalName == "Override").Select(e => (string?)e.Attribute("PartName")).ToArray();
        Assert.Contains("/xl/worksheets/sheet2.xml", types);
        var book = Part(file, "xl/workbook.xml");
        Assert.Equal(["Videos", "Speech"], book.Descendants(Main + "sheet").Select(s => (string?)s.Attribute("name")));
        var relations = Part(file, "xl/_rels/workbook.xml.rels").Descendants().Where(e => e.Name.LocalName == "Relationship").Select(e => (string)e.Attribute("Target")!).ToArray();
        Assert.Equal(["worksheets/sheet1.xml", "worksheets/sheet2.xml", "styles.xml"], relations);
    }

    [Fact]
    public void ATitleRow_AndTheRows_AreWrittenCellByCell_WithTheirReferences()
    {
        var file = Make(Sheet("Videos", ["a.mp4", "один"], ["sub\\b.mp4", null]));

        var cells = Cells(Part(file, "xl/worksheets/sheet1.xml")).ToList();

        Assert.Equal(["A1", "B1", "A2", "B2", "A3"], cells.Select(c => (string)c.Attribute("r")!));
        Assert.Equal(["Video", "Speech", "a.mp4", "один", "sub\\b.mp4"], cells.Select(TextOf));   // a null is no cell at all
        Assert.Equal("1", (string?)cells[0].Attribute("s"));                                          // the title row has its own look
        Assert.Equal("2", (string?)cells[3].Attribute("s"));                                          // a wrapped column
        Assert.Equal("A1:B3", (string?)Part(file, "xl/worksheets/sheet1.xml").Descendants(Main + "autoFilter").Single().Attribute("ref"));
    }

    [Fact]
    public void TheTitleRowStaysInView()
    {
        var pane = Part(Make(Sheet()), "xl/worksheets/sheet1.xml").Descendants(Main + "pane").Single();

        Assert.Equal("1", (string?)pane.Attribute("ySplit"));
        Assert.Equal("frozen", (string?)pane.Attribute("state"));
    }

    [Fact]
    public void TextThatXmlMeansSomethingBy_IsKeptAsText_AndWhatXmlCannotHold_IsLeftOut()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var nasty = "a < b & c > \"d\" 'e'\nline two\ttab" + emoji + "\u0001\u0008\uD800end";

        var file = Make(Sheet("Videos", ["x", nasty]));
        var cell = Cells(Part(file, "xl/worksheets/sheet1.xml")).Single(c => (string?)c.Attribute("r") == "B2");

        Assert.Equal("a < b & c > \"d\" 'e'\nline two\ttab" + emoji + "end", TextOf(cell));
    }

    [Fact]
    public void LeadingAndTrailingSpaces_Stay()
    {
        var cell = Cells(Part(Make(Sheet("Videos", ["x", "  two  "])), "xl/worksheets/sheet1.xml")).Single(c => (string?)c.Attribute("r") == "B2");

        Assert.Equal("  two  ", TextOf(cell));
        Assert.Equal("preserve", (string?)cell.Descendants(Main + "t").Single().Attribute(XNamespace.Xml + "space"));
    }

    [Fact]
    public void TextLongerThanACellCanHold_IsCut_WithAnEllipsis()
    {
        var cut = XlsxWriter.CellText(new string('я', 40_000));

        Assert.Equal(XlsxWriter.MaxCellCharacters + 1, cut.Length);
        Assert.EndsWith("я…", cut);
    }

    [Fact]
    public void ACutNeverSplitsAPairOfSurrogates()
    {
        var text = new string('a', XlsxWriter.MaxCellCharacters - 1) + char.ConvertFromUtf32(0x1F600) + "tail";

        var cut = XlsxWriter.CellText(text);

        Assert.EndsWith("…", cut);
        Assert.DoesNotContain(cut, c => char.IsSurrogate(c));
        Assert.Equal(XlsxWriter.MaxCellCharacters, cut.Length);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void AColumn_IsNamedTheWayExcelDoes(int index, string name) => Assert.Equal(name, XlsxWriter.ColumnName(index));

    [Theory]
    [InlineData("Videos", "Videos")]
    [InlineData("a/b\\c:d*e?f[g]h", "a-b-c-d-e-f-g-h")]
    [InlineData("", "Sheet")]
    [InlineData("  ", "Sheet")]
    [InlineData("'quoted'", "quoted")]
    [InlineData("0123456789012345678901234567890123456789", "0123456789012345678901234567890")]
    public void ASheetName_IsOneExcelAccepts(string name, string expected)
    {
        var made = XlsxWriter.SheetName(name);

        Assert.Equal(expected, made);
        Assert.True(made.Length <= 31);
    }

    [Fact]
    public void TwoSheetsWithTheSameName_GetDifferentOnes()
    {
        var file = Make(Sheet("Speech"), Sheet("speech"), Sheet("SPEECH"));

        var names = Part(file, "xl/workbook.xml").Descendants(Main + "sheet").Select(s => (string)s.Attribute("name")!).ToArray();

        Assert.Equal(["Speech", "speech (2)", "SPEECH (3)"], names);
    }

    [Fact]
    public void NumbersDatesAndLengths_AreNumbersWithTheirOwnLook_WhateverTheCultureIs()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("uk-UA"); // decimal comma
        try
        {
            var moment = new DateTime(2012, 3, 4, 12, 0, 0);
            var sheet = new XlsxSheet("S",
                [new XlsxColumn("n", 5), new XlsxColumn("when", 5, XlsxFormat.DateTime), new XlsxColumn("length", 5, XlsxFormat.Duration), new XlsxColumn("big", 5)],
                [[0.5, moment, TimeSpan.FromHours(6), 123456789012L]]);

            var cells = Cells(Part(Make(sheet), "xl/worksheets/sheet1.xml")).Skip(4).ToList();

            Assert.Equal(["0.5", moment.ToOADate().ToString("R", CultureInfo.InvariantCulture), "0.25", "123456789012"], cells.Select(c => (string?)c.Element(Main + "v")));
            Assert.Equal(["0", "3", "4", "0"], cells.Select(c => (string?)c.Attribute("s")));
            Assert.All(cells, c => Assert.Null(c.Attribute("t")));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void TheStyles_HoldEveryFormatTheCellsUse()
    {
        var styles = Part(Make(Sheet()), "xl/styles.xml");

        var formats = styles.Descendants(Main + "cellXfs").Single();
        Assert.Equal("5", (string?)formats.Attribute("count"));
        Assert.Equal(5, formats.Elements(Main + "xf").Count());
        Assert.Equal(["164", "165"], styles.Descendants(Main + "numFmt").Select(n => (string?)n.Attribute("numFmtId")));
        Assert.Equal("wrapText", formats.Elements(Main + "xf").ElementAt(2).Element(Main + "alignment")!.Attributes().Last().Name.LocalName);
    }

    [Fact]
    public void AWorkbookWithoutASheet_IsRefused() => Assert.Throws<ArgumentException>(() => Make());

    [Fact]
    public void AFileThatIsThere_IsReplaced_WithNoLeftoverBesideIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PLXlsx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "out.xlsx");
            File.WriteAllText(path, "old");

            XlsxWriter.Write(path, [Sheet("Videos", ["a.mp4", "x"])]);

            Assert.Equal(["out.xlsx"], Directory.GetFiles(folder).Select(Path.GetFileName));
            using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AFileThatIsOpenElsewhere_Throws_AndIsNotHalfWritten()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PLXlsx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "out.xlsx");
            File.WriteAllText(path, "old");
            Exception? problem;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // what Excel does to a file it has open
                problem = Record.Exception(() => XlsxWriter.Write(path, [Sheet()]));

            Assert.True(problem is IOException or UnauthorizedAccessException, problem?.ToString()); // Windows says "access denied" for a file that is in use

            Assert.Equal("old", File.ReadAllText(path));
            Assert.Equal(["out.xlsx"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class SpeechWorkbookTests
{
    private static readonly SpeechWorkbookLabels Labels = new(
        "Videos", "Speech", "Video", "Length", "Recorded", "Status", "Language", "Moments", "Segments", "Moment times", "Speech", "Start", "End", "Text",
        status => status.ToString().ToLowerInvariant());

    private static VideoSpeechRow Row(string path, SpeechStatus status, string language, double seconds, DateTime? recorded, SpeechSegment[] segments, long[] moments)
    {
        var info = new VideoInfo(seconds, 320, 240, 0, 25, "h264", "aac", 1000, null, null, null, null);
        var record = new VideoRecord(path.Length, path, 100, 1, info, recorded is null ? PhotoDate.NotRead : new PhotoDate(recorded, DateSource.Exif, PhotoDates.Version), null);
        return new VideoSpeechRow(record, @"C:\v\" + path, status, language, null, segments, moments);
    }

    [Fact]
    public void EveryVideo_IsARowOfTheFirstSheet_InOrderOfPath_ThoseWithoutSpeechToo()
    {
        var said = new[] { new SpeechSegment(0, 2000, "Вот лошадка"), new SpeechSegment(61_000, 63_000, "Ещё раз") };
        var rows = new[]
        {
            Row("sub/b.mp4", SpeechStatus.Heard, "ru", 90, new DateTime(2012, 3, 4, 5, 6, 0), said, [0, 3000, 61_000]),
            Row("A.mp4", SpeechStatus.NotRead, "", 5, null, [], []),
            Row("sub/c.mp4", SpeechStatus.NoSound, "", 12.5, null, [], []),
        };

        var sheets = SpeechWorkbook.Build(rows, Labels);

        var videos = sheets[0];
        Assert.Equal("Videos", videos.Name);
        Assert.Equal(["Video", "Length", "Recorded", "Status", "Language", "Moments", "Segments", "Moment times", "Speech"], videos.Columns.Select(c => c.Header));
        Assert.Equal(["A.mp4", @"sub\b.mp4", @"sub\c.mp4"], videos.Rows.Select(r => r[0]));  // by path, with the slashes Windows uses
        var heard = videos.Rows[1];
        Assert.Equal(TimeSpan.FromSeconds(90), heard[1]);
        Assert.Equal(new DateTime(2012, 3, 4, 5, 6, 0), heard[2]);
        Assert.Equal("heard", heard[3]);
        Assert.Equal("Russian (ru)", heard[4]);
        Assert.Equal(3, heard[5]);
        Assert.Equal(2, heard[6]);
        Assert.Equal("0:00, 0:03, 1:01", heard[7]);
        Assert.Equal("Вот лошадка Ещё раз", heard[8]);
        var unread = videos.Rows[0];
        Assert.Null(unread[2]);                                  // no date is an empty cell, not a made-up one
        Assert.Equal("notread", unread[3]);
        Assert.Equal(string.Empty, unread[4]);
        Assert.Equal(0, unread[5]);
        Assert.Equal(string.Empty, unread[8]);
    }

    [Fact]
    public void EveryStretchOfSpeech_IsARowOfTheSecondSheet_WithWhereItIs()
    {
        var rows = new[]
        {
            Row("b.mp4", SpeechStatus.Heard, "ru", 90, null, [new SpeechSegment(0, 2000, " Вот лошадка "), new SpeechSegment(61_500, 63_000, "Ещё раз")], []),
            Row("a.mp4", SpeechStatus.Heard, "en", 9, null, [new SpeechSegment(1000, 4000, "A horse")], []),
            Row("c.mp4", SpeechStatus.NothingHeard, "ru", 9, null, [], []),
        };

        var speech = SpeechWorkbook.Build(rows, Labels)[1];

        Assert.Equal("Speech", speech.Name);
        Assert.Equal(["Video", "Start", "End", "Text"], speech.Columns.Select(c => c.Header));
        Assert.Equal(3, speech.Rows.Count);
        Assert.Equal(["a.mp4", "b.mp4", "b.mp4"], speech.Rows.Select(r => r[0]));
        Assert.Equal(TimeSpan.FromSeconds(1), speech.Rows[0][1]);
        Assert.Equal(TimeSpan.FromSeconds(4), speech.Rows[0][2]);
        Assert.Equal(TimeSpan.FromMilliseconds(61_500), speech.Rows[2][1]);
        Assert.Equal("Вот лошадка", speech.Rows[1][3]);          // the spaces around it are not part of it
    }

    [Fact]
    public void ACatalogWithoutVideos_GivesTwoSheetsWithJustTheirTitles_AndTheyWrite()
    {
        var sheets = SpeechWorkbook.Build([], Labels);

        Assert.Equal(2, sheets.Count);
        Assert.All(sheets, sheet => Assert.Empty(sheet.Rows));
        using var stream = new MemoryStream();
        XlsxWriter.Write(stream, sheets);
        Assert.True(stream.Length > 500);
    }

    [Fact]
    public void WhatIsExported_ReadsBackThroughTheFile()
    {
        var rows = new[] { Row("sub/b.mp4", SpeechStatus.Heard, "uk", 90, new DateTime(2012, 3, 4, 5, 6, 0), [new SpeechSegment(0, 2000, "Дивись & <слухай>")], [0, 3000]) };

        using var stream = new MemoryStream();
        XlsxWriter.Write(stream, SpeechWorkbook.Build(rows, Labels));
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        var first = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var main = (XNamespace)"http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var texts = first.Descendants(main + "c").Where(c => (string?)c.Attribute("t") == "inlineStr").Select(c => string.Concat(c.Descendants(main + "t").Select(t => t.Value))).ToArray();

        Assert.Contains(@"sub\b.mp4", texts);
        Assert.Contains("Ukrainian (uk)", texts);
        Assert.Contains("0:00, 0:03", texts);
        Assert.Contains("Дивись & <слухай>", texts);
    }
}
