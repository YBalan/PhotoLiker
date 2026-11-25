using System.Net;
using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>
    /// Reading a page with an AI service that can see: how the photo goes to each of the three services (the shape of the request), what the service is told, what it is given of the pages before the
    /// page, and what is made of its answer. The Internet is a stand-in; no photo leaves the computer in a test.
    /// </summary>
    public sealed class CloudReaderTests
    {
        private const string Key = "sk-test-0123456789abcdef";
        private static readonly byte[] Photo = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5];
        private static readonly string PhotoBase64 = Convert.ToBase64String(Photo);

        private static CloudChat Chat(AiVendor vendor, FakeAiService service) =>
            new(vendor, Key, "model-1", new HttpClient(service), (_, _) => Task.CompletedTask);

        private static string OpenAiSays(string text) => "{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(text) + "},\"finish_reason\":\"stop\"}]}";

        private static string GeminiSays(string text) => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}]},\"finishReason\":\"STOP\"}]}";

        private static string ClaudeSays(string text) => "{\"content\":[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}],\"stop_reason\":\"end_turn\"}";

        private static string Says(AiVendor vendor, string text) => vendor switch
        {
            AiVendor.OpenAI => OpenAiSays(text),
            AiVendor.Gemini => GeminiSays(text),
            _ => ClaudeSays(text),
        };

        // ── How the photo goes to each service ──────────────────────────────

        [Fact]
        public async Task OpenAi_GetsThePhotoAsADataAddress_AfterTheText_AndTheInstructionsAsTheSystemMessage()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, OpenAiSays("line one\nline two"));

            var text = await new CloudReader(Chat(AiVendor.OpenAI, service), ScanLanguage.Ukrainian).ReadAsync(Photo);

            Assert.Equal("line one\nline two", text);
            var messages = Assert.Single(service.Requests).Json["messages"]!.AsArray();
            Assert.Equal("system", (string?)messages[0]!["role"]);
            Assert.Equal(CloudReader.SystemText(ScanLanguage.Ukrainian), (string?)messages[0]!["content"]);
            var parts = messages[1]!["content"]!.AsArray();
            Assert.Equal("text", (string?)parts[0]!["type"]);
            Assert.Equal(CloudReader.UserText(null), (string?)parts[0]!["text"]);
            Assert.Equal("image_url", (string?)parts[1]!["type"]);
            Assert.Equal("data:image/jpeg;base64," + PhotoBase64, (string?)parts[1]!["image_url"]!["url"]);
            Assert.Equal("high", (string?)parts[1]!["image_url"]!["detail"]); // handwriting wants the detail
        }

        [Fact]
        public async Task Gemini_GetsThePhotoAsInlineData_AfterTheText()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, GeminiSays("text"));

            await new CloudReader(Chat(AiVendor.Gemini, service), ScanLanguage.English).ReadAsync(Photo);

            var json = Assert.Single(service.Requests).Json;
            Assert.Equal(CloudReader.SystemText(ScanLanguage.English), (string?)json["systemInstruction"]!["parts"]![0]!["text"]);
            var parts = json["contents"]![0]!["parts"]!.AsArray();
            Assert.Equal(CloudReader.UserText(null), (string?)parts[0]!["text"]);
            Assert.Equal("image/jpeg", (string?)parts[1]!["inline_data"]!["mime_type"]);
            Assert.Equal(PhotoBase64, (string?)parts[1]!["inline_data"]!["data"]);
        }

        [Fact]
        public async Task Claude_GetsThePhotoFirst_AsABase64Block_ThenTheText()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("text"));

            await new CloudReader(Chat(AiVendor.Claude, service), ScanLanguage.Russian).ReadAsync(Photo);

            var json = Assert.Single(service.Requests).Json;
            Assert.Equal(CloudReader.SystemText(ScanLanguage.Russian), (string?)json["system"]);
            var blocks = json["messages"]![0]!["content"]!.AsArray();
            Assert.Equal("image", (string?)blocks[0]!["type"]);
            Assert.Equal("base64", (string?)blocks[0]!["source"]!["type"]);
            Assert.Equal("image/jpeg", (string?)blocks[0]!["source"]!["media_type"]);
            Assert.Equal(PhotoBase64, (string?)blocks[0]!["source"]!["data"]);
            Assert.Equal("text", (string?)blocks[1]!["type"]);
            Assert.Equal(CloudReader.UserText(null), (string?)blocks[1]!["text"]);
        }

        [Theory]
        [InlineData(AiVendor.OpenAI)]
        [InlineData(AiVendor.Gemini)]
        [InlineData(AiVendor.Claude)]
        public async Task TheKeyIsNeverInTheBody_AndThePhotoIsSentOnce(AiVendor vendor)
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, Says(vendor, "text"));

            await new CloudReader(Chat(vendor, service), ScanLanguage.Ukrainian).ReadAsync(Photo);

            var body = service.Requests[0].Body;
            Assert.DoesNotContain(Key, body);
            Assert.Equal(1, body.Split(PhotoBase64).Length - 1);
        }

        [Fact]
        public async Task ATurnWithoutAPicture_IsStillPlainText_ForEveryService()
        {
            // The text fix sends no picture, and must not be changed by the room made for one
            foreach (var vendor in AiVendors.All)
            {
                var service = new FakeAiService().Answer(HttpStatusCode.OK, Says(vendor, "ok"));

                await Chat(vendor, service).CompleteAsync("system", [new ChatTurn(false, "just text")]);

                var body = service.Requests[0].Body;
                Assert.DoesNotContain("image", body);
                Assert.DoesNotContain("inline_data", body);
            }
        }

        // ── What the service is told ────────────────────────────────────────

        [Theory]
        [InlineData(ScanLanguage.Ukrainian, "The text is in Ukrainian.")]
        [InlineData(ScanLanguage.Russian, "The text is in Russian.")]
        [InlineData(ScanLanguage.English, "The text is in English.")]
        public void TheInstructions_SayTheLanguageOfThePage(ScanLanguage language, string sentence) =>
            Assert.Contains(sentence, CloudReader.SystemText(language));

        [Fact]
        public void ThePageInMoreThanOneLanguage_IsToldSo()
        {
            var text = CloudReader.SystemText(ScanLanguage.Multilingual);

            Assert.Contains("Ukrainian, Russian or English", text);
            Assert.DoesNotContain("Multilingual", text);
        }

        [Fact]
        public void TheInstructions_SayWhatATranscriptionIs_AndWhatIsNotWanted()
        {
            var text = CloudReader.SystemText(ScanLanguage.Ukrainian);

            Assert.Contains("the way the writer wrote it", text);
            Assert.Contains("a line of text for each line of the page", text);
            Assert.Contains("a blank line", text);
            Assert.Contains("a tab between the columns", text);
            Assert.Contains("Do not correct, modernize, translate or explain", text);
            Assert.Contains("[?]", text);
            Assert.Contains("no code block", text);
        }

        [Fact]
        public void WithoutContext_TheServiceIsAskedToReadThePageOnly()
        {
            Assert.Equal("Read this page.", CloudReader.UserText(null));
            Assert.Equal("Read this page.", CloudReader.UserText("   "));
        }

        [Fact]
        public void WithContext_TheServiceIsGivenThePagesBefore_AndToldNotToWriteThemAgain()
        {
            var text = CloudReader.UserText("Granny Olha went to Lviv.");

            Assert.StartsWith("Read this page.", text);
            Assert.Contains("the pages that come before it", text);
            Assert.Contains("Do not write it again", text);
            Assert.EndsWith("Granny Olha went to Lviv.", text);
        }

        [Fact]
        public async Task TheContext_GoesWithThePhoto_WhenItIsGiven_AndNotWhenItIsNot()
        {
            var with = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("ok"));
            var without = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("ok"));

            await new CloudReader(Chat(AiVendor.Claude, with), ScanLanguage.Ukrainian, "Granny Olha").ReadAsync(Photo);
            await new CloudReader(Chat(AiVendor.Claude, without), ScanLanguage.Ukrainian).ReadAsync(Photo);

            Assert.Contains("Granny Olha", with.Requests[0].Body);
            Assert.DoesNotContain("Granny Olha", without.Requests[0].Body);
            Assert.DoesNotContain("pages that come before", without.Requests[0].Body);
        }

        // ── What is made of the answer ──────────────────────────────────────

        [Theory]
        [InlineData("line one\nline two", "line one\nline two")]
        [InlineData("  line one  \r\nline two  \r\n", "line one\nline two")]
        [InlineData("\n\nline one\n\n\n\nline two\n\n", "line one\n\nline two")]
        [InlineData("```\nline one\nline two\n```", "line one\nline two")]
        [InlineData("```text\nline one\n\nline two\n```", "line one\n\nline two")]
        [InlineData("a\tb\nc\td", "a\tb\nc\td")]
        [InlineData("   ", "")]
        [InlineData("", "")]
        [InlineData("line one\rline two", "line one\nline two")]
        public void TheAnswer_IsTheTextOfThePage_WithoutTheWrappingOfAService(string answer, string expected) => Assert.Equal(expected, CloudReader.Clean(answer));

        [Fact]
        public async Task AnAnswerOfOnlyBlanks_IsAnErrorOfTheService_ThatSaysItGaveNoText()
        {
            // A page with nothing on it, or a service that answered nothing: the person is told which page and which service
            var service = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("  \n  "));

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => new CloudReader(Chat(AiVendor.Claude, service), ScanLanguage.Ukrainian).ReadAsync(Photo));

            Assert.Contains("gave no text", ex.Message);
        }

        [Fact]
        public async Task AnAnswerThatIsOnlyTheWrapping_IsAnEmptyText_AndTheCallerSaysNoTextWasFound()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeSays("```\n```"));

            Assert.Equal(string.Empty, await new CloudReader(Chat(AiVendor.Claude, service), ScanLanguage.Ukrainian).ReadAsync(Photo));
        }

        [Fact]
        public async Task AServiceThatDoesNotAcceptTheKey_FailsTheReading_WithItsWords()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid x-api-key"}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => new CloudReader(Chat(AiVendor.Claude, service), ScanLanguage.Ukrainian).ReadAsync(Photo));

            Assert.Contains("invalid x-api-key", ex.Message);
        }

        [Fact]
        public async Task AServiceThatCannotSee_FailsTheReading_WithItsWords()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.BadRequest, """{"error":{"message":"This model does not support image input."}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => new CloudReader(Chat(AiVendor.OpenAI, service), ScanLanguage.Ukrainian).ReadAsync(Photo));

            Assert.Contains("does not support image input", ex.Message);
        }

        [Fact]
        public async Task StoppingIt_WhileTheServiceReads_IsACancel()
        {
            var service = new FakeAiService().Answer(async (_, cancel) =>
            {
                await Task.Delay(Timeout.Infinite, cancel);
                return new HttpResponseMessage();
            });
            using var stop = new CancellationTokenSource();
            var reading = new CloudReader(Chat(AiVendor.Gemini, service), ScanLanguage.Ukrainian).ReadAsync(Photo, stop.Token);

            stop.CancelAfter(50);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        }
    }
}
