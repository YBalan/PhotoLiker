namespace PhotoLiker.PhotoBook.Tests;

/// <summary>
/// The bullets and the numbers of a list: read from the RTF a Rich Text Box writes (the samples here are what it wrote), kept with the paragraph and not in its text, written back so that the
/// Rich Text Box shows the list again, and put before the text of the item (hanging out to its left) by what draws the text of a book.
/// </summary>
public class RichListTests
{
    /// <summary>Every character is 10 points wide.</summary>
    private sealed class Fixed : ITextMeasurer
    {
        public double Width(string text, RichStyle style) => text.Length * 10;
    }

    // What a Rich Text Box writes for three items with bullets, three numbered ones and a paragraph that is not in a list
    private const string Bullets =
        @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033{\fonttbl{\f0\fnil Segoe UI;}{\f1\fnil\fcharset2 Symbol;}}" + "\r\n" +
        @"{\*\generator Riched20 10.0.26100}\viewkind4\uc1 " + "\r\n" +
        @"\pard{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'B7}}\fi-360\li720\f0\fs24 one\par" + "\r\n" +
        @"{\pntext\f1\'B7\tab}two\par" + "\r\n" +
        @"{\pntext\f1\'B7\tab}three\par" + "\r\n}";

    private const string Numbers =
        @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033{\fonttbl{\f0\fnil Segoe UI;}}" + "\r\n" +
        @"{\*\generator Riched20 10.0.26100}\viewkind4\uc1 " + "\r\n" +
        @"\pard " + "\r\n" +
        @"{\pntext\f0 1.\tab}{\*\pn\pnlvlbody\pnf0\pnindent0\pnstart1\pndec{\pntxta.}}" + "\r\n" +
        @"\fi-360\li720\f0\fs24 one\par" + "\r\n" +
        @"{\pntext\f0 2.\tab}two\par" + "\r\n" +
        @"{\pntext\f0 3.\tab}three\par" + "\r\n" + "\r\n" +
        @"\pard plain\par" + "\r\n}";

    private static RichParagraph Item(string marker, string text, bool bold = false) =>
        new(TextAlign.Left, [new RichRun(text, new RichStyle(Bold: bold))]) { LeftIndent = 36, FirstLineIndent = -18, Marker = marker };

    // ── Reading ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheBulletsOfARichTextBox_AreTheMarkerOfTheirParagraphs_AndNotItsText()
    {
        var document = RtfParser.Parse(Bullets);

        Assert.Equal(3, document.Paragraphs.Count);
        Assert.All(document.Paragraphs, paragraph => Assert.Equal("•", paragraph.Marker));
        Assert.All(document.Paragraphs, paragraph => Assert.False(paragraph.IsNumbered));
        Assert.Equal("one\ntwo\nthree", document.PlainText); // the text is what the Rich Text Box has as its text: the bullets are not in it
        Assert.Equal([36.0, 36.0, 36.0], document.Paragraphs.Select(paragraph => paragraph.LeftIndent)); // the first line hangs out to the left of the others
        Assert.Equal([-18.0, -18.0, -18.0], document.Paragraphs.Select(paragraph => paragraph.FirstLineIndent));
        Assert.True(document.HasLists);
    }

    [Fact]
    public void TheNumbersOfARichTextBox_AreTheMarkerAsItShowsThem_AndAParagraphAfterTheListHasNone()
    {
        var document = RtfParser.Parse(Numbers);

        Assert.Equal(["1.", "2.", "3.", null], document.Paragraphs.Select(paragraph => paragraph.Marker));
        Assert.Equal([true, true, true, false], document.Paragraphs.Select(paragraph => paragraph.IsNumbered));
        Assert.Equal("one\ntwo\nthree\nplain", document.PlainText);
        Assert.Equal((0.0, 0.0), (document.Paragraphs[3].LeftIndent, document.Paragraphs[3].FirstLineIndent)); // \pard took the indents off
    }

    [Fact]
    public void TheWordsOfAListItem_KeepTheirLook_AndTheMarkerIsNotOneOfTheRuns()
    {
        var document = RtfParser.Parse(@"{\rtf1{\fonttbl{\f0 Segoe UI;}}\pard{\pntext\f0 7.\tab}{\*\pn\pnlvlbody\pnf0\pnindent0\pnstart7\pndec{\pntxta.}}\fi-360\li720 plain {\b bold}\par}");

        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("7.", paragraph.Marker);
        Assert.Equal(["plain ", "bold"], paragraph.Runs.Select(run => run.Text));
        Assert.True(paragraph.Runs[1].Style.Bold);
    }

