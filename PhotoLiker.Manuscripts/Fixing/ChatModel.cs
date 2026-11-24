using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Manuscripts.Fixing
{
    /// <summary>
    /// What a language model has worked out for the text it has read so far (the keys and the values of its attention, its "past"): a text that starts many prompts (the instructions) is read once, and
    /// each prompt goes on from this. It is the memory of the model, so it is let go of with <see cref="Dispose"/>.
    /// </summary>
    public sealed class KvCache : IDisposable
    {
        private readonly IDisposableReadOnlyCollection<OrtValue> _outputs;

        internal KvCache(IDisposableReadOnlyCollection<OrtValue> outputs, int length)
        {
            _outputs = outputs;
            Length = length;
        }

        /// <summary>How many tokens the cache has read.</summary>
        public int Length { get; }

        /// <summary>The keys and the values, layer by layer (the first output of the model is its logits, which are not in the cache).</summary>
        internal IEnumerable<OrtValue> Values => _outputs.Skip(1);

        public void Dispose() => _outputs.Dispose();
    }

    /// <summary>
    /// A language model that answers a text word by word (token by token), run on the processor with ONNX Runtime: the ONNX export of a chat model of the Qwen2 family, in the form that the
    /// transformers.js project publishes (inputs <c>input_ids</c>, <c>attention_mask</c>, <c>position_ids</c> and a <c>past_key_values.N.key/value</c> for each layer; outputs the logits and a
    /// <c>present.N.key/value</c> for each layer, float32). The answer is the likeliest token every time (no chance in it), so the same text gets the same answer. One thread at a time.
    /// </summary>
    public sealed class ChatModel : IDisposable
    {
        /// <summary>
        /// How many tokens are given to the model at once when it reads a prompt: its answer has the logits (the chances of the 150 thousand tokens) for each of them, 600 KB a token, which
        /// is much memory for a long prompt.
        /// </summary>
        private const int ChunkSize = 48;

        private const string PastPrefix = "past_key_values.";
        private const string PresentPrefix = "present.";

        private readonly InferenceSession _session;
        private readonly string[] _pastNames;
        private readonly string[] _outputNames;
        private readonly bool _hasPositions;
        private readonly long _heads;
        private readonly long _dimension;

        private ChatModel(InferenceSession session)
        {
            _session = session;
            var inputs = session.InputMetadata;

            // The layers: past_key_values.0.key, past_key_values.0.value, past_key_values.1.key ...
            var layers = inputs.Keys.Where(name => name.StartsWith(PastPrefix, StringComparison.Ordinal) && name.EndsWith(".key", StringComparison.Ordinal)).Count();
            if (layers == 0 || !inputs.ContainsKey("input_ids") || !inputs.ContainsKey("attention_mask"))
                throw new InvalidDataException("The file is not a language model this program can run.");

            Layers = layers;
            _pastNames = [.. Enumerable.Range(0, layers).SelectMany(layer => new[] { $"{PastPrefix}{layer}.key", $"{PastPrefix}{layer}.value" })];
            foreach (var name in _pastNames)
            {
                if (!inputs.TryGetValue(name, out var metadata) || metadata.ElementDataType != TensorElementType.Float || metadata.Dimensions.Length != 4)
                    throw new InvalidDataException("The file is not a language model this program can run (its memory is not float32).");
            }

            _outputNames = ["logits", .. _pastNames.Select(name => PresentPrefix + name[PastPrefix.Length..])];
            _hasPositions = inputs.ContainsKey("position_ids");
            _heads = inputs[_pastNames[0]].Dimensions[1];
            _dimension = inputs[_pastNames[0]].Dimensions[3];
        }

        /// <summary>How many layers the model has.</summary>
        public int Layers { get; }

        /// <summary>Opens the file of a model. <paramref name="threads"/> of 0 is two thirds of the processor's threads: a model that is read from memory for every token is held up by the memory, not by the processor, and runs best on about that many.</summary>
        /// <exception cref="InvalidDataException">The file is not a language model this program can run.</exception>
        public static ChatModel Open(string path, int threads = 0)
        {
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            options.IntraOpNumThreads = threads > 0 ? threads : Math.Max(2, Environment.ProcessorCount * 2 / 3);
            options.EnableCpuMemArena = false; // as for the other models: no memory that a window keeps until the program ends

            var session = new InferenceSession(path, options);
            try
            {
                return new ChatModel(session);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        /// <summary>Reads a text that many prompts start with, once; <see cref="Generate"/> goes on from it.</summary>
        public KvCache Read(IReadOnlyList<int> tokens, CancellationToken cancel = default)
        {
            using var state = new State(null, 0);
            Advance(state, tokens, cancel);
            return state.Detach();
        }

        /// <summary>
        /// The answer to a prompt, as the numbers of its tokens: the prompt is read (after <paramref name="after"/>, when it is given) and the model answers with the likeliest token again and again until it
        /// says one of <paramref name="stop"/> (which is not in the answer) or there are <paramref name="maximum"/> tokens.
        /// </summary>
        /// <param name="after">What the model has read already: the prompt goes on from it, and it is not changed.</param>
        public IReadOnlyList<int> Generate(IReadOnlyList<int> prompt, int maximum, IReadOnlySet<int> stop, KvCache? after = null, CancellationToken cancel = default)
        {
            using var state = new State(after, after?.Length ?? 0);
            var answer = new List<int>();
            var token = Advance(state, prompt, cancel);
            while (answer.Count < maximum && !stop.Contains(token))
            {
                answer.Add(token);
                token = Advance(state, [token], cancel);
            }

            return answer;
        }

        public void Dispose() => _session.Dispose();

        // ── Running ─────────────────────────────────────────────────────────

        /// <summary>Where a prompt is: the memory of what the model has read (it is the outputs of the run before, or a cache that is not ours to let go of).</summary>
        private sealed class State(KvCache? start, int length) : IDisposable
        {
            private IDisposableReadOnlyCollection<OrtValue>? _outputs;

            public int Length { get; set; } = length;

            /// <summary>The memory to go on from: the outputs of the last run, or the cache the prompt started from.</summary>
            public IEnumerable<OrtValue>? Past => _outputs?.Skip(1) ?? start?.Values;

            public void Take(IDisposableReadOnlyCollection<OrtValue> outputs)
            {
                _outputs?.Dispose();
                _outputs = outputs;
            }

            /// <summary>The memory as a cache of its own; this state has nothing of it any more.</summary>
            public KvCache Detach()
            {
                var cache = new KvCache(_outputs ?? throw new InvalidOperationException("Nothing was read."), Length);
                _outputs = null;
                return cache;
            }

            public void Dispose() => _outputs?.Dispose();
        }

        /// <summary>Gives the model tokens to read, a few at a time, and returns the token it would say next.</summary>
        private int Advance(State state, IReadOnlyList<int> tokens, CancellationToken cancel)
        {
            var next = 0;
            for (var start = 0; start < tokens.Count; start += ChunkSize)
            {
                cancel.ThrowIfCancellationRequested();
                var count = Math.Min(ChunkSize, tokens.Count - start);
                var outputs = Run(state, tokens, start, count);
                state.Length += count;
                next = Likeliest(outputs[0], count);
                state.Take(outputs);
            }

            return next;
        }

        private IDisposableReadOnlyCollection<OrtValue> Run(State state, IReadOnlyList<int> tokens, int start, int count)
        {
            var ids = new long[count];
            var positions = new long[count];
            for (var i = 0; i < count; i++)
            {
                ids[i] = tokens[start + i];
                positions[i] = state.Length + i;
            }

            var mask = new long[state.Length + count];
            Array.Fill(mask, 1L);

            var owned = new List<OrtValue>();
            try
            {
                var inputs = new Dictionary<string, OrtValue>
                {
                    ["input_ids"] = Own(owned, OrtValue.CreateTensorValueFromMemory(ids, [1, count])),
                    ["attention_mask"] = Own(owned, OrtValue.CreateTensorValueFromMemory(mask, [1, mask.Length])),
                };
                if (_hasPositions)
                    inputs["position_ids"] = Own(owned, OrtValue.CreateTensorValueFromMemory(positions, [1, count]));

                // The memory of what was read: the outputs of the run before, or nothing (a tensor with no length) for the first
                if (state.Past is { } past)
                {
                    var names = _pastNames.AsEnumerable().GetEnumerator();
                    foreach (var value in past)
                    {
                        names.MoveNext();
                        inputs[names.Current] = value;
                    }
                }
                else
                {
                    foreach (var name in _pastNames)
                        inputs[name] = Own(owned, OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float, [1, _heads, 0, _dimension]));
                }

                using var options = new RunOptions();
                return _session.Run(options, inputs, _outputNames);
            }
            finally
            {
                foreach (var value in owned)
                    value.Dispose();
            }
        }

        private static OrtValue Own(List<OrtValue> owned, OrtValue value)
        {
            owned.Add(value);
            return value;
        }

        /// <summary>The token with the best chance, after the last of <paramref name="count"/> tokens that were read.</summary>
        private static int Likeliest(OrtValue logits, int count)
        {
            var all = logits.GetTensorDataAsSpan<float>();
            var vocabulary = all.Length / count;
            var last = all.Slice((count - 1) * vocabulary, vocabulary);

            var best = 0;
            for (var i = 1; i < last.Length; i++)
            {
                if (last[i] > last[best])
                    best = i;
            }

            return best;
        }
    }
}
