using System.Net;
using PhotoLiker.Manuscripts.Cloud;
using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>What an AI service is told when it corrects a text, and what is made of what it answers; the service is a stand-in.</summary>
    public sealed class CloudCorrectorTests
    {
        private static CloudCorrector Corrector(FakeAiService service, AiVendor vendor = AiVendor.Claude, ScanLanguage language = ScanLanguage.English) =>
            new(new CloudChat(vendor, "key-1", "model-1", new HttpClient(service), (_, _) => Task.CompletedTask), language);

        private static string ClaudeSays(string text) => "{\"content\":[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}],\"stop_reason\":\"end_turn\"}";

        [Fact]
        public void TheService_IsToldWhatAScannerGetsWrong_ShownTheExamplesOfTheLanguage_AndGivenTheText()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("Wednesday"));

            var answer = Corrector(service, language: ScanLanguage.Ukrainian).Correct("Wensday", CancellationToken.None);

            Assert.Equal("Wednesday", answer);
            var json = Assert.Single(service.Requests).Json;
            Assert.Equal(LlmCorrector.SystemText(ScanLanguage.Ukrainian), (string?)json["system"]);
            var messages = json["messages"]!.AsArray();
            var examples = LlmCorrector.Examples(ScanLanguage.Ukrainian);
            Assert.Equal(examples.Count * 2 + 1, messages.Count);
            Assert.Equal(examples[0].Wrong, (string?)messages[0]!["content"]);
            Assert.Equal("assistant", (string?)messages[1]!["role"]);
            Assert.Equal(examples[0].Right, (string?)messages[1]!["content"]);
            Assert.Equal("Wensday", (string?)messages[^1]!["content"]);
            Assert.Equal("user", (string?)messages[^1]!["role"]);
        }

        [Fact]
        public void TheAnswer_IsTrimmed()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("\n  Wednesday  \n"));

            Assert.Equal("Wednesday", Corrector(service).Correct("Wensday", CancellationToken.None));
        }

        [Theory]
        [InlineData("```\nWednesday\n```", "Wednesday")]
        [InlineData("```text\nWednesday\nThursday\n```", "Wednesday\nThursday")]
        [InlineData("```plaintext\r\nWednesday\r\n```", "Wednesday")]
        [InlineData("```Wednesday```", "Wednesday")]
        [InlineData("Wednesday", "Wednesday")]
        [InlineData("Use ``` to quote", "Use ``` to quote")]
        [InlineData("```not closed\nWednesday", "```not closed\nWednesday")]
        [InlineData("```", "```")]
        [InlineData("``````", "")]
        [InlineData("```\nПривіт світе\n```", "Привіт світе")]
        public void ATextInACodeBlock_IsTheTextAlone(string answer, string expected) => Assert.Equal(expected, CloudCorrector.WithoutFence(answer));

        [Fact]
        public void EachPiece_IsASeparateRequest_AndTheEditsAreTheChangedWords()
        {
            var service = new FakeAiService()
                .Answer(HttpStatusCode.OK, ClaudeSays("Wednesday painting\nThursday music"))
                .Answer(HttpStatusCode.OK, ClaudeSays("the library was shut"));
            var text = "Wensday painting\nThursdav music\n\nthe lbrary was shut";

            var edits = TextFixer.Fix(text, Corrector(service), cancel: CancellationToken.None);

            Assert.Equal(2, service.Requests.Count);
            Assert.Equal("Wensday painting\nThursdav music", (string?)service.Requests[0].Json["messages"]!.AsArray()[^1]!["content"]);
            Assert.Equal("the lbrary was shut", (string?)service.Requests[1].Json["messages"]!.AsArray()[^1]!["content"]);
            Assert.Equal(["Wednesday", "Thursday", "library"], edits.Select(edit => edit.Replacement));
        }

        [Fact]
        public void AServiceThatRewritesTooMuch_ChangesNothingItShouldNot()
        {
            // The guard of TextFixer works on what a cloud service says as it does for the model on this computer
            var service = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("Here is the corrected text:\nCompletely different words entirely"));

            var edits = TextFixer.Fix("Wensday painting", Corrector(service), cancel: CancellationToken.None);

            Assert.Empty(edits);
        }

        [Fact]
        public void AServiceThatFails_FailsTheCorrection_WithItsWords()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid x-api-key"}}""");

            var ex = Assert.Throws<AiServiceException>(() => TextFixer.Fix("Wensday", Corrector(service), cancel: CancellationToken.None));

            Assert.Contains("invalid x-api-key", ex.Message);
        }

        [Fact]
        public void StoppingIt_StopsTheNextPiece_AndNoRequestIsMade()
        {
            var service = new FakeAiService();
            using var stop = new CancellationTokenSource();
            stop.Cancel();

            Assert.Throws<OperationCanceledException>(() => TextFixer.Fix("Wensday", Corrector(service), cancel: stop.Token));

            Assert.Empty(service.Requests);
        }

        [Fact]
        public void StoppingIt_WhileTheServiceThinks_StopsAtOnce()
        {
            var service = new FakeAiService().Answer(async (_, cancel) =>
            {
                await Task.Delay(Timeout.Infinite, cancel);
                return new HttpResponseMessage();
            });
            using var stop = new CancellationTokenSource();
            stop.CancelAfter(50);

            Assert.ThrowsAny<OperationCanceledException>(() => Corrector(service).Correct("Wensday", stop.Token));
        }
    }
}
