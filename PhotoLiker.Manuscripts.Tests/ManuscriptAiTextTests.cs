using System.Text.Json;
using PhotoLiker.Manuscripts.Cloud;
using PhotoLiker.Manuscripts.Fixing;
using PhotoLiker.PhotoBook;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>
    /// What is done with the text an AI service read from a photo: it becomes a document (a paragraph to a line), the rows that stand for its lines on the page (for the pictures), the kind of writing
    /// "AI service" is kept with a page, and the context of the pages before is a setting of the manuscript.
    /// </summary>
    public sealed class ManuscriptAiTextTests
    {
        // ── The text as a document ──────────────────────────────────────────

        [Fact]
        public void ALineIsAParagraph_ABlankLineIsAnEmptyParagraph_ATabStaysInTheLine()
        {
            var document = ManuscriptText.DocumentOf("Wensday\tpainting\nThursdav\tmusic\n\nthe end");

            Assert.Equal(["Wensday\tpainting", "Thursdav\tmusic", "", "the end"], document.Paragraphs.Select(paragraph => paragraph.PlainText));
            Assert.True(document.Paragraphs[2].IsBlank);
        }

        [Fact]
        public void TheDocument_WrittenAsRtf_IsTheSameTextWhenReadAgain()
        {
            const string text = "Привіт, світе!\n\tз відступом\n\nдругий абзац";

            var rtf = RtfWriter.Write(ManuscriptText.DocumentOf(text));

            Assert.Equal(text, ManuscriptText.PlainOf(rtf));
        }

        [Theory]
        [InlineData("one\r\ntwo", "one,two")]
        [InlineData("one\rtwo", "one,two")]
        [InlineData("", "")]
        public void LineEndsOfAnyKind_AreLines(string text, string expected) =>
            Assert.Equal(expected, string.Join(",", ManuscriptText.DocumentOf(text).Paragraphs.Select(paragraph => paragraph.PlainText)));

        // ── Rows for the pictures ───────────────────────────────────────────

        [Fact]
        public void TheRows_AreALinePerLineThatIsNotBlank_SpreadEvenlyOverThePage()
        {
            var rows = ManuscriptLayout.EvenRows("one\ntwo\nthree\nfour", pageHeight: 800);

            Assert.Equal(["one", "two", "three", "four"], rows.Select(row => row.Text));
            Assert.Equal([100d, 300d, 500d, 700d], rows.Select(row => row.CentreY));
            Assert.All(rows, row => Assert.Equal(0, row.Indent));
        }

        [Fact]
        public void ABlankLine_IsABlankBeforeTheNextRow_AndNotARow()
        {
            var rows = ManuscriptLayout.EvenRows("one\n\n\ntwo\n  \nthree", 600);

            Assert.Equal(["one", "two", "three"], rows.Select(row => row.Text));
            Assert.Equal([false, true, true], rows.Select(row => row.BlankBefore));
        }

        [Fact]
        public void NoText_IsNoRows_AndATextOfBlanksToo()
        {
            Assert.Empty(ManuscriptLayout.EvenRows(string.Empty, 600));
            Assert.Empty(ManuscriptLayout.EvenRows("\n  \n", 600));
        }

        [Fact]
        public void ABlankLineAtTheStart_IsNotABlankBeforeTheFirstRow()
        {
            Assert.False(ManuscriptLayout.EvenRows("\n\nfirst", 100)[0].BlankBefore);
        }

        [Fact]
        public void TheRows_GiveABlankLineTheSameWayAScanDoes()
        {
            // The text of the rows is what the document of the text was made of: the two agree
            const string text = "one\n\ntwo\nthree";

            Assert.Equal(text, ManuscriptLayout.TextOf(ManuscriptLayout.EvenRows(text, 300)));
        }

        // ── The kind of writing, and the context, are kept ──────────────────

        [Fact]
        public void TheKindAiService_IsKeptWithAPage_AndReadBack()
        {
            var page = new ManuscriptPage { File = "a.jpg", Kind = nameof(ScanKind.AiService) };

            var again = JsonSerializer.Deserialize<ManuscriptPage>(JsonSerializer.Serialize(page))!;

            Assert.Equal(ScanKind.AiService, again.GetKind());
        }

        [Theory]
        [InlineData(ScanLanguage.Ukrainian)]
        [InlineData(ScanLanguage.Russian)]
        [InlineData(ScanLanguage.English)]
        [InlineData(ScanLanguage.Multilingual)]
        public void AnAiServiceReadsEveryLanguage(ScanLanguage language)
        {
            Assert.True(ScanLanguages.IsSupported(ScanKind.AiService, language));
            Assert.Contains(language, ScanLanguages.For(ScanKind.AiService));
        }

        [Fact]
        public void TheContextSetting_IsKeptWithTheManuscript_OffUnlessItWasTurnedOn()
        {
            var folder = Path.Combine(Path.GetTempPath(), "photoliker-ctx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Assert.True(ManuscriptFile.TrySave(folder, new ManuscriptSettings { UseContext = true, Pages = [] }, out _));
                Assert.True(ManuscriptFile.Load(folder)!.UseContext);

                Assert.True(ManuscriptFile.TrySave(folder, new ManuscriptSettings { Pages = [] }, out _));
                Assert.NotEqual(true, ManuscriptFile.Load(folder)!.UseContext); // a file from before the setting is off
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        // ── The context the corrector is given ──────────────────────────────

        [Fact]
        public void TheCorrectorsInstructions_GetTheContextAfterThem_AndNothingWithout()
        {
            var plain = LlmCorrector.SystemText(ScanLanguage.Ukrainian);

            Assert.Equal(string.Empty, CloudCorrector.ContextText(null));
            Assert.Equal(string.Empty, CloudCorrector.ContextText("  "));
            var text = CloudCorrector.ContextText("Granny Olha went to Lviv.");
            Assert.StartsWith("\n\nFor reference only", text);
            Assert.Contains("Do not correct it, do not repeat it", text);
            Assert.EndsWith("Granny Olha went to Lviv.", text);
            Assert.DoesNotContain("Granny", plain);
        }
    }
}