    [Fact]
    public void AnItemWithNothingWrittenInIt_KeepsItsMarker_AndIsABlankParagraph()
    {
        var document = RtfParser.Parse(@"{\rtf1{\fonttbl{\f0 Segoe UI;}}\pard{\pntext\f0 1.\tab}{\*\pn\pnlvlbody\pnf0\pnindent0\pnstart1\pndec{\pntxta.}}\fi-360\li720 first\par{\pntext\f0 2.\tab}\par}");

        Assert.Equal(["1.", "2."], document.Paragraphs.Select(paragraph => paragraph.Marker));
        Assert.True(document.Paragraphs[1].IsBlank);
        Assert.Empty(document.Paragraphs[1].Runs);
    }

    [Fact]
    public void TheListTextOfWord_IsTheMarkerToo()
    {
        var document = RtfParser.Parse(@"{\rtf1\ansi{\fonttbl{\f0 Calibri;}}\pard\ls1\ilvl0\fi-360\li720{\listtext\f0 a)\tab}Item\par}");

        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("a)", paragraph.Marker);
        Assert.True(paragraph.IsNumbered); // letters count as numbers: they are not a bullet
        Assert.Equal("Item", paragraph.PlainText);
    }

    [Fact]
    public void TheMarkerWordWrites_StartsWithPardAndPlain_AndTheIndentsOfTheParagraphStay()
    {
        // Word writes the group of the marker as if it were a paragraph of its own: \pard there is not the end of what was set for the paragraph
        var document = RtfParser.Parse(@"{\rtf1\ansi{\fonttbl{\f0 Calibri;}{\f3\fnil\fcharset2 Symbol;}}\pard\plain\ql\fi-360\li720\tx720{\listtext\pard\plain\qr\fs24\f3\'b7\tab}Item text\par}");

        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("•", paragraph.Marker);
        Assert.Equal("Item text", paragraph.PlainText);
        Assert.Equal((36.0, -18.0), (paragraph.LeftIndent, paragraph.FirstLineIndent));
        Assert.Equal([36.0], paragraph.TabStops);
        Assert.Equal(TextAlign.Left, paragraph.Align); // the \qr of the marker is not the paragraph's
    }

