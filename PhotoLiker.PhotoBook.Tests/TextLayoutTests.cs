namespace PhotoLiker.PhotoBook.Tests;

public class TextLayoutTests
{
    /// <summary>Every character is 10 points wide, 12 for bold ones (so that styles can be told apart in the widths), whatever the size.</summary>
    private sealed class Fixed : ITextMeasurer
    {
        public double Width(string text, RichStyle style) => text.Length * (style.Bold ? 12 : 10);
    }

    private static RichDocument Doc(string text, TextAlign align = TextAlign.Left, RichStyle style = default) =>
        new([new RichParagraph(align, [new RichRun(text, style)])]);

    private static List<TextLine> Set(RichDocument document, double width) => TextLayout.Layout(document, width, new Fixed());

    [Fact]
    public void AShortText_IsOneLine()
    {
        var lines = Set(Doc("hello world"), 500);

        Assert.Equal("hello world", Assert.Single(lines).Text);
    }

    [Fact]
    public void TheWords_AreBrokenWhereALineIsFull()
    {
        // 10 points a character: 100 points are ten characters
        var lines = Set(Doc("aaa bbb ccc ddd"), 100);

        Assert.Equal(["aaa bbb", "ccc ddd"], lines.Select(line => line.Text));
    }

    [Fact]
    public void AWordThatFitsExactly_StaysOnTheLine()
    {
        var lines = Set(Doc("aaaa bbbbb"), 100);

        Assert.Equal(["aaaa bbbbb"], lines.Select(line => line.Text));
    }

    [Fact]
    public void ALineDoesNotStartWithASpace_NorEndWithOne()
    {
        var lines = Set(Doc("aaaaaaaaa   bbb"), 100);

        Assert.Equal(["aaaaaaaaa", "bbb"], lines.Select(line => line.Text));
    }

    [Fact]
    public void AWordLongerThanTheColumn_IsCutWhereTheLineIsFull()
    {
        var lines = Set(Doc("abcdefghijklmnopqrstuvwxyz"), 100);

        Assert.Equal(["abcdefghij", "klmnopqrst", "uvwxyz"], lines.Select(line => line.Text));
    }

    [Fact]
    public void ACutWord_AfterOtherWords_StartsOnALineOfItsOwn()
    {
        var lines = Set(Doc("hi abcdefghijklmno"), 100);

        Assert.Equal("hi", lines[0].Text);
        Assert.Equal(string.Concat("abcdefghij", "klmno"), string.Concat(lines.Skip(1).Select(line => line.Text)));
    }

    [Fact]
    public void AColumnNarrowerThanACharacter_StillEnds()
    {
        var lines = Set(Doc("abc"), 5);

        Assert.Equal(["a", "b", "c"], lines.Select(line => line.Text));
    }

