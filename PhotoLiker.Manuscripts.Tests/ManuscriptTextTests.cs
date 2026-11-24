using PhotoLiker.PhotoBook;
using TextLine = PhotoLiker.Drawing.Ocr.TextLine;
using PhotoLiker.Drawing.Ocr;

namespace PhotoLiker.Manuscripts.Tests
{
    public sealed class ManuscriptTextTests
    {
        private static string Join(string text, int start = 0, int length = -1) => ManuscriptText.Apply(text, ManuscriptText.JoinEdits(text, start, length));

        [Fact]
        public void ALineBreakInsideAParagraph_BecomesASpace()
        {
            Assert.Equal("one two three", Join("one\ntwo\nthree"));
        }

        [Fact]
        public void ABlankLine_IsWhereAParagraphEnds_AndStays()
        {
            Assert.Equal("one two\n\nthree four", Join("one\ntwo\n\nthree\nfour"));
        }

        [Fact]
        public void ABreakAtTheStartOrTheEnd_Stays()
        {
            Assert.Equal("\nabc", Join("\nabc"));
            Assert.Equal("abc\n", Join("abc\n"));
        }

        [Theory]
        [InlineData("закоха-\nна", "закохана")]
        [InlineData("закоха¬\nна", "закохана")]
        [InlineData("закоха-  \n  на", "закохана")]
        public void AWordSplitByAHyphenAtTheEndOfALine_IsJoinedWithoutTheHyphen(string text, string expected)
        {
            Assert.Equal(expected, Join(text));
        }

        [Fact]
        public void AHyphenBeforeACapitalOrAFigure_IsNotAWordBreak_ItIsADashOrACompound()
        {
            Assert.Equal("до Києва- Оксана", Join("до Києва-\nОксана"));
            Assert.Equal("тема 1- 2", Join("тема 1-\n2"));
        }

        [Fact]
        public void ADashAtTheStartOfTheNextLine_IsKept_AsADialogueDashIs()
        {
            Assert.Equal("примусово - бадьоре", Join("примусово\n- бадьоре"));
        }

        [Fact]
        public void ALineThatEndsWithASpacedDash_KeepsItAndGetsASpace()
        {
            Assert.Equal("мама - на працю", Join("мама -\nна працю"));
        }

        [Fact]
        public void TheBlanksAroundABreak_AreOneSpace()
        {
            Assert.Equal("one two", Join("one   \n   two"));
        }

        [Fact]
        public void OnlyTheSelectedPartIsJoined()
        {
            const string text = "one\ntwo\nthree\nfour";
            var start = text.IndexOf("two", StringComparison.Ordinal);

            // From "two" for the length of "two\nthree": the break between two and three is inside; the ones before and after are not
            Assert.Equal("one\ntwo three\nfour", Join(text, start, "two\nthree".Length));
        }

        [Fact]
        public void TheEditsComeLastFirst_SoTheyCanBeDoneOneAfterAnotherInAnEditor()
        {
            var edits = ManuscriptText.JoinEdits("a\nb\nc");

            Assert.Equal(2, edits.Count);
            Assert.True(edits[0].Index > edits[1].Index);
            Assert.Equal(" ", edits[0].Replacement);
        }

        [Fact]
        public void NothingToJoin_NoEdits()
        {
            Assert.Empty(ManuscriptText.JoinEdits("one line"));
            Assert.Empty(ManuscriptText.JoinEdits("a\n\nb"));
            Assert.Empty(ManuscriptText.JoinEdits(""));
        }

        [Fact]
        public void APageHasContent_WhenItHasWordsOrAPicture_NotWhenItHasNeither()
        {
            var picture = new RichPicture([1], true, 40, 30, 0.5);
            var withPicture = new ManuscriptPage { Rtf = RtfWriter.Write(new RichDocument([RichParagraph.OfPicture(picture)])) };
            var withText = new ManuscriptPage { Rtf = RtfWriter.Write(RichDocument.FromPlainText("words")) };
            var blank = new ManuscriptPage { Rtf = RtfWriter.Write(RichDocument.FromPlainText("   ")) };

            Assert.True(ManuscriptText.HasContent(withPicture));
            Assert.False(ManuscriptText.HasText(withPicture)); // a picture is no words
            Assert.True(ManuscriptText.HasPictures(withPicture.Rtf));
            Assert.True(ManuscriptText.HasContent(withText));
            Assert.False(ManuscriptText.HasPictures(withText.Rtf));
            Assert.False(ManuscriptText.HasContent(blank));
            Assert.False(ManuscriptText.HasContent(new ManuscriptPage()));
            Assert.False(ManuscriptText.HasPictures(null));
        }

        [Fact]
        public void TheLinesOfAScan_MakeTheTextALineToALine()
        {
            var box = TextRegion.FromCentre(new System.Drawing.PointF(10, 10), 20, 5, 0);

            var text = ManuscriptText.FromLines([new TextLine("перший", 0.9f, box), new TextLine("другий", 0.8f, box)]);

            Assert.Equal("перший\nдругий", text);
        }

