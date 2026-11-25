using PhotoLiker.PhotoBook;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>The context an AI service is given with a page: the end of the text of the pages before it, from the pages that are in the book and have text, nearest last, cut at a line.</summary>
    public sealed class ManuscriptContextTests
    {
        private static ManuscriptPage Page(string? text, bool leftOut = false) => new()
        {
            File = "p.jpg",
            Rtf = text is null ? null : RtfWriter.Write(ManuscriptText.DocumentOf(text)),
            LeftOut = leftOut ? true : null,
        };

        [Fact]
        public void ThePagesBefore_AreGiven_InTheirOrder_ApartByABlankLine()
        {
            List<ManuscriptPage> pages = [Page("first page\nline two"), Page("second page"), Page("the page that is read")];

            Assert.Equal("first page\nline two\n\nsecond page", ManuscriptContext.Before(pages, 2));
        }

        [Fact]
        public void ThePageItself_AndThePagesAfterIt_AreNotContext()
        {
            List<ManuscriptPage> pages = [Page("before"), Page("itself"), Page("after")];

            Assert.Equal("before", ManuscriptContext.Before(pages, 1));
            Assert.Equal(string.Empty, ManuscriptContext.Before(pages, 0));
        }

        [Fact]
        public void APageWithNoText_APageThatIsLeftOut_AndAPageOfOnlyBlanks_AreSkipped_AndDoNotCountAsPages()
        {
            List<ManuscriptPage> pages = [Page("one"), Page("two"), Page("three"), Page(null), Page("   \n  "), Page("not in the book", leftOut: true), Page("here")];

            // Four pages are in between, but only the ones with text count against the limit of three
            Assert.Equal("one\n\ntwo\n\nthree", ManuscriptContext.Before(pages, 6));
        }

        [Fact]
        public void NoMoreThanThreePages_AreGiven_TheNearestOnes()
        {
            List<ManuscriptPage> pages = [Page("one"), Page("two"), Page("three"), Page("four"), Page("five")];

            Assert.Equal("two\n\nthree\n\nfour", ManuscriptContext.Before(pages, 4));
        }

        [Fact]
        public void TooMuchText_IsCutFromTheStart_AtTheStartOfALine()
        {
            var lines = Enumerable.Range(1, 40).Select(i => $"line number {i:00} of the page").ToList(); // 21 characters a line, 40 lines
            List<ManuscriptPage> pages = [Page(string.Join("\n", lines)), Page("here")];

            var context = ManuscriptContext.Before(pages, 1, maximumCharacters: 100);

            Assert.True(context.Length <= 100);
            Assert.StartsWith("line number ", context); // a whole line, not the end of one
            Assert.EndsWith("line number 40 of the page", context); // the end of the page is what is nearest
            Assert.Equal(context.Split('\n').Count(line => line.Length > 0), context.Split('\n').Length); // no broken line
        }

        [Fact]
        public void TheNearestPagesAreKept_WhenTheOnesBeforeThemDoNotFit()
        {
            List<ManuscriptPage> pages = [Page(new string('a', 60)), Page(new string('b', 60)), Page("here")];

            var context = ManuscriptContext.Before(pages, 2, maximumCharacters: 80);

            Assert.EndsWith(new string('b', 60), context);
            Assert.True(context.Length <= 80);
        }

        [Fact]
        public void APageCutAtItsStart_ThatIsOneLongLine_IsCutInsideTheLine_AsThereIsNoOtherPlace()
        {
            List<ManuscriptPage> pages = [Page(new string('x', 500)), Page("here")];

            var context = ManuscriptContext.Before(pages, 1, maximumCharacters: 100);

            Assert.Equal(100, context.Length);
        }

        [Fact]
        public void NoPages_GiveNoContext()
        {
            Assert.Equal(string.Empty, ManuscriptContext.Before([], 0));
            Assert.Equal(string.Empty, ManuscriptContext.Before([Page("a")], 0));
            Assert.Equal(string.Empty, ManuscriptContext.Before([Page("a")], -5));
        }

        [Fact]
        public void AnIndexPastTheEnd_IsTheEnd()
        {
            List<ManuscriptPage> pages = [Page("one"), Page("two")];

            Assert.Equal("one\n\ntwo", ManuscriptContext.Before(pages, 99));
        }

        [Fact]
        public void TheTextOfAPage_HasNothingOfItsLook_AndNoBlankLinesAtTheEnds()
        {
            var page = Page("\n\nfirst\n\n\n\nsecond   \n\n");

            Assert.Equal("first\n\nsecond", ManuscriptContext.TextOf(page));
        }

        [Fact]
        public void TheTextOfAPageThatHasOnlyAPicture_IsNothing()
        {
            var picture = RichPicture.FromFile(OnePixelPng(), 0.4)!;
            var page = new ManuscriptPage { File = "p.jpg", Rtf = RtfWriter.Write(new RichDocument([RichParagraph.OfPicture(picture)])) };

            Assert.Equal(string.Empty, ManuscriptContext.TextOf(page));
        }

        private static byte[] OnePixelPng() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    }
}
