using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>A picture that goes with a turn of a chat (a page to read): the bytes of a JPEG or PNG file, and its media type ("image/jpeg").</summary>
    public sealed record ChatImage(string MediaType, byte[] Data);

    /// <summary>One turn of a chat: what the person (or the program for them) said, or what the AI answered. A turn of the person can have a picture with it.</summary>
    public sealed record ChatTurn(bool FromAssistant, string Text, ChatImage? Image = null);

    /// <summary>
    /// An AI service did not do what was asked: the key was not accepted, a limit was reached, the service is down, it refused, or it could not be reached. The message is English and says which service;
    /// it never has the key in it. <see cref="Status"/> is the HTTP status, null when the service did not answer.
    /// </summary>
    public sealed class AiServiceException : Exception
    {
        public AiServiceException(string message, HttpStatusCode? status = null, Exception? inner = null)
            : base(message, inner) => Status = status;

        public HttpStatusCode? Status { get; }
    }

    /// <summary>
    /// A conversation with an AI service over the Internet, one request and one answer at a time: a system text, the turns of the chat so far, and what comes back is the text the AI wrote. The three
    /// services have three ways to ask (OpenAI: Chat Completions; Gemini: generateContent; Claude: Messages); this class is the only place that knows them, and what is asked is the same for all.
    /// Only the addresses of the services are ever called (the key is not sent anywhere else), the key goes in a header and not in the address, and nothing is logged. A request that fails
    /// because the service is busy is tried again, twice, after a wait it asks for or a short one.
    /// </summary>
    public sealed class CloudChat
    {
        /// <summary>The version of the Messages API that is asked of Claude (the one the sampling settings <c>temperature</c>, <c>top_p</c> and <c>top_k</c> are gone from: none is sent).</summary>
        public const string ClaudeApiVersion = "2026-10-21";

        /// <summary>How long one request may take; an answer that takes longer is given up.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

        internal const string OpenAiChat = "https://api.openai.com/v1/chat/completions";
        internal const string OpenAiModels = "https://api.openai.com/v1/models";
        internal const string GeminiBase = "https://generativelanguage.googleapis.com/v1beta/models";
        internal const string ClaudeMessages = "https://api.anthropic.com/v1/messages";
        internal const string ClaudeModels = "https://api.anthropic.com/v1/models?limit=1000";

        /// <summary>The answer of Claude is capped at this many tokens (a piece of a page is a few hundred; the room is for the thinking that some models do first).</summary>
        internal const int ClaudeMaxTokens = 16000;

        private const int MaximumAttempts = 3;
        private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(15);

        private readonly AiVendor _vendor;
        private readonly string _key;
        private readonly string _model;
        private readonly HttpClient _http;
        private readonly Func<TimeSpan, CancellationToken, Task> _wait;

        /// <param name="vendor">The service.</param>
        /// <param name="apiKey">The key of the person for it.</param>
        /// <param name="model">The model to ask (as the service names it).</param>
        /// <param name="http">What sends the requests (not disposed here); a test gives it a handler that answers instead of the Internet.</param>
        /// <param name="wait">How a wait before a new try is made; null is a real wait (a test gives one that does not wait).</param>
        /// <exception cref="ArgumentException">The key or the model is not one (see <see cref="AiServiceSettings"/>).</exception>
        public CloudChat(AiVendor vendor, string apiKey, string model, HttpClient http, Func<TimeSpan, CancellationToken, Task>? wait = null)
        {
            if (!AiServiceSettings.IsPlausibleKey(apiKey))
                throw new ArgumentException("A key is one line without spaces.", nameof(apiKey));
            if (!AiServiceSettings.IsPlausibleModel(model))
                throw new ArgumentException("A model name has letters, digits and . - _ : / only.", nameof(model));

            _vendor = vendor;
            _key = apiKey;
            _model = model;
            _http = http;
            _wait = wait ?? ((time, cancel) => Task.Delay(time, cancel));
        }

        /// <summary>How long one request may take: <see cref="RequestTimeout"/> (a test makes it short).</summary>
        internal TimeSpan Timeout { get; init; } = RequestTimeout;

        private string ServiceName => AiVendors.Name(_vendor);

        // ── Asking ──────────────────────────────────────────────────────────

        /// <summary>The text the AI wrote for a system text and the turns of a chat (the last is the person's).</summary>
        /// <exception cref="AiServiceException">The service did not give a text.</exception>
        /// <exception cref="OperationCanceledException">The person stopped it.</exception>
        public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken cancel = default)
        {
            var body = await SendAsync(() => ChatRequest(system, turns), cancel).ConfigureAwait(false);
            return Read(body, ReadAnswer);
        }

        /// <summary>
        /// The models the service has for text, as it names them (what can be put in <c>model</c>). It costs nothing, and it is how a key is tried: a key the service does not accept
        /// fails here with the reason the service gives.
        /// </summary>
        /// <exception cref="AiServiceException">The service did not give a list (the key is not accepted, or the key can not list models).</exception>
        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancel = default)
        {
            var body = await SendAsync(ListRequest, cancel).ConfigureAwait(false);
            return Read(body, ReadModels);
        }

        // ── The requests ────────────────────────────────────────────────────

        internal HttpRequestMessage ChatRequest(string system, IReadOnlyList<ChatTurn> turns)
        {
            switch (_vendor)
            {
                case AiVendor.OpenAI:
                {
                    var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
                    foreach (var turn in turns)
                    {
                        // A turn with a picture is a list of parts, the text and the picture (as a data address); one without is the text
                        JsonNode content = turn.Image is { } image
                            ? new JsonArray
                            {
                                new JsonObject { ["type"] = "text", ["text"] = turn.Text },
                                new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data)}", ["detail"] = "high" } },
                            }
                            : JsonValue.Create(turn.Text)!;
                        messages.Add(new JsonObject { ["role"] = turn.FromAssistant ? "assistant" : "user", ["content"] = content });
                    }

                    var request = Post(OpenAiChat, new JsonObject { ["model"] = _model, ["messages"] = messages });
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
                    return request;
                }

                case AiVendor.Gemini:
                {
                    var contents = new JsonArray();
                    foreach (var turn in turns)
                    {
                        var parts = new JsonArray { new JsonObject { ["text"] = turn.Text } };
                        if (turn.Image is { } image)
                            parts.Add(new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = image.MediaType, ["data"] = Convert.ToBase64String(image.Data) } });

                        contents.Add(new JsonObject { ["role"] = turn.FromAssistant ? "model" : "user", ["parts"] = parts });
                    }

                    var body = new JsonObject { ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = system } } }, ["contents"] = contents };
                    var request = Post($"{GeminiBase}/{Uri.EscapeDataString(GeminiModel())}:generateContent", body);
                    request.Headers.Add("x-goog-api-key", _key);
                    return request;
                }

                default:
                {
                    var messages = new JsonArray();
                    foreach (var turn in turns)
                    {
                        // A turn with a picture is a list of blocks, the picture first (as Claude is best asked) and then the text; one without is the text
                        JsonNode content = turn.Image is { } image
                            ? new JsonArray
                            {
                                new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = Convert.ToBase64String(image.Data) } },
                                new JsonObject { ["type"] = "text", ["text"] = turn.Text },
                            }
                            : JsonValue.Create(turn.Text)!;
                        messages.Add(new JsonObject { ["role"] = turn.FromAssistant ? "assistant" : "user", ["content"] = content });
                    }

                    var body = new JsonObject { ["model"] = _model, ["max_tokens"] = ClaudeMaxTokens, ["system"] = system, ["messages"] = messages };
                    var request = Post(ClaudeMessages, body);
                    request.Headers.Add("x-api-key", _key);
                    request.Headers.Add("anthropic-version", ClaudeApiVersion);
                    return request;
                }
            }
        }

        internal HttpRequestMessage ListRequest()
        {
            switch (_vendor)
            {
                case AiVendor.OpenAI:
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, OpenAiModels);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
                    return request;
                }

                case AiVendor.Gemini:
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, GeminiBase + "?pageSize=1000");
                    request.Headers.Add("x-goog-api-key", _key);
                    return request;
                }

                default:
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, ClaudeModels);
                    request.Headers.Add("x-api-key", _key);
                    request.Headers.Add("anthropic-version", ClaudeApiVersion);
                    return request;
                }
            }
        }

        private static HttpRequestMessage Post(string address, JsonObject body) =>
            new(HttpMethod.Post, address) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

        /// <summary>The model as Gemini wants it in the address: without the <c>models/</c> the list of the service puts before it.</summary>
        private string GeminiModel() => _model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? _model["models/".Length..] : _model;

        // ── Sending, and trying again ───────────────────────────────────────

        private async Task<string> SendAsync(Func<HttpRequestMessage> build, CancellationToken cancel)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(Timeout);

                HttpStatusCode status;
                string body;
                TimeSpan? asked;
                try
                {
                    using var request = build();
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                    status = response.StatusCode;
                    asked = response.Headers.RetryAfter is { } after ? after.Delta ?? (after.Date - DateTimeOffset.UtcNow) : null;
                    body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new AiServiceException($"{ServiceName} did not answer in time.");
                }
                catch (HttpRequestException ex)
                {
                    throw new AiServiceException($"{ServiceName} could not be reached: {Scrub(ex.Message)}", null, ex);
                }

                if ((int)status is >= 200 and < 300)
                    return body;

                var wait = asked is { } given && given > TimeSpan.Zero ? given : TimeSpan.FromSeconds(attempt == 1 ? 2 : 6);
                if (IsBusy(status) && attempt < MaximumAttempts && wait <= LongestWait)
                {
                    await _wait(wait, cancel).ConfigureAwait(false);
                    continue;
                }

                throw new AiServiceException(Failure(status, body), status);
            }
        }

        /// <summary>Statuses that mean "not now" and not "no": the service is busy or a limit was reached for the moment.</summary>
        private static bool IsBusy(HttpStatusCode status) =>
            (int)status is 408 or 429 or 500 or 502 or 503 or 504 or 529;

        /// <summary>What went wrong, from the status and what the service wrote (all three put it in <c>error.message</c>).</summary>
        private string Failure(HttpStatusCode status, string body)
        {
            var said = ErrorMessage(body);
            var reason = (int)status == 429 ? "too many requests" : status.ToString();
            return said is null
                ? $"{ServiceName} answered {(int)status} ({reason})."
                : $"{ServiceName} answered {(int)status} ({reason}): {Scrub(said)}";
        }

        private static string? ErrorMessage(string body)
        {
            try
            {
                var text = (JsonNode.Parse(body) as JsonObject)?["error"] is JsonObject error ? Text(error["message"])?.Trim() : null;
                return string.IsNullOrEmpty(text) ? null : text.Length > 400 ? text[..400] + "..." : text;
            }
            catch (JsonException)
            {
                return null; // a page of a proxy, or nothing: the status says what there is to say
            }
        }

        /// <summary>The text with the key taken out: a service may repeat a part of it, a message is shown to the person and may be copied.</summary>
        private string Scrub(string text) => text.Replace(_key, "***", StringComparison.Ordinal);

        // ── The answers ─────────────────────────────────────────────────────

        private string ReadAnswer(string body)
        {
            var root = Parse(body);
            switch (_vendor)
            {
                case AiVendor.OpenAI:
                {
                    var choice = First(root["choices"]);
                    var text = Text(choice?["message"]?["content"]);
                    if (Text(choice?["finish_reason"]) == "length")
                        throw Cut();
                    if (string.IsNullOrWhiteSpace(text))
                        throw new AiServiceException(Text(choice?["message"]?["refusal"]) is { Length: > 0 } refusal ? $"{ServiceName} declined: {refusal}" : $"{ServiceName} gave no text.");

                    return text;
                }

                case AiVendor.Gemini:
                {
                    var candidate = First(root["candidates"]);
                    var text = new StringBuilder();
                    if (candidate?["content"]?["parts"] is JsonArray parts)
                    {
                        foreach (var part in parts)
                        {
                            // A model that thinks first may send the thoughts as parts of their own, marked so
                            if (part?["thought"] is JsonValue thought && thought.TryGetValue<bool>(out var isThought) && isThought)
                                continue;

                            text.Append(Text(part?["text"]));
                        }
                    }

                    var finish = Text(candidate?["finishReason"]);
                    if (finish == "MAX_TOKENS")
                        throw Cut();
                    if (text.ToString().Trim().Length == 0)
                        throw new AiServiceException($"{ServiceName} gave no text ({Text(root["promptFeedback"]?["blockReason"]) ?? finish ?? "empty answer"}).");

                    return text.ToString();
                }

                default:
                {
                    var stop = Text(root["stop_reason"]);
                    if (stop == "refusal")
                        throw new AiServiceException($"{ServiceName} declined to answer ({Text(root["stop_details"]?["category"]) ?? "no reason given"}).");

                    var text = new StringBuilder();
                    if (root["content"] is JsonArray blocks)
                    {
                        foreach (var block in blocks)
                        {
                            if (Text(block?["type"]) == "text")
                                text.Append(Text(block?["text"]));
                        }
                    }

                    if (stop == "max_tokens")
                        throw Cut();
                    if (text.ToString().Trim().Length == 0)
                        throw new AiServiceException($"{ServiceName} gave no text.");

                    return text.ToString();
                }
            }
        }

        private AiServiceException Cut() => new($"{ServiceName} stopped before the end of the answer (it was too long).");

        private IReadOnlyList<string> ReadModels(string body)
        {
            var root = Parse(body);
            var names = new List<string>();
            switch (_vendor)
            {
                case AiVendor.OpenAI:
                    if (root["data"] is JsonArray openAi)
                        names.AddRange(openAi.Select(model => Text(model?["id"])).OfType<string>().Where(IsOpenAiChatModel));
                    break;

                case AiVendor.Gemini:
                    if (root["models"] is JsonArray gemini)
                    {
                        foreach (var model in gemini)
                        {
                            var name = Text(model?["name"]);
                            var methods = model?["supportedGenerationMethods"] as JsonArray;
                            if (name is not null && name.StartsWith("models/gemini", StringComparison.OrdinalIgnoreCase) && methods is not null && methods.Any(method => Text(method) == "generateContent"))
                                names.Add(name["models/".Length..]);
                        }
                    }

                    break;

                default:
                    if (root["data"] is JsonArray claude)
                        names.AddRange(claude.Select(model => Text(model?["id"])).OfType<string>());
                    break;
            }

            return [.. names.Where(AiServiceSettings.IsPlausibleModel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        }

        /// <summary>The models of OpenAI that write text (its list has the ones that draw, speak, listen and embed too).</summary>
        internal static bool IsOpenAiChatModel(string id)
        {
            var chat = id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) || id.StartsWith("chatgpt-", StringComparison.OrdinalIgnoreCase) || (id.Length > 1 && id[0] == 'o' && char.IsAsciiDigit(id[1]));
            return chat && !new[] { "audio", "realtime", "image", "tts", "transcribe", "diarize", "search", "embedding", "moderation", "codex", "instruct" }.Any(word => id.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        private JsonObject Parse(string body)
        {
            try
            {
                return JsonNode.Parse(body) as JsonObject ?? throw new JsonException("not an object");
            }
            catch (JsonException ex)
            {
                throw new AiServiceException($"{ServiceName} sent an answer that could not be read.", null, ex);
            }
        }

        /// <summary>Reads an answer; one that has another shape than the service documents (a value where an object should be) is "could not be read", not a crash.</summary>
        private T Read<T>(string body, Func<string, T> read)
        {
            try
            {
                return read(body);
            }
            catch (InvalidOperationException ex)
            {
                throw new AiServiceException($"{ServiceName} sent an answer that could not be read.", null, ex);
            }
        }

        private static JsonNode? First(JsonNode? array) => array is JsonArray { Count: > 0 } list ? list[0] : null;

        /// <summary>The text of a JSON value, null when it is not a string.</summary>
        private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