        private static PhotoLiker.Drawing.Ocr.TextLine At(string text, float x, float y, float height = 20) =>
            new(text, 0.9f, TextRegion.FromCentre(new System.Drawing.PointF(x, y), 200, height, 0));

        [Fact]
        public void MoreRoomThanUsualBetweenTwoLinesOfThePage_IsABlankLineInTheText()
        {
            // The lines are 40 apart, except one gap of 100: a new paragraph there
            var text = ManuscriptText.FromLines([At("one", 100, 100), At("two", 100, 140), At("three", 100, 180), At("four", 100, 280), At("five", 100, 320)]);

            Assert.Equal("one\ntwo\nthree\n\nfour\nfive", text);
        }

        [Fact]
        public void LinesThatAreEvenlySpaced_HaveNoBlankLine_AndNeitherDoLinesSideBySideOnARow()
        {
            Assert.Equal("a\nb\nc\nd", ManuscriptText.FromLines([At("a", 100, 100), At("b", 100, 140), At("c", 100, 180), At("d", 100, 221)]));
            Assert.Equal("left\tright\nunder", ManuscriptText.FromLines([At("left", 100, 100), At("right", 400, 102), At("under", 100, 140)])); // one row, with a tab for the gap
        }

        [Fact]
        public void FewLines_AreNeverSeparated_BecauseThereIsNoUsualGapToCompareWith()
        {
            Assert.Equal("a\nb", ManuscriptText.FromLines([At("a", 100, 100), At("b", 100, 300)]));
            Assert.Equal("a", ManuscriptText.FromLines([At("a", 100, 100)]));
            Assert.Equal(string.Empty, ManuscriptText.FromLines([]));
        }

        [Fact]
        public void TheLinesOfAText_ThatAreNotBlank_AreFoundWithTheirPlaces_AndACharacterSaysWhichLineItIsIn()
        {
            const string text = "alpha\nbe\n\n  \ngamma";

            var spans = ManuscriptText.LineSpans(text);

            Assert.Equal([(0, 5), (6, 2), (13, 5)], spans);
            Assert.Equal(0, ManuscriptText.LineAt(spans, 0));
            Assert.Equal(0, ManuscriptText.LineAt(spans, 5)); // the break itself is the end of its line
            Assert.Equal(1, ManuscriptText.LineAt(spans, 7));
            Assert.Equal(-1, ManuscriptText.LineAt(spans, 9)); // on a blank line
            Assert.Equal(-1, ManuscriptText.LineAt(spans, 11));
            Assert.Equal(2, ManuscriptText.LineAt(spans, 16));
            Assert.Equal(2, ManuscriptText.LineAt(spans, text.Length));
            Assert.Empty(ManuscriptText.LineSpans(string.Empty));
            Assert.Empty(ManuscriptText.LineSpans("\n \n"));
        }

        [Fact]
        public void APageIsNewScannedOrEdited_BySomethingWrittenAndWhatTheScanRead()
        {
            Assert.Equal(PageState.New, ManuscriptText.StateOf(new ManuscriptPage()));

            var scanned = new ManuscriptPage { Scanned = "hello world", Rtf = @"{\rtf1\ansi hello world}" };
            Assert.Equal(PageState.Scanned, ManuscriptText.StateOf(scanned));

            var spaced = new ManuscriptPage { Scanned = "hello world", Rtf = @"{\rtf1\ansi hello   world\par}" };
            Assert.Equal(PageState.Scanned, ManuscriptText.StateOf(spaced)); // blanks are not a change

            var changed = new ManuscriptPage { Scanned = "hello world", Rtf = @"{\rtf1\ansi hello there}" };
            Assert.Equal(PageState.Edited, ManuscriptText.StateOf(changed));

            var written = new ManuscriptPage { Rtf = @"{\rtf1\ansi typed by hand}" };
            Assert.Equal(PageState.Edited, ManuscriptText.StateOf(written));

            var emptied = new ManuscriptPage { Scanned = "hello", Rtf = null };
            Assert.Equal(PageState.Edited, ManuscriptText.StateOf(emptied)); // the person deleted what the scan read
        }

        [Fact]
        public void HasText_IsMoreThanBlanks()
        {
            Assert.False(ManuscriptText.HasText(new ManuscriptPage()));
            Assert.False(ManuscriptText.HasText(new ManuscriptPage { Rtf = @"{\rtf1\ansi   \par}" }));
            Assert.True(ManuscriptText.HasText(new ManuscriptPage { Rtf = @"{\rtf1\ansi x}" }));
        }

        [Fact]
        public void CyrillicInTheRtf_IsReadBack()
        {
            Assert.Equal("Магія голосу", ManuscriptText.PlainOf(@"{\rtf1\ansi\ansicpg1251 \'cc\'e0\'e3\'b3\'ff \'e3\'ee\'eb\'ee\'f1\'f3}"));
        }
    }
}
