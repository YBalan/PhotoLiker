using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Manuscripts.Handwriting
{
    /// <summary>
    /// Reads one line of handwriting with a handwriting network (see <see cref="CrnnConverter"/>) that runs on this computer with ONNX Runtime: the line comes in as grey numbers
    /// (<see cref="CrnnConfig.ImageHeight"/> rows, any width, white is 1 and black is -1) and goes out as text, with how sure the network is of it. One thread at a time.
    /// </summary>
    public sealed class HandwritingReader : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string[] _alphabet;
        private readonly bool[]? _allowed;
        private readonly object _gate = new();
        private bool _disposed;

        /// <summary>Opens the network of an ONNX file made by <see cref="CrnnConverter"/> (it says its characters itself).</summary>
        /// <param name="threads">How many threads ONNX Runtime may use for a line; 0 lets it choose.</param>
        /// <param name="allowCharacter">Whether the network may answer with a character (the text it makes is held to them); null: it may answer with any it knows.</param>
        /// <exception cref="InvalidDataException">The file is not a network of this kind.</exception>
        public HandwritingReader(string onnxPath, CrnnConfig? config = null, int threads = 0, Func<string, bool>? allowCharacter = null)
        {
            Config = config ?? new CrnnConfig();

            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // the program has other things to do with the processor
            options.EnableCpuMemArena = false; // as for the other models: no memory that a window keeps until the program ends
            options.EnableMemoryPattern = false;

            _session = new InferenceSession(onnxPath, options);
            if (!_session.ModelMetadata.CustomMetadataMap.TryGetValue(CrnnConverter.CharactersKey, out var json))
                throw new InvalidDataException("The network does not say its characters; it is not one that this program made.");

            _alphabet = JsonSerializer.Deserialize<string[]>(json) ?? throw new InvalidDataException("The characters of the network are not a list.");
            if (allowCharacter is not null)
                _allowed = [.. _alphabet.Select((character, index) => index == 0 || character.Length == 0 || allowCharacter(character))]; // the blank is always an answer
        }

        public CrnnConfig Config { get; }

        /// <summary>The text of a line, and how sure the network is (the mean of how likely the characters it took are, 0 to 1).</summary>
        /// <param name="grey"><see cref="CrnnConfig.ImageHeight"/> rows of <paramref name="width"/> numbers.</param>
        public (string Text, float Confidence) Read(float[] grey, int width)
        {
            var height = Config.ImageHeight;
            if (width < 1 || grey.Length != height * width)
                throw new ArgumentException($"A line of {width} by {height} has {width * height} numbers, not {grey.Length}.", nameof(grey));

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                using var results = _session.Run([NamedOnnxValue.CreateFromTensor("image", new DenseTensor<float>(grey, [1, 1, height, width]))]);
                var output = results.First().AsTensor<float>();
                var dimensions = output.Dimensions;
                if (dimensions.Length != 3 || dimensions[2] != _alphabet.Length)
                    throw new InvalidDataException($"The network gives {string.Join(" by ", dimensions.ToArray())} answers; {_alphabet.Length} for each column of a line were expected.");

                return Decode(output.ToArray(), dimensions[0], _alphabet, _allowed);
            }
        }

        /// <summary>
        /// The text that the answers of the network make: the likeliest answer of each column, a repeat of the same answer in the next column counted once, the blank (answer 0) dropped (CTC).
        /// <paramref name="scores"/> is <paramref name="columns"/> by the number of characters, numbers that are not chances (they are made into chances here). A character that
        /// <paramref name="allowed"/> says no to is never the answer (the likeliest of the others is).
        /// </summary>
        internal static (string Text, float Confidence) Decode(float[] scores, int columns, string[] alphabet, bool[]? allowed = null)
        {
            var classes = alphabet.Length;
            var text = new System.Text.StringBuilder();
            double sure = 0;
            var taken = 0;
            var last = -1;
            for (var t = 0; t < columns; t++)
            {
                var row = scores.AsSpan(t * classes, classes);
                var best = 0;
                for (var c = 1; c < classes; c++)
                {
                    if (allowed is not null && !allowed[c])
                        continue;

                    if (row[c] > row[best])
                        best = c;
                }

                if (best != 0 && best != last && alphabet[best].Length > 0)
                {
                    double total = 0;
                    for (var c = 0; c < classes; c++)
                    {
                        if (allowed is null || allowed[c])
                            total += Math.Exp(row[c] - row[best]);
                    }

                    text.Append(alphabet[best]);
                    sure += 1 / total;
                    taken++;
                }

                last = best;
            }

            return (text.ToString(), taken == 0 ? 0f : (float)(sure / taken));
        }

        /// <summary>Where the ONNX file made from a checkpoint is kept: next to it, with the extension changed.</summary>
        public static string OnnxPathOf(string checkpointPath) => Path.ChangeExtension(checkpointPath, ".onnx");

        /// <summary>
        /// The ONNX file of a downloaded checkpoint, made when it is not there yet or is older than the checkpoint (the file is written under another name and moved into place, so a program
        /// that is closed while it works leaves no half file). Returns its path.
        /// </summary>
        /// <exception cref="InvalidDataException">The checkpoint is not a network of this kind.</exception>
        public static string EnsureOnnx(string checkpointPath, CrnnConfig? config = null)
        {
            var onnxPath = OnnxPathOf(checkpointPath);
            if (File.Exists(onnxPath) && File.GetLastWriteTimeUtc(onnxPath) >= File.GetLastWriteTimeUtc(checkpointPath))
                return onnxPath;

            var bytes = CrnnConverter.ToOnnx(TorchCheckpoint.Read(checkpointPath), config ?? new CrnnConfig());
            var partial = onnxPath + ".part";
            File.WriteAllBytes(partial, bytes);
            File.Move(partial, onnxPath, overwrite: true);
            return onnxPath;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _session.Dispose();
            }
        }
    }
}
