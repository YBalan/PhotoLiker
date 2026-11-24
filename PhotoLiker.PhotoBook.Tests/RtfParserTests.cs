namespace PhotoLiker.PhotoBook.Tests;

public class RtfParserTests
{
    private static RichDocument Parse(string rtf) => RtfParser.Parse(rtf);

    // A character the way a Rich Text Box writes it: a backslash, the letter u, the number of the character, and a stand-in for readers that do not know Unicode
    private static string U(int code) => "\\" + "u" + code + "?";


    [Fact]
    public void NothingAtAll_IsAnEmptyDocument()
    {
        Assert.True(Parse("").IsEmpty);
        Assert.True(RtfParser.Parse(null).IsEmpty);
        Assert.True(Parse("   ").IsEmpty);
    }

    [Fact]
    public void ARichTextBoxThatIsEmpty_IsAnEmptyDocument()
    {
        // What a Rich Text Box with nothing in it writes: one paragraph mark
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}
{\*\generator Riched20 10.0.19041}\viewkind4\uc1
\pard\f0\fs24\lang1033\par
}";
        Assert.True(Parse(rtf).IsEmpty);
    }

    [Fact]
    public void TextThatIsNotRtf_IsTakenAsItIs()
    {
        var document = Parse("one\ntwo");

        Assert.Equal(["one", "two"], document.Paragraphs.Select(paragraph => paragraph.PlainText));
    }

    [Fact]
    public void PlainText_IsAParagraphEachLine_InTheDefaultStyle()
    {
        var document = RichDocument.FromPlainText("a\r\n\r\nb");

        Assert.Equal(3, document.Paragraphs.Count);
        Assert.True(document.Paragraphs[1].IsBlank);
        Assert.Equal(default, document.Paragraphs[0].Runs[0].Style);
    }

    [Fact]
    public void BoldItalicUnderlineAndStrike_AreRunsOfTheirOwn()
    {
        var document = Parse(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\pard\f0 plain \b bold\b0  \i italic\i0  \ul under\ulnone  \strike gone\strike0  end\par}");

        var runs = Assert.Single(document.Paragraphs).Runs;
        Assert.Equal(["plain ", "bold", " ", "italic", " ", "under", " ", "gone", " end"], runs.Select(run => run.Text));
        Assert.True(runs[1].Style.Bold);
        Assert.True(runs[3].Style.Italic);
        Assert.True(runs[5].Style.Underline);
        Assert.True(runs[7].Style.Strike);
        Assert.Equal(default, runs[8].Style);
    }

    [Fact]
    public void TheSpaceThatEndsAControlWord_IsNotText_ButTheNextOneIs()
    {
        var document = Parse(@"{\rtf1\b Hello\b0  world\par}");

        Assert.Equal(["Hello", " world"], Assert.Single(document.Paragraphs).Runs.Select(run => run.Text));
    }

    [Fact]
    public void TheSize_IsInHalfPoints()
    {
        var document = Parse(@"{\rtf1\fs24 twelve \fs40 twenty\par}");

        var runs = Assert.Single(document.Paragraphs).Runs;
        Assert.Equal(12, runs[0].Style.Size);
        Assert.Equal(20, runs[1].Style.Size);
    }

    [Fact]
    public void AColour_IsReadFromTheColourTable_AndZeroIsAutomatic()
    {
        var document = Parse(@"{\rtf1{\colortbl ;\red255\green0\blue0;\red0\green128\blue255;}\cf1 red \cf2 blue \cf0 auto\par}");

        var runs = Assert.Single(document.Paragraphs).Runs;
        Assert.Equal(0xFF0000, runs[0].Style.Color);
        Assert.Equal(0x0080FF, runs[1].Style.Color);
        Assert.Null(runs[2].Style.Color);
    }

    [Fact]
    public void AGroup_EndsTheStyleItSet()
    {
        var document = Parse(@"{\rtf1 before {\b bold} after\par}");

        var runs = Assert.Single(document.Paragraphs).Runs;
        Assert.Equal(["before ", "bold", " after"], runs.Select(run => run.Text));
        Assert.False(runs[0].Style.Bold);
        Assert.True(runs[1].Style.Bold);
        Assert.False(runs[2].Style.Bold);
    }

    [Fact]
    public void AlignmentAndParagraphs()
    {
        var document = Parse(@"{\rtf1\pard\qc centre\par\pard\qr right\par\pard\qj justified\par\pard left\par}");

        Assert.Equal([TextAlign.Center, TextAlign.Right, TextAlign.Justify, TextAlign.Left], document.Paragraphs.Select(paragraph => paragraph.Align));
        Assert.Equal(["centre", "right", "justified", "left"], document.Paragraphs.Select(paragraph => paragraph.PlainText));
    }

    [Fact]
    public void AnEmptyParagraph_IsABlankLine()
    {
        var document = Parse(@"{\rtf1 one\par\par two\par}");

        Assert.Equal(3, document.Paragraphs.Count);
        Assert.True(document.Paragraphs[1].IsBlank);
    }

    [Fact]
    public void ALineBreakAndATab_AreInTheText()
    {
        var document = Parse(@"{\rtf1 a\line b\tab c\par}");

        Assert.Equal("a\nb\tc", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void EscapedBracesAndBackslashes_AreText()
    {
        var document = Parse(@"{\rtf1 \{ \\ \} \par}");

        Assert.Equal("{ \\ }", Assert.Single(document.Paragraphs).PlainText.Trim());
    }

    [Fact]
    public void UnicodeCharacters_AreReadWhateverStandsInForThem()
    {
        // The way a Rich Text Box writes Cyrillic: \uN followed by a ? for readers that do not know it
        var document = Parse(@"{\rtf1\ansi\uc1 " + U(1055) + U(1088) + U(1080) + U(1074) + U(1110) + U(1090) + ", " + U(1089) + U(1074) + U(1110) + U(1090) + @"\par}");

        Assert.Equal("Привіт, світ", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void ANegativeUnicodeNumber_IsTheCharacterAbove32767()
    {
        // U+20AC (euro) is 8364, U+FB01 (fi ligature) is 64257, written as -1279
        var document = Parse(@"{\rtf1\uc1 " + U(8364) + U(-1279) + @"\par}");

        Assert.Equal("€ﬁ", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void ABytePair_IsReadInTheCodePageOfTheFont()
    {
        // 1251 is Cyrillic: \'cf\'f0\'e8\'e2\'b3\'f2 is "Привіт"
        var document = Parse(@"{\rtf1\ansi\ansicpg1251\deff0{\fonttbl{\f0\fcharset204 Arial;}}\f0 \'cf\'f0\'e8\'e2\'b3\'f2\par}");

        Assert.Equal("Привіт", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void TheCodePageOfAFont_WinsOverTheOneOfTheDocument()
    {
        var document = Parse(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fcharset0 Arial;}{\f1\fcharset204 Arial;}}\f0 caf\'e9 \f1 \'cf\'f0\par}");

        Assert.Equal("café Пр", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void WhatFollowsAUnicodeCharacter_IsSkippedAsManyAsUcSays()
    {
        // U() writes one stand-in character; \uc2 says there are two, so the next one is skipped as well
        var document = Parse(@"{\rtf1\uc2 " + U(1055) + @"?x\par}");

        Assert.Equal("Пx", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void TheFontTableTheColourTableAndTheGenerator_AreNotText()
    {
        var document = Parse(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}{\f1 Courier New;}}{\colortbl ;\red1\green2\blue3;}{\*\generator Riched20 10.0.19041}{\info{\title Secret}}\pard Only this\par}");

        Assert.Equal("Only this", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void APicture_IsLeftOut()
    {
        var document = Parse(@"{\rtf1 before {\pict\wmetafile8 0102030405} after\par}");

        Assert.Equal("before  after", Assert.Single(document.Paragraphs).PlainText);
    }

    [Fact]
    public void APictureThatIsAPng_IsAParagraphOfItsOwn_WithItsBytesAndItsWidth()
    {
        var png = TestImages.SeeThroughPng(40, 30);
        // As the Rich Text Box writes it: shown 3240 twips wide (162 points), 37.5 % of the 432 points of the reference column
        var rtf = @"{\rtf1\ansi{\fonttbl{\f0 Segoe UI;}}\pard Before\par" + "\r\n" +
                  @"\pard\qc\f1\lang1033{\pict{\*\picprop{\sp{\sn wzDescription}{\sv Image}}}\pngblip\picw1000\pich750\picwgoal3240\pichgoal2430 " + Convert.ToHexString(png) + @"}\f0\par" + "\r\n" +
                  @"\pard After\par}";

        var document = Parse(rtf);

        Assert.Equal(3, document.Paragraphs.Count); // the paragraph mark after the picture does not make a paragraph of its own
        Assert.Equal("Before", document.Paragraphs[0].PlainText);
        var picture = document.Paragraphs[1].Picture!;
        Assert.Equal(png, picture.Data);
        Assert.True(picture.IsPng);
        Assert.Equal((40, 30), (picture.PixelWidth, picture.PixelHeight));
        Assert.Equal(3240 / 20.0 / RichDocument.ReferenceWidth, picture.WidthShare, 6);
        Assert.Equal(TextAlign.Center, document.Paragraphs[1].Align);
        Assert.Equal("After", document.Paragraphs[2].PlainText);
        Assert.DoesNotContain("wzDescription", document.PlainText);
    }

    [Fact]
    public void APictureOfAnotherKind_OrWithBrokenBytes_IsLeftOut()
    {
        var document = Parse(@"{\rtf1 a\par {\pict\pngblip\picwgoal3000 89504e47zz}\par {\pict\jpegblip\picwgoal3000 0102030405060708090a0b0c0d0e0f10}\par b\par}");

        Assert.Equal(["a", "", "", "b"], document.Paragraphs.Select(paragraph => paragraph.PlainText));
        Assert.All(document.Paragraphs, paragraph => Assert.Null(paragraph.Picture));
    }

    [Fact]
    public void ThePictureScale_ChangesHowWideItIsShown()
    {
        var png = TestImages.SeeThroughPng(40, 30);

        var document = Parse(@"{\rtf1\pard {\pict\pngblip\picwgoal4320\picscalex50 " + Convert.ToHexString(png) + @"}\par}");

        Assert.Equal(4320 / 20.0 * 0.5 / RichDocument.ReferenceWidth, Assert.Single(document.Paragraphs).Picture!.WidthShare, 6);
    }

    [Fact]
    public void AJpeg_IsAPictureToo()
    {
        var jpeg = TestImages.LandscapeJpeg();

        var document = Parse(@"{\rtf1\pard {\pict\jpegblip\picw3000\pich2000 " + Convert.ToHexString(jpeg) + @"}\par}");

        var picture = Assert.Single(document.Paragraphs).Picture!;
        Assert.False(picture.IsPng);
        Assert.Equal((TestImages.LandscapeWidth, TestImages.LandscapeHeight), (picture.PixelWidth, picture.PixelHeight));
        Assert.Equal(3000 * 72.0 / 2540.0 / RichDocument.ReferenceWidth, picture.WidthShare, 6); // no goal: its size in hundredths of a millimetre
    }

    [Fact]
    public void IndentsAndTabStops_AreInPoints_AndPardTakesThemOff()
    {
        var document = Parse(@"{\rtf1\pard\li720\fi-360\tx2000\tx1000 First\par Second\par\pard Third\par}");

        Assert.Equal((36.0, -18.0), (document.Paragraphs[0].LeftIndent, document.Paragraphs[0].FirstLineIndent));
        Assert.Equal([50.0, 100.0], document.Paragraphs[0].TabStops); // from the nearest to the farthest
        Assert.Equal(36.0, document.Paragraphs[1].LeftIndent); // a paragraph goes on as the one before until \pard
        Assert.Equal((0.0, 0.0), (document.Paragraphs[2].LeftIndent, document.Paragraphs[2].FirstLineIndent));
        Assert.Empty(document.Paragraphs[2].TabStops);
    }

    [Fact]
    public void PlainResetsTheLook()
    {
        var document = Parse(@"{\rtf1\b\i\fs40 big \plain small\par}");

        var runs = Assert.Single(document.Paragraphs).Runs;
        Assert.True(runs[0].Style.Bold && runs[0].Style.Italic);
        Assert.Equal(default, runs[1].Style);
    }

    [Theory]
    [InlineData(@"{\rtf1 broken {{ never closed")]
    [InlineData(@"{\rtf1 \u")]
    [InlineData(@"{\rtf1 \'")]
    [InlineData(@"{\rtf1 \'zz text\par}")]
    [InlineData(@"{\rtf1}}}} extra\par")]
    [InlineData(@"{\rtf1\fonttbl{\f0")]
    [InlineData(@"{\rtf1\colortbl ;\red")]
    public void BrokenRtf_NeverThrows(string rtf) => _ = Parse(rtf);

    [Fact]
    public void ABrokenDocument_GivesTheTextThatCouldBeRead()
    {
        var document = Parse(@"{\rtf1 first\par second {\b bold");

        Assert.Equal("first", document.Paragraphs[0].PlainText);
        Assert.Contains("second", document.PlainText);
        Assert.Contains("bold", document.PlainText);
    }

    [Fact]
    public void TheEmptyLinesAtTheEnd_AreLeftOutByWithoutTrailingBlank()
    {
        var document = Parse(@"{\rtf1 text\par\par\par}").WithoutTrailingBlank();

        Assert.Equal(["text"], document.Paragraphs.Select(paragraph => paragraph.PlainText));
        Assert.Same(document, document.WithoutTrailingBlank());
    }

    [Fact]
    public void TheEmptyLinesAtTheStart_Stay()
    {
        var document = Parse(@"{\rtf1\par text\par}").WithoutTrailingBlank();

        Assert.Equal(2, document.Paragraphs.Count);
    }
}
