using System.Net;
using System.Text.Json;
using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>
    /// Reading a page with an AI service when the lines are marked on the photo with numbered boxes: what the service is told, and what is made of the answers it gives (and of those it should not
    /// give): JSON with a number and a text for each box, in a block of code or with words round it, in another order, with numbers as text, plain text, an answer that was cut off. The text of the
    /// page is always kept; the boxes only when the answer says what is in boxes that are on the photo. The Internet is a stand-in.
    /// </summary>
    public sealed class MarkedReadingTests
    {
        private static readonly byte[] Photo = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

        private static string Lines(params (int Box, string Text)[] entries) =>
            "{\"lines\":[" + string.Join(",", entries.Select(entry => "{\"box\":" + entry.Box + ",\"text\":" + JsonSerializer.Serialize(entry.Text) + "}")) + "]}";

        // ── What the service is told ────────────────────────────────────────

        [Fact]
        public void TheInstructionsForMarkedBoxes_AskForJson_WithTheNumbersOfTheBoxes_AndKeepTheRulesOfTheTranscription()
        {
            var plain = CloudReader.SystemText(ScanLanguage.Ukrainian);
            var marked = CloudReader.SystemText(ScanLanguage.Ukrainian, marked: true);

            Assert.NotEqual(plain, marked);
            Assert.Contains("JSON", marked);
            Assert.Contains("red box", marked);
            Assert.Contains("\"box\":1", marked);
            Assert.Contains("is left out", marked); // a box with nothing in it
            Assert.StartsWith(plain[..plain.IndexOf("Answer with the text of the page only", StringComparison.Ordinal)], marked); // the way the text is written down does not depend on the boxes
            Assert.Contains("Ukrainian", marked);
            Assert.Contains("[?]", marked);
            Assert.DoesNotContain("Answer with the text of the page only", marked);
            Assert.DoesNotContain("coordinates", marked, StringComparison.OrdinalIgnoreCase); // the service is not asked where anything is
        }

        [Fact]
        public void TheTextThatGoesWithThePhoto_SaysHowManyBoxesThereAre_AndKeepsTheContext()
        {
            Assert.Equal("Read this page.", CloudReader.UserText(null));
            Assert.Equal("Read this page. 12 boxes are marked on it, numbered from 1 to 12.", CloudReader.UserText(null, 12));
            Assert.StartsWith("Read this page. 3 boxes are marked on it", CloudReader.UserText("earlier text", 3));
            Assert.EndsWith("earlier text", CloudReader.UserText("earlier text", 3));
        }

        [Fact]
        public async Task ReadMarkedAsync_SendsTheInstructionsForBoxes_AndGivesTheTextOfEachBox()
        {
            var answer = Lines((1, "first line"), (2, "second line"));
            var service = new FakeAiService().Answer(HttpStatusCode.OK, "{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(answer) + "}],\"stop_reason\":\"end_turn\"}");
            var chat = new CloudChat(AiVendor.Claude, "sk-test-0123456789abcdef", "model-1", new HttpClient(service), (_, _) => Task.CompletedTask);

            var reading = await new CloudReader(chat, ScanLanguage.English).ReadMarkedAsync(Photo, 2);

            Assert.Equal("first line\nsecond line", reading.Text);
            Assert.Equal([new MarkedLine(1, "first line"), new MarkedLine(2, "second line")], reading.Lines);
            var json = Assert.Single(service.Requests).Json;
            Assert.Equal(CloudReader.SystemText(ScanLanguage.English, marked: true), (string?)json["system"]);
            Assert.Equal(CloudReader.UserText(null, 2), (string?)json["messages"]![0]!["content"]![1]!["text"]);
        }

        // ── What is made of the answers ─────────────────────────────────────

        [Fact]
        public void TheEntries_AreGivenInTheOrderOfTheNumbers_WhateverOrderTheServiceWroteThem()
        {
            var reading = MarkedAnswer.Parse(Lines((3, "c"), (1, "a"), (2, "b")), 3);

            Assert.Equal("a\nb\nc", reading.Text);
            Assert.Equal([1, 2, 3], reading.Lines!.Select(line => line.Box).ToArray());
        }

        [Fact]
        public void ABoxThatIsNotInTheAnswer_HasNoText_AndANumberThatIsNotOnThePhotoIsLeftOut()
        {
            var reading = MarkedAnswer.Parse(Lines((1, "a"), (4, "d"), (7, "far away"), (0, "zero")), 4);

            Assert.Equal([1, 4], reading.Lines!.Select(line => line.Box).ToArray());
            Assert.Equal("a\nd", reading.Text);
        }

        [Fact]
        public void TwoEntriesForOneBox_GiveTheFirst_AndABoxWithNoTextIsNotALine()
        {
            var reading = MarkedAnswer.Parse(Lines((1, "first"), (1, "again"), (2, ""), (3, "   "), (4, "last")), 4);

            Assert.Equal(["first", "last"], reading.Lines!.Select(line => line.Text).ToArray());
        }

        [Fact]
        public void TheTextOfABox_IsOneLine_WithItsTabsAndTheSpacesAtItsStart()
        {
            var reading = MarkedAnswer.Parse(Lines((1, "one\ntwo"), (2, "   indented\tcolumn")), 2);

            Assert.Equal("one two", reading.Lines![0].Text);
            Assert.Equal("   indented\tcolumn", reading.Lines[1].Text);
        }

        [Fact]
        public void TheJson_MayBeInACodeBlock_OrHaveWordsRoundIt()
        {
            var json = Lines((1, "a"), (2, "b"));

            foreach (var answer in new[] { "```json\n" + json + "\n```", "Here is the page:\n" + json + "\nHope that helps.", "  " + json + "  " })
            {
                var reading = MarkedAnswer.Parse(answer, 2);
                Assert.Equal("a\nb", reading.Text);
                Assert.Equal(2, reading.Lines!.Count);
            }
        }

        [Fact]
        public void TheEntriesMayBeTheWholeAnswer_AsAnArray_TheNumbersTextsAndTheNamesInAnyCase()
        {
            var array = MarkedAnswer.Parse("[{\"box\":1,\"text\":\"a\"},{\"box\":\"2\",\"text\":\"b\"}]", 2);
            var names = MarkedAnswer.Parse("{\"Lines\":[{\"Number\":1,\"Text\":\"a\"},{\"N\":2,\"TEXT\":\"b\"}]}", 2);

            Assert.Equal("a\nb", array.Text);
            Assert.Equal("a\nb", names.Text);
            Assert.Equal(2, array.Lines!.Count);
            Assert.Equal(2, names.Lines!.Count);
        }

        [Fact]
        public void AnAnswerThatIsPlainText_IsTheTextOfThePage_WithNoBoxes()
        {
            var reading = MarkedAnswer.Parse("```\nfirst line\nsecond [?] line\n```", 5);

            Assert.Equal("first line\nsecond [?] line", reading.Text);
            Assert.Null(reading.Lines);
        }

        [Theory]
        [InlineData("[?]")]
        [InlineData("[1]")]
        [InlineData("see [3] and [?] there")]
        [InlineData("{ not json }")]
        public void BracketsThatAreWordsOfThePage_AreNotTakenForJson(string page)
        {
            var reading = MarkedAnswer.Parse(page, 3);

            Assert.Equal(page, reading.Text);
            Assert.Null(reading.Lines);
        }

        [Fact]
        public void AnAnswerThatWasCutOffInTheMiddle_GivesTheEntriesThatAreWhole()
        {
            var cut = "{\"lines\":[{\"box\":1,\"text\":\"first\"},{\"box\":2,\"text\":\"second \\\"quoted\\\"\"},{\"box\":3,\"text\":\"thi";

            var reading = MarkedAnswer.Parse(cut, 3);

            Assert.Equal("first\nsecond \"quoted\"", reading.Text);
            Assert.Equal([1, 2], reading.Lines!.Select(line => line.Box).ToArray());
        }

        [Fact]
        public void TextsThatHaveNoNumberOfABoxOnThePhoto_AreTheTextOfThePage_WithNoBoxes()
        {
            // The numbers are not the ones that were asked for (they start at 0, or are of another photo): the text is all that can be used
            var reading = MarkedAnswer.Parse(Lines((8, "a"), (9, "b")), 3);

            Assert.Equal("a\nb", reading.Text);
            Assert.Null(reading.Lines);
        }

        [Fact]
        public void AnAnswerWithNoLines_IsNoText_AndNoBoxes_ButItIsAnAnswer()
        {
            var empty = MarkedAnswer.Parse("{\"lines\":[]}", 3);
            var blanks = MarkedAnswer.Parse(Lines((1, ""), (2, "  ")), 3);

            Assert.Equal(string.Empty, empty.Text);
            Assert.Empty(empty.Lines!);
            Assert.Equal(string.Empty, blanks.Text);
            Assert.Empty(blanks.Lines!);
        }
    }
}