    [Fact]
    public void AWordInTwoStyles_IsNeverSplitBetweenLines()
    {
        // "wor" plain and "ld" bold are one word: 30 + 24 points, which does not fit the line after "aaaaa " (60)
        var document = new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("aaaaa wor", default), new RichRun("ld", new RichStyle(Bold: true))])]);

        var lines = Set(document, 100);

        Assert.Equal(["aaaaa", "world"], lines.Select(line => line.Text));
    }

    [Fact]
    public void ALineBreak_EndsTheLine()
    {
        var lines = Set(Doc("one\ntwo"), 500);

        Assert.Equal(["one", "two"], lines.Select(line => line.Text));
    }

    [Fact]
    public void AnEmptyParagraph_IsABlankLine_OfTheDefaultSize()
    {
        var document = new RichDocument([
            new RichParagraph(TextAlign.Left, [new RichRun("a", default)]),
            new RichParagraph(TextAlign.Left, []),
            new RichParagraph(TextAlign.Left, [new RichRun("b", default)])]);

        var lines = Set(document, 500);

        Assert.Equal(3, lines.Count);
        Assert.Empty(lines[1].Pieces);
        Assert.Equal(RichStyle.DefaultSize * TextLayout.LineSpacing, lines[1].Height, 6);
    }

    [Fact]
    public void TheBlankLinesAtTheEnd_TakeNoRoom()
    {
        var document = new RichDocument([
            new RichParagraph(TextAlign.Left, [new RichRun("a", default)]),
            new RichParagraph(TextAlign.Left, []),
            new RichParagraph(TextAlign.Left, [])]);

        Assert.Single(Set(document, 500));
    }

    [Fact]
    public void NoText_IsNoLines()
    {
        Assert.Empty(Set(RichDocument.Empty, 500));
        Assert.Equal(0, TextLayout.Height(Set(RichDocument.Empty, 500)));
    }

    [Fact]
    public void ALine_IsAsTallAsItsBiggestText()
    {
        var document = new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("small ", new RichStyle(Size: 10)), new RichRun("BIG", new RichStyle(Size: 30))])]);

        var line = Assert.Single(Set(document, 500));

        Assert.Equal(30 * TextLayout.LineSpacing, line.Height, 6);
        Assert.Equal(30 * TextLayout.BaselineFactor, line.Baseline, 6);
    }

    [Fact]
    public void TheHeightOfLines_IsTheirSum()
    {
        var lines = Set(Doc("aaa bbb ccc ddd", style: new RichStyle(Size: 20)), 100);

        Assert.Equal(2 * 20 * TextLayout.LineSpacing, TextLayout.Height(lines), 6);
    }

    [Fact]
    public void Left_StartsAtZero()
    {
        var line = Assert.Single(Set(Doc("abc"), 100));

        Assert.Equal(0, line.Pieces[0].X);
    }

    [Fact]
    public void Centre_PutsTheLineInTheMiddle()
    {
        var line = Assert.Single(Set(Doc("abc", TextAlign.Center), 100));

        Assert.Equal(35, line.Pieces[0].X, 6); // (100 - 30) / 2
    }

    [Fact]
    public void Right_PutsTheLineAtTheRightSide()
    {
        var line = Assert.Single(Set(Doc("abc", TextAlign.Right), 100));

        Assert.Equal(70, line.Pieces[0].X, 6);
    }

    [Fact]
    public void Justify_SpreadsTheSpaces_ExceptOnTheLastLineOfTheParagraph()
    {
        var lines = Set(Doc("aa bb cc dd ee", TextAlign.Justify), 100);

        // The first line holds "aa bb cc" (80) and is spread to 100: two spaces get 10 more each
        Assert.Equal("aa bb cc", lines[0].Text.Trim());
        var first = lines[0].Pieces;
        Assert.Equal(0, first[0].X, 6);
        var last = first[^1];
        Assert.Equal(100, last.X + last.Width, 6);

        // The last line is not spread
        Assert.Equal(0, lines[^1].Pieces[0].X, 6);
        Assert.True(lines[^1].Pieces[^1].X + lines[^1].Pieces[^1].Width < 100);
    }

    [Fact]
    public void Justify_DoesNotSpreadALineThatAHardBreakEnds()
    {
        var lines = Set(Doc("aa bb\ncc dd", TextAlign.Justify), 100);

        Assert.Equal(50, lines[0].Pieces[^1].X + lines[0].Pieces[^1].Width, 6);
    }

    [Fact]
    public void WhatIsInOneStyle_IsOneText_WithItsSpaces()
    {
        var line = Assert.Single(Set(Doc("one two three"), 500));

        var piece = Assert.Single(line.Pieces);
        Assert.Equal("one two three", piece.Text);
        Assert.Equal(130, piece.Width, 6);
    }

    [Fact]
    public void AChangeOfStyle_StartsANewText_WhereTheOldOneEnds()
    {
        var document = new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("plain ", default), new RichRun("bold", new RichStyle(Bold: true))])]);

        var line = Assert.Single(Set(document, 500));

        Assert.Equal(2, line.Pieces.Count);
        Assert.Equal("plain ", line.Pieces[0].Text);
        Assert.Equal(60, line.Pieces[1].X, 6);
        Assert.True(line.Pieces[1].Style.Bold);
        Assert.Equal(48, line.Pieces[1].Width, 6);
    }

    // The reference column is 432 points wide: in a column of that width the indents and the tab stops are in the points they are written in
    private const double Column = RichDocument.ReferenceWidth;

    private static RichDocument Para(string text, double left = 0, double first = 0, double[]? stops = null, TextAlign align = TextAlign.Left) =>
        new([new RichParagraph(align, [new RichRun(text, default)]) { LeftIndent = left, FirstLineIndent = first, TabStops = stops ?? [] }]);

    [Fact]
    public void ATab_GoesToTheNextHalfInch_WhenTheParagraphHasNoStops()
    {
        var line = Assert.Single(Set(Para("a\tb"), Column));

        Assert.Equal(["a", "b"], line.Pieces.Select(piece => piece.Text)); // the gap is not drawn, and the two are not one text
        Assert.Equal(0, line.Pieces[0].X, 6);
        Assert.Equal(TextLayout.DefaultTabWidth, line.Pieces[1].X, 6); // "a" is 10 wide: the stop is at 36
    }

    [Fact]
    public void ATabThatStartsOnAHalfInch_GoesToTheNextOne_NotToWhereItIs()
    {
        // "abc" in bold is 36 wide: it ends on a half inch, and the tab goes on to the next, it is not a gap of nothing
        var document = new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("abc", new RichStyle(Bold: true)), new RichRun("\tx", default)])]);

        var line = Assert.Single(Set(document, Column));

        Assert.Equal(72, line.Pieces[^1].X, 6);
    }

    [Fact]
    public void TheIndentOfTheOtherLines_IsTheSameShareOfAWiderColumn()
    {
        var lines = Set(Para(string.Join(" ", Enumerable.Repeat("abcd", 40)), left: 50), 2 * Column);

        Assert.True(lines.Count > 1);
        Assert.All(lines, line => Assert.Equal(100, line.Pieces[0].X, 6));
    }

    [Fact]
    public void ATabPastTheLastStopOfTheParagraph_GoesOnByHalfInches()
    {
        // "cat" is 30 wide, the stop is at 20: past it, the next stop is the half inch after 30, which is 36; "a" then ends at 46, the next is 72
        var line = Assert.Single(Set(Para("cat\ta\tb", stops: [20]), Column));

        Assert.Equal([0, 36, 72], line.Pieces.Select(piece => piece.X));
    }

    [Fact]
    public void ATab_GoesToTheStopsTheParagraphHas()
    {
        var line = Assert.Single(Set(Para("a\tb\tc", stops: [100, 250]), Column));

        Assert.Equal([0, 100, 250], line.Pieces.Select(piece => piece.X));
    }

    [Fact]
    public void TheTabStops_AreTheSameShareOfAWiderOrNarrowerColumn()
    {
        var wide = Assert.Single(Set(Para("a\tb", stops: [100]), 2 * Column));
        var narrow = Assert.Single(Set(Para("a\tb", stops: [100]), Column / 2));

        Assert.Equal(200, wide.Pieces[1].X, 6);
        Assert.Equal(50, narrow.Pieces[1].X, 6);
    }

    [Fact]
    public void ATabAtTheStartOfALine_IsAnIndent()
    {
        var line = Assert.Single(Set(Para("\ttext", stops: [60]), Column));

        Assert.Equal("text", Assert.Single(line.Pieces).Text);
        Assert.Equal(60, line.Pieces[0].X, 6);
    }

    [Fact]
    public void ATabThatWouldGoPastTheEndOfTheLine_IsASpace()
    {
        var line = Assert.Single(Set(Para("ab\tcd", stops: [500]), Column));

        Assert.Equal(30, line.Pieces[1].X, 6); // "ab" is 20 wide and a space is 10
    }

    [Fact]
    public void AJustifiedLineWithATab_KeepsItsColumns()
    {
        var long_ = string.Join(" ", Enumerable.Repeat("word", 30));
        var document = Para("x\t" + long_, stops: [50], align: TextAlign.Justify);

        var lines = Set(document, Column);

        Assert.True(lines.Count > 1);
        Assert.Equal(50, lines[0].Pieces[1].X, 6); // not stretched: the first line has the tab
        Assert.Equal(2, lines[0].Pieces.Count); // "x", and the words after the tab as they are: a line that is spread has a piece for each word
    }

    [Fact]
    public void TheIndent_MovesEveryLine_AndTheFirstLineHasItsOwn()
    {
        // 300 points are 30 characters; with 50 of indent 25 characters fit a line, and the first line is 20 further in
        var words = string.Join(" ", Enumerable.Repeat("abcd", 30));
        var lines = Set(Para(words, left: 50, first: 20), Column);

        Assert.True(lines.Count > 2);
        Assert.Equal(70, lines[0].Pieces[0].X, 6);
        Assert.All(lines.Skip(1), line => Assert.Equal(50, line.Pieces[0].X, 6));
        Assert.All(lines.Skip(1), line => Assert.True(line.Pieces[^1].X + line.Pieces[^1].Width <= Column + 0.01));
        Assert.True(lines[0].Pieces.Sum(piece => piece.Width) <= Column - 70 + 0.01, "the first line has room only for what is left after its start");
    }

    [Fact]
    public void AHangingIndent_StartsTheFirstLineBeforeTheOthers()
    {
        var lines = Set(Para(string.Join(" ", Enumerable.Repeat("abcd", 40)), left: 60, first: -40), Column);

        Assert.Equal(20, lines[0].Pieces[0].X, 6);
        Assert.Equal(60, lines[1].Pieces[0].X, 6);
    }

    [Fact]
    public void AnIndentThatLeavesNoRoom_IsCutSoThatTheTextStillIsSet()
    {
        var lines = Set(Para("abc def", left: 10_000), Column);

        Assert.Equal("abc def", string.Join(" ", lines.Select(line => line.Text)));
        Assert.All(lines, line => Assert.True(line.Pieces[0].X <= Column * 0.8 + 0.01));
    }

    [Fact]
    public void TheIndentIsTheSameShareOfAnyColumn_AndCentreAndRightWorkInsideIt()
    {
        var wide = Assert.Single(Set(Para("abc", left: 100), 2 * Column));
        var centred = Assert.Single(Set(Para("abc", left: 100, align: TextAlign.Center), Column));
        var right = Assert.Single(Set(Para("abc", left: 100, align: TextAlign.Right), Column));

        Assert.Equal(200, wide.Pieces[0].X, 6);
        Assert.Equal(100 + (Column - 100 - 30) / 2, centred.Pieces[0].X, 6);
        Assert.Equal(Column - 30, right.Pieces[0].X, 6);
    }

    private static RichPicture Picture(double share, int width = 40, int height = 30) => new([1], true, width, height, share);

    [Fact]
    public void APicture_IsALineOfItsOwn_AsWideAsItsShareOfTheColumn_AndAsTallAsItsShape()
    {
        var document = new RichDocument([RichParagraph.OfPicture(Picture(0.5, 40, 30), TextAlign.Left), new RichParagraph(TextAlign.Left, [new RichRun("text", default)])]);

        var lines = Set(document, 400);

        Assert.Equal(2, lines.Count);
        Assert.Same(document.Paragraphs[0].Picture, lines[0].Picture);
        Assert.Equal(200, lines[0].PictureWidth, 6);
        Assert.Equal(150, lines[0].PictureHeight, 6);
        Assert.Equal(150 + 2 * TextLayout.PictureGap, lines[0].Height, 6);
        Assert.Equal(TextLayout.PictureGap, lines[0].PictureTop, 6);
        Assert.Empty(lines[0].Pieces);
        Assert.Null(lines[1].Picture);
    }

    [Theory]
    [InlineData(TextAlign.Left, 0)]
    [InlineData(TextAlign.Center, 100)]
    [InlineData(TextAlign.Right, 200)]
    [InlineData(TextAlign.Justify, 0)]
    public void APicture_IsAtTheLeftTheMiddleOrTheRight(TextAlign align, double expected)
    {
        var line = Assert.Single(Set(new RichDocument([RichParagraph.OfPicture(Picture(0.5), align)]), 400));

        Assert.Equal(expected, line.PictureX, 6);
    }

    [Fact]
    public void APictureThatIsTallerThanTheMaximum_IsMadeSmaller_KeepingItsShape()
    {
        var document = new RichDocument([RichParagraph.OfPicture(Picture(1.0, 40, 30))]);

        var line = Assert.Single(TextLayout.Layout(document, 400, new Fixed(), maximumPictureHeight: 150));

        Assert.Equal(150, line.PictureHeight, 6);
        Assert.Equal(200, line.PictureWidth, 6);
    }

    [Fact]
    public void ALeftPicture_StartsAtTheIndentOfItsParagraph()
    {
        var paragraph = RichParagraph.OfPicture(Picture(0.25), TextAlign.Left) with { LeftIndent = 108 };

        var line = Assert.Single(Set(new RichDocument([paragraph]), 2 * Column));

        Assert.Equal(216, line.PictureX, 6);
    }

    [Fact]
    public void ThePicturesAtTheEnd_AreNotBlankLinesToBeCut()
    {
        var document = new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("a", default)]), RichParagraph.OfPicture(Picture(0.5))]);

        Assert.Equal(2, Set(document, 400).Count);
    }

    [Fact]
    public void Cyrillic_IsSetLikeAnyText()
    {
        var lines = Set(Doc("Привіт світе, це довгий опис фотографії"), 150);

        Assert.True(lines.Count > 1);
        Assert.Equal("Привіт світе, це довгий опис фотографії", string.Join(" ", lines.Select(line => line.Text)));
    }
}
