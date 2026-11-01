using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Search
{
    /// <summary>The text half of a CLIP-like model: a sentence becomes a vector that is close to the vectors of photos that show it.</summary>
    public sealed class ClipTextEncoder : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly ITextTokenizer _tokenizer;
        private readonly string _input;
        private readonly bool _int32Ids; // some exports take 32-bit ids, others 64-bit
        private readonly string _output;
        private readonly bool _wantsMask;

        public ClipTextEncoder(string modelPath, ITextTokenizer tokenizer)
        {
            _tokenizer = tokenizer;
            using var options = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            _session = new InferenceSession(modelPath, options);

            _input = _session.InputMetadata.ContainsKey("input_ids") ? "input_ids"
                : _session.InputMetadata.ContainsKey("text") ? "text"
                : _session.InputMetadata.Keys.First();
            _int32Ids = _session.InputMetadata[_input].ElementType == typeof(int);
            _output = _session.OutputMetadata.ContainsKey("text_embeds") ? "text_embeds"
                : _session.OutputMetadata.ContainsKey("embedding") ? "embedding"
                : _session.OutputMetadata.Keys.First();
            _wantsMask = _session.InputMetadata.ContainsKey("attention_mask");
        }

        public float[] Encode(string text)
        {
            var ids = _tokenizer.Encode(text);
            var inputs = new List<NamedOnnxValue>
            {
                _int32Ids
                    ? NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<int>(ids.Select(id => (int)id).ToArray(), [1, ids.Length]))
                    : NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<long>(ids, [1, ids.Length])),
            };
            if (_wantsMask)
            {
                var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
                inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [1, ids.Length])));
            }

            lock (_session) // one query at a time is plenty
            {
                using var results = _session.Run(inputs);
                var vector = results.First(r => r.Name == _output).AsTensor<float>().ToArray();
                VectorMath.Normalize(vector);
                return vector;
            }
        }

        public void Dispose()
        {
            lock (_session) // not while a query is being answered
                _session.Dispose();
        }
    }
}
