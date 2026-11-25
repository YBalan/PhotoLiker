using System.Net;
using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>
    /// What is asked of each AI service, and how its answer is read: the address, where the key goes, the shape of the turns, and what is made of an answer that is cut off, refused, empty or an error.
    /// The Internet is a stand-in (<see cref="FakeAiService"/>); the real services are not called.
    /// </summary>
    public sealed class CloudChatTests
    {
        private const string Key = "sk-test-0123456789abcdef";
        private static readonly ChatTurn[] Turns = [new(false, "wrong"), new(true, "right"), new(false, "Wensday")];

        private static CloudChat Chat(AiVendor vendor, FakeAiService service, string model = "some-model", List<TimeSpan>? waits = null) =>
            new(vendor, Key, model, new HttpClient(service), (time, _) =>
            {
                waits?.Add(time);
                return Task.CompletedTask;
            });

        private const string OpenAiOk = """{"choices":[{"message":{"role":"assistant","content":"Wednesday"},"finish_reason":"stop"}]}""";
        private const string GeminiOk = """{"candidates":[{"content":{"role":"model","parts":[{"text":"Wednesday"}]},"finishReason":"STOP"}]}""";
        private const string ClaudeOk = """{"type":"message","role":"assistant","content":[{"type":"text","text":"Wednesday"}],"stop_reason":"end_turn"}""";

        // ── OpenAI ──────────────────────────────────────────────────────────

        [Fact]
        public async Task OpenAi_IsAskedWithTheKeyInTheAuthorizationHeader_AndTheTurnsAsMessages()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, OpenAiOk);

            var answer = await Chat(AiVendor.OpenAI, service, "gpt-x").CompleteAsync("be exact", Turns);

            Assert.Equal("Wednesday", answer);
            var sent = Assert.Single(service.Requests);
            Assert.Equal(HttpMethod.Post, sent.Method);
            Assert.Equal("https://api.openai.com/v1/chat/completions", sent.Uri.AbsoluteUri);
            Assert.Equal("Bearer " + Key, sent.Headers["Authorization"]);
            Assert.Equal("gpt-x", (string?)sent.Json["model"]);
            var messages = sent.Json["messages"]!.AsArray();
            Assert.Equal(["system", "user", "assistant", "user"], messages.Select(m => (string?)m!["role"]));
            Assert.Equal(["be exact", "wrong", "right", "Wensday"], messages.Select(m => (string?)m!["content"]));
        }

        [Fact]
        public async Task OpenAi_AnAnswerThatWasCutOff_IsAnError_NotHalfATextToPutInThePage()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, """{"choices":[{"message":{"content":"Wedn"},"finish_reason":"length"}]}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.OpenAI, service).CompleteAsync("s", Turns));

            Assert.Contains("too long", ex.Message);
        }

        [Fact]
        public async Task OpenAi_ARefusal_SaysSo()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, """{"choices":[{"message":{"content":null,"refusal":"I can't help with that."},"finish_reason":"stop"}]}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.OpenAI, service).CompleteAsync("s", Turns));

            Assert.Contains("declined", ex.Message);
            Assert.Contains("I can't help with that.", ex.Message);
        }

        [Theory]
        [InlineData("""{"choices":[]}""")]
        [InlineData("""{"choices":[{"message":{"content":"   "},"finish_reason":"stop"}]}""")]
        [InlineData("""{}""")]
        public async Task OpenAi_NoText_IsAnError(string body)
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, body);

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.OpenAI, service).CompleteAsync("s", Turns));

            Assert.Contains("no text", ex.Message);
        }

        // ── Gemini ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Gemini_IsAskedWithTheKeyInAHeader_NotInTheAddress_AndTheAiTurnsAreTheModel()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, GeminiOk);

            var answer = await Chat(AiVendor.Gemini, service, "gemini-x").CompleteAsync("be exact", Turns);

            Assert.Equal("Wednesday", answer);
            var sent = Assert.Single(service.Requests);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-x:generateContent", sent.Uri.AbsoluteUri);
            Assert.DoesNotContain(Key, sent.Uri.ToString());
            Assert.Equal(Key, sent.Headers["x-goog-api-key"]);
            Assert.Equal("be exact", (string?)sent.Json["systemInstruction"]!["parts"]![0]!["text"]);
            var contents = sent.Json["contents"]!.AsArray();
            Assert.Equal(["user", "model", "user"], contents.Select(c => (string?)c!["role"]));
            Assert.Equal(["wrong", "right", "Wensday"], contents.Select(c => (string?)c!["parts"]![0]!["text"]));
        }

        [Fact]
        public async Task Gemini_AModelNamedAsItsListNamesIt_GoesInTheAddressOnce()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, GeminiOk);

            await Chat(AiVendor.Gemini, service, "models/gemini-x").CompleteAsync("s", Turns);

            Assert.EndsWith("/v1beta/models/gemini-x:generateContent", service.Requests[0].Uri.AbsoluteUri);
        }

        [Fact]
        public async Task Gemini_ThePartsAreJoined_AndTheThoughtsAreLeftOut()
        {
            var body = """{"candidates":[{"content":{"parts":[{"text":"let me think","thought":true},{"text":"Wed"},{"text":"nesday"}]},"finishReason":"STOP"}]}""";
            var service = new FakeAiService().Answer(HttpStatusCode.OK, body);

            Assert.Equal("Wednesday", await Chat(AiVendor.Gemini, service).CompleteAsync("s", Turns));
        }

        [Fact]
        public async Task Gemini_AnAnswerThatWasCutOff_IsAnError()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"Wedn"}]},"finishReason":"MAX_TOKENS"}]}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Gemini, service).CompleteAsync("s", Turns));

            Assert.Contains("too long", ex.Message);
        }

        [Fact]
        public async Task Gemini_ABlockedText_SaysWhy()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Gemini, service).CompleteAsync("s", Turns));

            Assert.Contains("SAFETY", ex.Message);
        }

        // ── Claude ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Claude_IsAskedWithTheKeyAndTheVersion_AndNoSamplingSettings()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, ClaudeOk);

            var answer = await Chat(AiVendor.Claude, service, "claude-x").CompleteAsync("be exact", Turns);

            Assert.Equal("Wednesday", answer);
            var sent = Assert.Single(service.Requests);
            Assert.Equal("https://api.anthropic.com/v1/messages", sent.Uri.AbsoluteUri);
            Assert.Equal(Key, sent.Headers["x-api-key"]);
            Assert.Equal("2026-10-21", sent.Headers["anthropic-version"]);
            Assert.Equal("claude-x", (string?)sent.Json["model"]);
            Assert.Equal(16000, (int?)sent.Json["max_tokens"]);
            Assert.Equal("be exact", (string?)sent.Json["system"]);
            var messages = sent.Json["messages"]!.AsArray();
            Assert.Equal(["user", "assistant", "user"], messages.Select(m => (string?)m!["role"]));
            Assert.Equal(["wrong", "right", "Wensday"], messages.Select(m => (string?)m!["content"]));

            // These three are refused by the Messages API of the version that is asked for
            foreach (var name in new[] { "temperature", "top_p", "top_k" })
                Assert.False(sent.Json.ContainsKey(name), name);
        }

        [Fact]
        public async Task Claude_OnlyTheTextBlocksAreTheAnswer_TheThinkingIsNot()
        {
            var body = """{"content":[{"type":"thinking","thinking":"hm"},{"type":"text","text":"Wed"},{"type":"text","text":"nesday"}],"stop_reason":"end_turn"}""";
            var service = new FakeAiService().Answer(HttpStatusCode.OK, body);

            Assert.Equal("Wednesday", await Chat(AiVendor.Claude, service).CompleteAsync("s", Turns));
        }

        [Fact]
        public async Task Claude_ARefusal_NamesItsCategory()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, """{"content":[],"stop_reason":"refusal","stop_details":{"type":"refusal","category":"bio"}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Claude, service).CompleteAsync("s", Turns));

            Assert.Contains("declined", ex.Message);
            Assert.Contains("bio", ex.Message);
        }

        [Fact]
        public async Task Claude_AnAnswerThatWasCutOff_IsAnError()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, """{"content":[{"type":"text","text":"Wedn"}],"stop_reason":"max_tokens"}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Claude, service).CompleteAsync("s", Turns));

            Assert.Contains("too long", ex.Message);
        }

        // ── Errors and trying again ─────────────────────────────────────────

        [Theory]
        [InlineData(AiVendor.OpenAI)]
        [InlineData(AiVendor.Gemini)]
        [InlineData(AiVendor.Claude)]
        public async Task AKeyThatIsNotAccepted_ShowsTheWordsOfTheService_AndIsNotTriedAgain(AiVendor vendor)
        {
            var service = new FakeAiService().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error"}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(vendor, service).CompleteAsync("s", Turns));

            Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
            Assert.Contains("401", ex.Message);
            Assert.Contains("Incorrect API key provided.", ex.Message);
            Assert.Contains(AiVendors.Name(vendor), ex.Message);
            Assert.Single(service.Requests);
        }

        [Fact]
        public async Task TheKey_IsNeverInAMessage_EvenWhenTheServiceRepeatsIt()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"Incorrect API key provided: {{{Key}}}."}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.OpenAI, service).CompleteAsync("s", Turns));

            Assert.DoesNotContain(Key, ex.Message);
            Assert.Contains("***", ex.Message);
        }

        [Fact]
        public async Task AnErrorThatIsNotJson_IsTheStatusAlone_NotAPageOfHtml()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.BadGateway, "<html><body>Bad gateway from the proxy</body></html>")
                .Answer(HttpStatusCode.BadGateway, "<html></html>").Answer(HttpStatusCode.BadGateway, "<html></html>");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Claude, service).CompleteAsync("s", Turns));

            Assert.Contains("502", ex.Message);
            Assert.DoesNotContain("<html>", ex.Message);
        }

        [Fact]
        public async Task ABusyService_IsTriedAgainAfterAShortWait_AndTheSecondTryCounts()
        {
            var waits = new List<TimeSpan>();
            var service = new FakeAiService().Answer(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"overloaded"}}""").Answer(HttpStatusCode.OK, OpenAiOk);

            var answer = await Chat(AiVendor.OpenAI, service, waits: waits).CompleteAsync("s", Turns);

            Assert.Equal("Wednesday", answer);
            Assert.Equal(2, service.Requests.Count);
            Assert.Equal([TimeSpan.FromSeconds(2)], waits);
        }

        [Fact]
        public async Task AWaitTheServiceAsksFor_IsKept()
        {
            var waits = new List<TimeSpan>();
            var service = new FakeAiService()
                .Answer(HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""", response => response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7)))
                .Answer(HttpStatusCode.OK, ClaudeOk);

            await Chat(AiVendor.Claude, service, waits: waits).CompleteAsync("s", Turns);

            Assert.Equal([TimeSpan.FromSeconds(7)], waits);
        }

        [Fact]
        public async Task AWaitThatIsTooLong_IsNotWaitedFor_TheErrorIsShownAtOnce()
        {
            var waits = new List<TimeSpan>();
            var service = new FakeAiService()
                .Answer(HttpStatusCode.TooManyRequests, """{"error":{"message":"daily limit"}}""", response => response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5)));

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.OpenAI, service, waits: waits).CompleteAsync("s", Turns));

            Assert.Contains("429", ex.Message);
            Assert.Contains("daily limit", ex.Message);
            Assert.Empty(waits);
            Assert.Single(service.Requests);
        }

        [Fact]
        public async Task AServiceThatStaysBusy_IsGivenUpAfterThreeTries()
        {
            var waits = new List<TimeSpan>();
            var busy = """{"error":{"message":"overloaded"}}""";
            var service = new FakeAiService().Answer((HttpStatusCode)529, busy).Answer((HttpStatusCode)529, busy).Answer((HttpStatusCode)529, busy);

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Claude, service, waits: waits).CompleteAsync("s", Turns));

            Assert.Equal(3, service.Requests.Count);
            Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)], waits);
            Assert.Equal((HttpStatusCode)529, ex.Status);
        }

        [Fact]
        public async Task ABadRequest_IsNotTriedAgain()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.BadRequest, """{"error":{"message":"model not found"}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.OpenAI, service).CompleteAsync("s", Turns));

            Assert.Contains("model not found", ex.Message);
            Assert.Single(service.Requests);
        }

        [Fact]
        public async Task NoConnection_IsAnErrorOfTheService_NotACrash()
        {
            var service = new FakeAiService().Answer((_, _) => throw new HttpRequestException("No such host is known."));

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Gemini, service).CompleteAsync("s", Turns));

            Assert.Contains("could not be reached", ex.Message);
            Assert.Contains("No such host", ex.Message);
        }

        [Fact]
        public async Task AServiceThatDoesNotAnswer_IsGivenUpAt_TheTimeout()
        {
            var service = new FakeAiService().Answer(async (_, cancel) =>
            {
                await Task.Delay(Timeout.Infinite, cancel);
                return new HttpResponseMessage();
            });
            var chat = new CloudChat(AiVendor.Claude, Key, "m", new HttpClient(service)) { Timeout = TimeSpan.FromMilliseconds(80) };

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => chat.CompleteAsync("s", Turns));

            Assert.Contains("did not answer", ex.Message);
        }

        [Fact]
        public async Task StoppingIt_IsACancel_NotAnErrorOfTheService()
        {
            var service = new FakeAiService().Answer(async (_, cancel) =>
            {
                await Task.Delay(Timeout.Infinite, cancel);
                return new HttpResponseMessage();
            });
            using var stop = new CancellationTokenSource();
            var asking = Chat(AiVendor.OpenAI, service).CompleteAsync("s", Turns, stop.Token);

            stop.CancelAfter(50);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        [InlineData("""{"choices":[{"message":"a string where an object should be"}]}""")]
        public async Task AnAnswerOfAnotherShape_IsAnErrorThatSaysSo(string body)
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, body);

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.OpenAI, service).CompleteAsync("s", Turns));

            Assert.Contains("could not be read", ex.Message);
        }

        // ── The key and the model that are taken ────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("has space")]
        [InlineData("new\nline")]
        [InlineData("tab\tkey")]
        public void AKeyThatCanNotBeOne_IsNotTaken(string key) =>
            Assert.Throws<ArgumentException>(() => new CloudChat(AiVendor.OpenAI, key, "m", new HttpClient(new FakeAiService())));

        [Theory]
        [InlineData("")]
        [InlineData("a b")]
        [InlineData("-leading")]
        [InlineData("x?y=1")]
        public void AModelThatCanNotBeOne_IsNotTaken(string model) =>
            Assert.Throws<ArgumentException>(() => new CloudChat(AiVendor.OpenAI, Key, model, new HttpClient(new FakeAiService())));

        // ── The list of models, which is how a key is tried ─────────────────

        [Fact]
        public async Task OpenAi_TheList_KeepsTheModelsThatWriteText_AndTheKeyIsInTheHeader()
        {
            var body = """{"data":[{"id":"text-embedding-3-small"},{"id":"gpt-6-luna"},{"id":"gpt-4o-audio-preview"},{"id":"o3"},{"id":"dall-e-3"},{"id":"gpt-6-luna"},{"id":"whisper-1"},{"id":"chatgpt-4o-latest"},{"id":"gpt-image-1"},{"id":"gpt-6.2-sol"}]}""";
            var service = new FakeAiService().Answer(HttpStatusCode.OK, body);

            var models = await Chat(AiVendor.OpenAI, service).ListModelsAsync();

            Assert.Equal(["chatgpt-4o-latest", "gpt-6-luna", "gpt-6.2-sol", "o3"], models);
            var sent = Assert.Single(service.Requests);
            Assert.Equal(HttpMethod.Get, sent.Method);
            Assert.Equal("https://api.openai.com/v1/models", sent.Uri.AbsoluteUri);
            Assert.Equal("Bearer " + Key, sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task Gemini_TheList_KeepsTheModelsThatCanGenerateText_WithoutThePrefix()
        {
            var body = """{"models":[{"name":"models/gemini-3.8-flash","supportedGenerationMethods":["generateContent","countTokens"]},{"name":"models/embedding-001","supportedGenerationMethods":["embedContent"]},{"name":"models/gemini-embedding-x","supportedGenerationMethods":["embedContent"]},{"name":"models/gemini-3.5-flash-lite","supportedGenerationMethods":["generateContent"]},{"name":"models/imagen-4","supportedGenerationMethods":["predict"]}]}""";
            var service = new FakeAiService().Answer(HttpStatusCode.OK, body);

            var models = await Chat(AiVendor.Gemini, service).ListModelsAsync();

            Assert.Equal(["gemini-3.5-flash-lite", "gemini-3.8-flash"], models);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000", service.Requests[0].Uri.AbsoluteUri);
            Assert.Equal(Key, service.Requests[0].Headers["x-goog-api-key"]);
        }

        [Fact]
        public async Task Claude_TheList_IsItsIds_AskedWithTheVersion()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.OK, """{"data":[{"id":"claude-sonnet-5-5"},{"id":"claude-opus-5-5"}],"has_more":false}""");

            var models = await Chat(AiVendor.Claude, service).ListModelsAsync();

            Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5"], models);
            Assert.Equal("https://api.anthropic.com/v1/models?limit=1000", service.Requests[0].Uri.AbsoluteUri);
            Assert.Equal(Key, service.Requests[0].Headers["x-api-key"]);
            Assert.Equal("2026-10-21", service.Requests[0].Headers["anthropic-version"]);
        }

        [Fact]
        public async Task TheList_OfAKeyThatIsNotAccepted_IsTheError()
        {
            var service = new FakeAiService().Answer(HttpStatusCode.Forbidden, """{"error":{"message":"API key not valid. Please pass a valid API key."}}""");

            var ex = await Assert.ThrowsAsync<AiServiceException>(() => Chat(AiVendor.Gemini, service).ListModelsAsync());

            Assert.Contains("403", ex.Message);
            Assert.Contains("API key not valid", ex.Message);
        }

        [Theory]
        [InlineData("gpt-6-luna", true)]
        [InlineData("o4-mini", true)]
        [InlineData("chatgpt-4o-latest", true)]
        [InlineData("gpt-4o-realtime-preview", false)]
        [InlineData("gpt-4o-mini-tts", false)]
        [InlineData("gpt-4o-transcribe", false)]
        [InlineData("gpt-5-codex", false)]
        [InlineData("gpt-4o-search-preview", false)]
        [InlineData("gpt-3.5-turbo-instruct", false)]
        [InlineData("gpt-4o-transcribe-diarize", false)]
        [InlineData("gpt-image-1", false)]
        [InlineData("gpt-4o-audio-preview", false)]
        [InlineData("chatgpt-image-latest", false)]
        [InlineData("omni-moderation-latest", false)]
        [InlineData("o", false)]
        [InlineData("davinci-002", false)]
        public void OpenAiModels_ThatWriteText_AreTold(string id, bool writes) => Assert.Equal(writes, CloudChat.IsOpenAiChatModel(id));
    }
}