    [Theory]
    [InlineData(@"{\fonttbl{\f0 Segoe UI;}{\f1\fnil\fcharset2 Symbol;}}\pard{\pntext\f1\'B7\tab}x\par")] // the bullet of a Rich Text Box: a character of the Symbol font, which another font draws as a dot
    [InlineData(@"{\fonttbl{\f0 Segoe UI;}{\f1\fnil\fcharset2 Wingdings;}}\pard{\listtext\f1\'A7\tab}x\par")] // any character of a font of symbols is a bullet
    [InlineData(@"{\fonttbl{\f0 Segoe UI;}}\pard{\pntext\f0 \" + "u8226?" + @"\tab}x\par")]
    [InlineData(@"{\fonttbl{\f0 Segoe UI;}}\pard{\pntext\f0 \bullet\tab}x\par")]
    public void ABullet_IsABulletWhicheverWayItIsWritten(string body)
    {
        var paragraph = Assert.Single(RtfParser.Parse(@"{\rtf1\ansi" + body + "}").Paragraphs);

        Assert.Equal("•", paragraph.Marker);
        Assert.False(paragraph.IsNumbered);
        Assert.Equal("x", paragraph.PlainText);
    }

    [Fact]
    public void ADashAsABullet_StaysADash()
    {
        var paragraph = Assert.Single(RtfParser.Parse(@"{\rtf1\ansi{\fonttbl{\f0 Segoe UI;}}\pard{\pntext\f0 \endash\tab}x\par}").Paragraphs);

        Assert.Equal("–", paragraph.Marker);
    }

    [Fact]
    public void ThePnGroupThatDescribesTheList_IsNotText()
    {
        var document = RtfParser.Parse(Numbers);

        Assert.DoesNotContain("pn", document.PlainText);
        Assert.DoesNotContain("dec", document.PlainText);
        Assert.DoesNotContain(".", document.PlainText);
    }

    [Fact]
    public void ARtfOfTextOnly_HasNoLists_AndWithMarkersInTextGivesTheDocumentItself()
    {
        var document = RtfParser.Parse(@"{\rtf1\ansi\pard text\par}");

        Assert.False(document.HasLists);
        Assert.Same(document, document.WithMarkersInText());
        Assert.Null(Assert.Single(document.Paragraphs).Marker);
    }

    // ── The marker as a part of the text ────────────────────────────────────

    [Fact]
    public void TheMarker_IsTheStartOfTheText_AndATabEndsItAtTheLeftIndent()
    {
        var paragraph = Item("3.", "text").WithMarkerInText();

        Assert.Null(paragraph.Marker);
        Assert.Equal("3.\ttext", paragraph.PlainText);
        Assert.Equal([36.0], paragraph.TabStops); // the text of the item starts where its other lines do
        Assert.Equal((36.0, -18.0), (paragraph.LeftIndent, paragraph.FirstLineIndent));
    }

    [Fact]
    public void TheMarker_LooksAsTheStartOfTheTextDoes_ButIsNotUnderlinedOrStruckThrough()
    {
        var item = new RichParagraph(TextAlign.Left, [new RichRun("word", new RichStyle(Bold: true, Italic: true, Underline: true, Strike: true, Size: 16, Color: 0xC81E1E)), new RichRun(" end", default)])
        {
            LeftIndent = 36,
            FirstLineIndent = -18,
            Marker = "1.",
        };

        var marker = item.WithMarkerInText().Runs[0];

        Assert.Equal("1.\t", marker.Text);
        Assert.Equal(new RichStyle(Bold: true, Italic: true, Size: 16, Color: 0xC81E1E), marker.Style);
    }

    [Fact]
    public void AStopThatTheParagraphHasAlready_IsNotAddedTwice_AndTheOthersStay()
    {
        var item = Item("•", "text") with { TabStops = [20, 36, 100] };

        Assert.Equal([20.0, 36.0, 100.0], item.WithMarkerInText().TabStops);
    }

    [Fact]
    public void AParagraphWhoseFirstLineDoesNotHang_GetsNoStopOfItsOwn()
    {
        var item = new RichParagraph(TextAlign.Left, [new RichRun("text", default)]) { LeftIndent = 36, Marker = "•" };

        Assert.Empty(item.WithMarkerInText().TabStops); // the tab goes to the next half inch, as a tab does
    }

    [Fact]
    public void AnItemWithNothingWrittenInIt_ShowsNoMarker()
    {
        var empty = new RichParagraph(TextAlign.Left, []) { LeftIndent = 36, FirstLineIndent = -18, Marker = "2." };

        var shown = empty.WithMarkerInText();

        Assert.Null(shown.Marker);
        Assert.Empty(shown.Runs);
        Assert.True(shown.IsBlank);
    }

    [Fact]
    public void AParagraphThatIsNotAnItem_IsGivenBackAsItIs()
    {
        var paragraph = new RichParagraph(TextAlign.Left, [new RichRun("text", default)]);

        Assert.Same(paragraph, paragraph.WithMarkerInText());
    }

    // ── Writing ─────────────────────────────────────────────────────────────

    [Fact]
    public void ABulletedAndANumberedList_SurviveTheTripThroughRtf_WithTheNumbersTheyHad()
    {
        var document = new RichDocument([
            Item("•", "one"), Item("•", "two"),
            new RichParagraph(TextAlign.Left, [new RichRun("between", default)]),
            Item("5.", "five"), Item("6.", "six"),
        ]);

        var back = RtfParser.Parse(RtfWriter.Write(document));

        Assert.Equal(["•", "•", null, "5.", "6."], back.Paragraphs.Select(paragraph => paragraph.Marker));
        Assert.Equal(document.PlainText, back.PlainText);
        Assert.Equal([36.0, 36.0, 0.0, 36.0, 36.0], back.Paragraphs.Select(paragraph => paragraph.LeftIndent));
        Assert.Equal([-18.0, -18.0, 0.0, -18.0, -18.0], back.Paragraphs.Select(paragraph => paragraph.FirstLineIndent));
    }

    [Fact]
    public void TheRtf_HasTheGroupsOfARichTextBox_SoThatItShowsTheListAndNumbersItFromTheStartTheItemHad()
    {
        var rtf = RtfWriter.Write(new RichDocument([Item("•", "a"), Item("4.", "b"), Item("(7)", "c")]));

        Assert.Contains(@"{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'B7}}", rtf);
        Assert.Contains(@"{\pntext\f0 4.\tab}{\*\pn\pnlvlbody\pnf0\pnindent0\pnstart4\pndec{\pntxta .}}", rtf);
        Assert.Contains(@"{\pntext\f0 (7)\tab}{\*\pn\pnlvlbody\pnf0\pnindent0\pnstart7\pndec{\pntxtb (}{\pntxta )}}", rtf);
        Assert.Contains(@"{\f1\fnil\fcharset2 Symbol;}", rtf);
    }

    [Fact]
    public void TheSymbolFont_IsInTheTableOnlyForABullet()
    {
        Assert.DoesNotContain("Symbol", RtfWriter.Write(new RichDocument([Item("1.", "a"), new RichParagraph(TextAlign.Left, [new RichRun("b", default)])])));
        Assert.Contains("Symbol", RtfWriter.Write(new RichDocument([Item("•", "a")])));
    }

    [Theory]
    [InlineData("a)")]
    [InlineData("iv.")]
    [InlineData("(3.")]
    [InlineData("1.1")]
    [InlineData("123456.")]
    public void AMarkerThatARichTextBoxHasNoKindOfListFor_IsTextBeforeTheTextOfTheItem(string marker)
    {
        var back = RtfParser.Parse(RtfWriter.Write(new RichDocument([Item(marker, "text")])));

        var paragraph = Assert.Single(back.Paragraphs);
        Assert.Null(paragraph.Marker);
        Assert.Equal(marker + "\ttext", paragraph.PlainText);
        Assert.Equal((36.0, -18.0), (paragraph.LeftIndent, paragraph.FirstLineIndent));
    }

    [Fact]
    public void AnItemWithNothingWrittenInIt_IsWrittenWithItsMarker()
    {
        var empty = new RichParagraph(TextAlign.Left, []) { LeftIndent = 36, FirstLineIndent = -18, Marker = "2." };

        var back = RtfParser.Parse(RtfWriter.Write(new RichDocument([Item("1.", "first"), empty])));

        Assert.Equal(["1.", "2."], back.Paragraphs.Select(paragraph => paragraph.Marker));
        Assert.True(back.Paragraphs[1].IsBlank);
    }

    [Fact]
    public void WhatIsReadFromARichTextBox_IsWrittenAndReadAgainTheSame()
    {
        var first = RtfParser.Parse(Numbers);

        var again = RtfParser.Parse(RtfWriter.Write(first));

        Assert.Equal(first.Paragraphs.Select(paragraph => paragraph.Marker), again.Paragraphs.Select(paragraph => paragraph.Marker));
        Assert.Equal(first.PlainText, again.PlainText);
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheMarkerHangsOutToTheLeft_AndTheTextStartsWhereTheLinesAfterItDo()
    {
        var lines = TextLayout.Layout(new RichDocument([Item("•", string.Join(" ", Enumerable.Repeat("word", 12)))]), RichDocument.ReferenceWidth, new Fixed());

        Assert.True(lines.Count > 1);
        Assert.Equal("•", lines[0].Pieces[0].Text);
        Assert.StartsWith("word word", lines[0].Pieces[1].Text); // (the words after the tab are one text)
        Assert.Equal(18, lines[0].Pieces[0].X, 6); // the left indent less the hang
        Assert.Equal(36, lines[0].Pieces[1].X, 6); // the tab stops at the left indent
        Assert.All(lines.Skip(1), line => Assert.Equal(36, line.Pieces[0].X, 6));
    }

    [Fact]
    public void ANumberWiderThanTheHang_PushesTheTextToTheNextHalfInch()
    {
        var line = Assert.Single(TextLayout.Layout(new RichDocument([Item("100.", "text")]), RichDocument.ReferenceWidth, new Fixed()));

        Assert.Equal(18, line.Pieces[0].X, 6);
        Assert.Equal(72, line.Pieces[1].X, 6); // "100." is 40 wide: it ends at 58, past the stop at 36
    }

    [Fact]
    public void AnItemWithNothingWrittenInIt_IsABlankLine_WithNoMarker()
    {
        var empty = new RichParagraph(TextAlign.Left, []) { LeftIndent = 36, FirstLineIndent = -18, Marker = "2." };

        var lines = TextLayout.Layout(new RichDocument([Item("1.", "first"), empty, Item("3.", "third")]), RichDocument.ReferenceWidth, new Fixed());

        Assert.Equal(["1.first", "", "3.third"], lines.Select(line => line.Text));
    }

    [Fact]
    public void ATextThatEndsWithAnEmptyItem_DoesNotShowIt()
    {
        var empty = new RichParagraph(TextAlign.Left, []) { LeftIndent = 36, FirstLineIndent = -18, Marker = "2." };

        var lines = TextLayout.Layout(new RichDocument([Item("1.", "first"), empty]), RichDocument.ReferenceWidth, new Fixed());

        Assert.Equal(["1.first"], lines.Select(line => line.Text));
    }
}
