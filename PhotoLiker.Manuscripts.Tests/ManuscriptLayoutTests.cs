using System.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.PhotoBook;
using TextLine = PhotoLiker.Drawing.Ocr.TextLine;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>What the lines of a scan become as the text of a page: rows, indents, tabs, blank lines, and where the pictures go among them.</summary>
    public sealed class ManuscriptLayoutTests
    {
        /// <summary>A line that is <paramref name="width"/> wide with its left side at <paramref name="left"/>, its middle <paramref name="y"/> down the page, and 20 tall.</summary>
        private static TextLine Line(string text, float left, float y, float width = 200, float height = 20) =>
            new(text, 0.9f, TextRegion.FromCentre(new PointF(left + width / 2, y), width, height, 0));

        private static IReadOnlyList<LayoutRow> Rows(params TextLine[] lines) => ManuscriptLayout.Rows(lines);

        [Fact]
        public void NoLines_NoRows()
        {
            Assert.Empty(ManuscriptLayout.Rows([]));
            Assert.Equal(string.Empty, ManuscriptLayout.TextOf([]));
            Assert.Empty(ManuscriptLayout.ToDocument([]).Paragraphs);
        }

        [Fact]
        public void LinesOneUnderTheOther_AreARowEach_WithNothingInTheirWay()
        {
            var rows = Rows(Line("one", 100, 100), Line("two", 100, 140), Line("three", 100, 180));

            Assert.Equal(["one", "two", "three"], rows.Select(row => row.Text));
            Assert.All(rows, row => Assert.Equal(0, row.Indent));
            Assert.All(rows, row => Assert.Empty(row.TabStops));
            Assert.All(rows, row => Assert.False(row.BlankBefore));
            Assert.Equal([[0], [1], [2]], rows.Select(row => row.Lines.ToArray()));
        }

        [Fact]
        public void ARowThatStartsFurtherInThanTheOthers_IsIndentedInProportionToTheWidthOfTheText()
        {
            // The text is 100 to 700 (600 wide, which is the 432 points of the reference column); the first line of the paragraph starts 60 further in: 43.2 points, as a multiple of 4: 44
            var rows = Rows(Line("Red line starts here", 160, 100, 540), Line("and the next is at the margin", 100, 140, 600), Line("last", 100, 180, 600));

            Assert.Equal(44, rows[0].Indent);
            Assert.Equal(0, rows[1].Indent);
            Assert.Equal(0, rows[2].Indent);
        }

        [Fact]
        public void TwoLinesOneUnderTheOther_AreTwoRows_EvenWhenTheirTallBoxesAreLevel()
        {
            // The boxes of a scan are about twice as tall as the writing: 60 tall with their middles 18 apart, the second a little to the right of the first, over the same part of the page
            var rows = Rows(Line("above", 100, 100, 300, 60), Line("below", 200, 118, 300, 60));

            Assert.Equal(2, rows.Count);
            Assert.Equal(["above", "below"], rows.Select(row => row.Text));
        }

        [Fact]
        public void ALineSlightlyHigherThanItsNeighbour_ButBesideIt_IsOnItsRow()
        {
            var rows = Rows(Line("left", 100, 100, 150, 60), Line("right", 400, 112, 150, 60)); // 12 apart is a fifth of the height

            Assert.Equal("left\tright", Assert.Single(rows).Text);
        }

        [Fact]
        public void ALineThatIsHigherThanAThirdOfALine_IsNotOnTheRowOfItsNeighbour()
        {
            var rows = Rows(Line("left", 100, 100, 150, 60), Line("right", 400, 130, 150, 60));

            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public void ADriftOfTheHand_IsNotAnIndent()
        {
            // 10 of 600 are less than 5 % of the width of the text, and so are 25
            var rows = Rows(Line("a", 100, 100, 600), Line("b", 110, 140, 590), Line("c", 125, 180, 575), Line("d", 100, 220, 600));

            Assert.All(rows, row => Assert.Equal(0, row.Indent));
        }

        [Fact]
        public void TheLeftSideOfTheText_IsWhereMostRowsStart_NotTheOneThatStartsFurthestLeft()
        {
            // One note out in the margin (at 20) and ten lines at 100: the lines are not indented by 80
            var lines = new List<TextLine> { Line("note", 20, 20, 80) };
            for (var i = 0; i < 10; i++)
                lines.Add(Line("line " + i, 100, 60 + 40 * i, 500));

            var rows = ManuscriptLayout.Rows(lines);

            Assert.All(rows.Skip(1), row => Assert.Equal(0, row.Indent));
        }

        [Fact]
        public void LinesSideBySideOnOneRow_AreOneRow_WithATabWhereThePageHasAGap()
        {
            // "cat" 100-200 and its translation at 400-500: 200 apart on a text that is 400 wide
            var rows = Rows(Line("cat", 100, 100, 100), Line("кіт", 400, 102, 100), Line("dog", 100, 140, 100), Line("пес", 400, 139, 100));

            Assert.Equal(["cat\tкіт", "dog\tпес"], rows.Select(row => row.Text));
            Assert.Equal([[0, 1], [2, 3]], rows.Select(row => row.Lines.ToArray()));
        }

        [Fact]
        public void TwoLinesThatAreCloseOnARow_AreSeparatedBySpace_NotATab()
        {
            // A reader that cut a line in two at a wide space: 30 apart (6 %) on a text that is 500 wide
            var rows = Rows(Line("first half", 100, 100, 200), Line("second half", 330, 101, 270), Line("below", 100, 140, 500));

            Assert.Equal("first half second half", rows[0].Text);
            Assert.Empty(rows[0].TabStops);
        }

        [Fact]
        public void TheTabStops_AreWhereTheColumnsOfThePageAre_TheSameInEveryRowOfATable()
        {
            // The second column starts at about 400 (a hand is a few points out) and the third at 600; the text is 100 to 700, so 432 points are 600 wide: a pixel is 0.72 points
            var rows = Rows(
                Line("a", 100, 100, 100), Line("b", 400, 100, 100), Line("c", 600, 100, 100),
                Line("d", 100, 140, 100), Line("e", 404, 140, 100), Line("f", 598, 140, 102),
                Line("g", 100, 180, 100), Line("h", 397, 180, 100), Line("i", 601, 180, 99));

            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal(2, row.TabStops.Count));
            Assert.Equal(rows[0].TabStops, rows[1].TabStops);
            Assert.Equal(rows[0].TabStops, rows[2].TabStops);
            Assert.Equal(300 * 0.72, rows[0].TabStops[0], 0);
            Assert.Equal(500 * 0.72, rows[0].TabStops[1], 0);
            Assert.True(rows[0].TabStops[0] < rows[0].TabStops[1]);
        }

        [Fact]
        public void ARowThatSkipsAColumn_StopsItsOneTabAtTheColumnItGoesTo()
        {
            var rows = Rows(
                Line("a", 100, 100, 100), Line("b", 400, 100, 100), Line("c", 600, 100, 100),
                Line("d", 100, 140, 100), Line("e", 400, 140, 100), Line("f", 600, 140, 100),
                Line("g", 100, 180, 100), Line("h", 600, 180, 100));

            Assert.Equal(2, rows[0].TabStops.Count);
            Assert.Equal([rows[0].TabStops[1]], rows[2].TabStops);
            Assert.Equal("g\th", rows[2].Text);
        }

        [Fact]
        public void ARowThatHasOnlyTheSecondColumn_IsIndentedToIt()
        {
            var rows = Rows(Line("a", 100, 100, 100), Line("b", 400, 100, 100), Line("c", 100, 140, 100), Line("d", 400, 140, 100), Line("only", 400, 180, 100));

            Assert.Equal(324, rows[2].Indent); // 300 pixels of a text that is 400 wide, which is the 432 points
            Assert.Empty(rows[2].TabStops);
        }

        [Fact]
        public void MoreRoomThanUsualBetweenTwoRows_IsABlankLine()
        {
            var rows = Rows(Line("one", 100, 100), Line("two", 100, 140), Line("three", 100, 180), Line("four", 100, 280), Line("five", 100, 320));

            Assert.Equal([false, false, false, true, false], rows.Select(row => row.BlankBefore));
            Assert.Equal("one\ntwo\nthree\n\nfour\nfive", ManuscriptLayout.TextOf(rows));
        }

        [Fact]
        public void TheRowsOfALineOrTwo_NeverHaveABlankLine_BecauseThereIsNoUsualGapToCompareWith()
        {
            Assert.Equal("a\nb", ManuscriptLayout.TextOf(Rows(Line("a", 100, 100), Line("b", 100, 300))));
            Assert.Equal("a", ManuscriptLayout.TextOf(Rows(Line("a", 100, 100))));
        }

        [Fact]
        public void TheTextOfAnIndentedTextWithATable_HasTheTabsInIt()
        {
            var rows = Rows(Line("Title", 100, 100, 200), Line("cat", 100, 140, 100), Line("кіт", 400, 140, 100));

            Assert.Equal("Title\ncat\tкіт", ManuscriptLayout.TextOf(rows));
        }

        [Fact]
        public void TheDocument_HasAParagraphForEachRow_AnEmptyOneForABlankLine_WithTheIndentAndTheTabStops()
        {
            var rows = Rows(
                Line("first", 160, 100, 540), Line("second", 100, 140, 600), Line("third", 100, 180, 600),
                Line("x", 100, 280, 100), Line("y", 400, 280, 100), Line("z", 100, 320, 100), Line("w", 400, 320, 100));

            var document = ManuscriptLayout.ToDocument(rows);

            Assert.Equal(["first", "second", "third", "", "x\ty", "z\tw"], document.Paragraphs.Select(paragraph => paragraph.PlainText)); // 5 rows and the blank line before the 4th
            Assert.Equal(44, document.Paragraphs[0].LeftIndent);
            Assert.Equal(0, document.Paragraphs[1].LeftIndent);
            Assert.True(document.Paragraphs[3].IsBlank);
            Assert.Single(document.Paragraphs[4].TabStops);
            Assert.Equal(TextAlign.Left, document.Paragraphs[0].Align);
        }

        [Fact]
        public void TheRowOfEachLine_IsFoundFromTheRows()
        {
            var rows = Rows(Line("a", 100, 100, 100), Line("b", 400, 100, 100), Line("c", 100, 140, 100));

            Assert.Equal([0, 0, 1], ManuscriptLayout.RowOfLine(rows, 3));
            Assert.Equal([0, 0, 1, -1], ManuscriptLayout.RowOfLine(rows, 4));
        }

        [Fact]
        public void ThePageWidth_KeepsALineOrTwoFromBeingTakenForAWholeText()
        {
            // Two short lines, the second 100 further in: with no page width the text is 300 wide and 100 is a third of it; with a page of 2000 the text is at least 800 wide
            var lines = new[] { Line("short", 100, 100, 100), Line("shorter", 200, 140, 200) };

            var alone = ManuscriptLayout.Rows(lines);
            var onAPage = ManuscriptLayout.Rows(lines, 2000);

            Assert.True(alone[1].Indent > onAPage[1].Indent);
            Assert.Equal(Math.Round(100.0 / 800 * RichDocument.ReferenceWidth / 4) * 4, onAPage[1].Indent);
        }

        [Fact]
        public void ARowNeverGetsAnIndentThatIsNotAMultipleOfFourPoints()
        {
            var lines = new List<TextLine>();
            for (var i = 0; i < 8; i++)
                lines.Add(Line("line", 100 + i * 7, 100 + 40 * i, 300));

            foreach (var row in ManuscriptLayout.Rows(lines))
                Assert.Equal(0, row.Indent % 4);
        }
    }
}
