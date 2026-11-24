using System.Drawing;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.PhotoBook;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>Where the pictures of a page go in its text.</summary>
    public sealed class PicturePlacementTests
    {
        private static PagePicture Picture(float left, float top, float right, float bottom) =>
            new([new PointF(left, top), new PointF(right, top), new PointF(right, bottom), new PointF(left, bottom)]);

        private static LayoutRow Row(double centreY) => new([0], "text", 0, [], false, centreY);

        private static RichParagraph Para(string text) => new(TextAlign.Left, [new RichRun(text, default)]);

        private static RichParagraph Pic(string name) => RichParagraph.OfPicture(new RichPicture([1], true, 40, 30, 0.5), TextAlign.Center) with { LeftIndent = name.Length }; // the length of the name tells them apart

        private static string Show(RichDocument document) =>
            string.Join(",", document.Paragraphs.Select(paragraph => paragraph.Picture is not null ? "P" + paragraph.LeftIndent : paragraph.PlainText));

        [Fact]
        public void APictureComesBeforeTheFirstRowThatIsBelowItsMiddle()
        {
            var rows = new[] { Row(100), Row(200), Row(300) };

            Assert.Equal(0, PicturePlacement.RowBefore(Picture(0, 0, 100, 40), rows)); // its middle is at 20: above everything
            Assert.Equal(1, PicturePlacement.RowBefore(Picture(0, 120, 100, 180), rows)); // 150: between the first and the second
            Assert.Equal(2, PicturePlacement.RowBefore(Picture(0, 200, 100, 220), rows)); // 210
            Assert.Equal(3, PicturePlacement.RowBefore(Picture(0, 500, 100, 600), rows)); // below all: after the last
            Assert.Equal(0, PicturePlacement.RowBefore(Picture(0, 0, 100, 600), []));
        }

        [Theory]
        [InlineData(0, 100, PictureThird.Left)]
        [InlineData(300, 400, PictureThird.Middle)]
        [InlineData(400, 600, PictureThird.Middle)]
        [InlineData(700, 900, PictureThird.Right)]
        public void APictureIsAtTheLeftTheMiddleOrTheRight_ByTheThirdOfThePageItsMiddleIsIn(float left, float right, PictureThird expected)
        {
            var align = PicturePlacement.AlignOf(Picture(left, 0, right, 50), 1000);

            Assert.Equal(expected switch { PictureThird.Left => TextAlign.Left, PictureThird.Middle => TextAlign.Center, _ => TextAlign.Right }, align);
        }

        public enum PictureThird
        {
            Left,
            Middle,
            Right,
        }

        [Fact]
        public void APictureIsAsWideInTheTextAsItIsOnThePage_ButNeitherTinyNorMoreThanTheText()
        {
            Assert.Equal(0.4, PicturePlacement.ShareOf(Picture(100, 0, 500, 50), 1000), 6);
            Assert.Equal(0.15, PicturePlacement.ShareOf(Picture(100, 0, 150, 50), 1000), 6);
            Assert.Equal(1.0, PicturePlacement.ShareOf(Picture(0, 0, 1500, 50), 1000), 6);
            Assert.Equal(0.5, PicturePlacement.ShareOf(Picture(0, 0, 100, 50), 0), 6);
        }

        [Fact]
        public void ThePicturesGoBeforeTheRowsTheyAreBefore_AboveTheFirstAndAfterTheLast()
        {
            var document = new RichDocument([Para("one"), Para("two"), Para("three")]);

            var result = PicturePlacement.Insert(document, [(Pic("a"), 0), (Pic("bb"), 2), (Pic("ccc"), 3)], 3);

            Assert.Equal("P1,one,two,P2,three,P3", Show(result));
        }

        [Fact]
        public void TheBlankLineBetweenParagraphs_StaysBeforeThePictureThatIsBeforeTheNextRow()
        {
            var document = new RichDocument([Para("one"), Para("two"), new RichParagraph(TextAlign.Left, []), Para("three")]);

            var result = PicturePlacement.Insert(document, [(Pic("a"), 2)], 3);

            Assert.Equal("one,two,,P1,three", Show(result));
        }

        [Fact]
        public void SeveralPicturesBeforeTheSameRow_KeepTheirOrder_AlsoAfterTheLastRow()
        {
            var document = new RichDocument([Para("one"), Para("two")]);

            var result = PicturePlacement.Insert(document, [(Pic("a"), 1), (Pic("bb"), 1), (Pic("ccc"), 2), (Pic("dddd"), 2)], 2);

            Assert.Equal("one,P1,P2,two,P3,P4", Show(result));
        }

        [Fact]
        public void PicturesThatWereInTheTextAreTakenOutFirst_SoFindingThemAgainDoesNotPutThemInTwice()
        {
            var document = new RichDocument([Pic("old"), Para("one"), Pic("older"), Para("two")]);

            var result = PicturePlacement.Insert(document, [(Pic("n"), 1)], 2);

            Assert.Equal("one,P1,two", Show(result));
        }

        [Fact]
        public void WhenTheTextIsNotTheRowsAnyMore_ARowIsTheParagraphAsFarDownAsTheRowIsDownTheRows()
        {
            // Ten rows were joined into four paragraphs; the picture was before the 6th row of ten, which is as far down as the 2nd paragraph of four
            var document = new RichDocument([Para("one"), Para("two"), Para("three"), Para("four")]);

            var result = PicturePlacement.Insert(document, [(Pic("a"), 5)], 10);

            Assert.Equal("one,two,P1,three,four", Show(result));
        }

        [Fact]
        public void APageWithNoText_HasItsPicturesInTheirOrder()
        {
            var result = PicturePlacement.Insert(RichDocument.Empty, [(Pic("a"), 0), (Pic("bb"), 0)], 0);

            Assert.Equal("P1,P2", Show(result));
        }

        [Fact]
        public void NoPictures_TheTextAsItWas_WithoutItsPictures()
        {
            var document = new RichDocument([Para("one"), Pic("x"), Para("two")]);

            Assert.Equal("one,two", Show(PicturePlacement.Insert(document, [], 2)));
        }
    }
}
