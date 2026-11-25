using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>Stands in for the Internet in the tests of the AI services: it records what was sent and answers with what the test says, in order. Nothing leaves the computer.</summary>
    internal sealed class FakeAiService : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _answers = new();

        /// <summary>The requests that came, in order.</summary>
        public List<Sent> Requests { get; } = [];

        /// <summary>The next request is answered with this status and body.</summary>
        public FakeAiService Answer(HttpStatusCode status, string body, Action<HttpResponseMessage>? tune = null)
        {
            _answers.Enqueue((_, _) =>
            {
                var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                tune?.Invoke(response);
                return Task.FromResult(response);
            });
            return this;
        }

        /// <summary>The next request is answered by this (it can throw, or wait for the token).</summary>
        public FakeAiService Answer(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        {
            _answers.Enqueue(answer);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Sent(request.Method, request.RequestUri!, request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase), body));

            if (_answers.Count == 0)
                throw new InvalidOperationException("The test did not expect another request.");

            return await _answers.Dequeue()(request, cancellationToken);
        }

        /// <summary>A request as it came.</summary>
        public sealed record Sent(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body)
        {
            /// <summary>The body as JSON.</summary>
            public JsonObject Json => (JsonObject)JsonNode.Parse(Body)!;
        }
    }
}
