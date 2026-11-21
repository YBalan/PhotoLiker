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

    [Fact]
    public void ATab_IsFourSpaces()
    {
        var line = Assert.Single(Set(Doc("a\tb"), 500));

        Assert.Equal(60, line.Pieces[^1].X + line.Pieces[^1].Width, 6);
    }

    [Fact]
    public void Cyrillic_IsSetLikeAnyText()
    {
        var lines = Set(Doc("Привіт світе, це довгий опис фотографії"), 150);

        Assert.True(lines.Count > 1);
        Assert.Equal("Привіт світе, це довгий опис фотографії", string.Join(" ", lines.Select(line => line.Text)));
    }
}
