namespace PhotoLiker.Manuscripts.Handwriting
{
    /// <summary>
    /// The shape of the handwriting networks: a CNN of 3 by 3 convolutions (each with a batch normalization and a leaky ReLU, some followed by a 2 by 2 max pool), then a bidirectional LSTM of
    /// several layers reading the picture column by column, then a linear layer that gives a chance for each character for each column (the architecture of Puigcerver, 2017, which PyLaia and
    /// Transkribus use). The same for every model of the Polyscriptor project; only the sizes differ.
    /// </summary>
    public sealed record CrnnConfig
    {
        /// <summary>The height a line is read at; the width is whatever the line is at that height.</summary>
        public int ImageHeight { get; init; } = 128;

        /// <summary>The number of channels of each convolution.</summary>
        public int[] Filters { get; init; } = [12, 24, 48, 48];

        /// <summary>For each convolution, whether a 2 by 2 max pool follows it.</summary>
        public bool[] Pools { get; init; } = [true, true, false, true];

        public int Hidden { get; init; } = 256;

        public int Layers { get; init; } = 3;

        /// <summary>How many times the picture gets narrower on the way through the convolutions: a line is read in columns that are this many pixels wide.</summary>
        public int Stride => 1 << Pools.Count(pool => pool);
    }

    /// <summary>
    /// Turns the weights of a handwriting network (a PyTorch checkpoint) into the same network as an ONNX file, which ONNX Runtime runs. The numbers are the model's, only moved: each batch normalization
    /// is folded into the convolution before it (what it does when a network runs, not when it is trained), and the gates of each LSTM are put in the order ONNX has them (input, output, forget, cell)
    /// instead of the order of PyTorch (input, forget, cell, output).
    /// </summary>
    public static class CrnnConverter
    {
        private const float BatchNormEpsilon = 1e-5f;
        private const float LeakySlope = 0.2f;

        /// <summary>The network of the weights as the bytes of an ONNX file: it takes a picture of 1 by 1 by <see cref="CrnnConfig.ImageHeight"/> by any width and gives one number for each character for each column (frames by 1 by characters).</summary>
        /// <summary>The name of the note in the file that holds the characters of the network, as a JSON list: the character of answer 0 (the blank of the CTC, which is empty), of answer 1, and so on.</summary>
        public const string CharactersKey = "photoliker.characters";

        /// <param name="characters">The character of each answer the network gives; the checkpoint's own list when this is null. The characters go into the file with the network.</param>
        /// <exception cref="InvalidDataException">A weight is not there, or has a size the network does not, or the characters are not known.</exception>
        public static byte[] ToOnnx(TorchCheckpoint checkpoint, CrnnConfig config, IReadOnlyDictionary<int, string>? characters = null)
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            ArgumentNullException.ThrowIfNull(config);
            if (config.Filters.Length != config.Pools.Length)
                throw new ArgumentException("Each convolution says whether a pool follows it.", nameof(config));

            var weights = checkpoint.Weights;
            var classes = Get(weights, "fc.weight", null).Shape[0];
            characters ??= checkpoint.Characters ?? throw new InvalidDataException("The checkpoint does not say which character each answer of the network is.");
            var graph = new OnnxWriter("crnn", ("image", [1, 1, config.ImageHeight, "width"]), ("logits", ["frames", 1, classes]));
            graph.Metadata(CharactersKey, System.Text.Json.JsonSerializer.Serialize(Alphabet(characters, classes)));

            // The convolutions: the layers of the Sequential are numbered one after the other (convolution, normalization, ReLU, and a pool when there is one)
            var current = "image";
            var channels = 1;
            var layer = 0;
            for (var i = 0; i < config.Filters.Length; i++)
            {
                var filters = config.Filters[i];
                var weight = Get(weights, $"cnn.{layer}.weight", [filters, channels, 3, 3]);
                var bias = Get(weights, $"cnn.{layer}.bias", [filters]);
                var gamma = Get(weights, $"cnn.{layer + 1}.weight", [filters]);
                var beta = Get(weights, $"cnn.{layer + 1}.bias", [filters]);
                var mean = Get(weights, $"cnn.{layer + 1}.running_mean", [filters]);
                var variance = Get(weights, $"cnn.{layer + 1}.running_var", [filters]);

                var folded = (float[])weight.Values.Clone();
                var shifted = new float[filters];
                var perFilter = channels * 9;
                for (var f = 0; f < filters; f++)
                {
                    var scale = gamma.Values[f] / MathF.Sqrt(variance.Values[f] + BatchNormEpsilon);
                    for (var k = 0; k < perFilter; k++)
                        folded[f * perFilter + k] *= scale;
                    shifted[f] = (bias.Values[f] - mean.Values[f]) * scale + beta.Values[f];
                }

                graph.Floats($"conv{i}.w", folded, filters, channels, 3, 3);
                graph.Floats($"conv{i}.b", shifted, filters);
                graph.Node("Conv", [current, $"conv{i}.w", $"conv{i}.b"], [$"conv{i}"],
                    OnnxAttribute.Of("kernel_shape", 3, 3), OnnxAttribute.Of("pads", 1, 1, 1, 1), OnnxAttribute.Of("strides", 1, 1), OnnxAttribute.Of("dilations", 1, 1));
                graph.Node("LeakyRelu", [$"conv{i}"], [$"act{i}"], OnnxAttribute.Of("alpha", LeakySlope));
                current = $"act{i}";
                layer += 3;

                if (config.Pools[i])
                {
                    graph.Node("MaxPool", [current], [$"pool{i}"], OnnxAttribute.Of("kernel_shape", 2, 2), OnnxAttribute.Of("strides", 2, 2));
                    current = $"pool{i}";
                    layer++;
                }

                channels = filters;
            }

            // [1, channels, height, frames] -> [frames, 1, channels * height]: each column of the picture is one step of the LSTM
            var columnHeight = config.ImageHeight / config.Stride;
            graph.Integers("to.sequence", 0, 0, -1);
            graph.Node("Transpose", [current], ["columns"], OnnxAttribute.Of("perm", 3, 0, 1, 2));
            graph.Node("Reshape", ["columns", "to.sequence"], ["sequence"]);
            current = "sequence";

            var inputSize = channels * columnHeight;
            for (var k = 0; k < config.Layers; k++)
            {
                var hidden = config.Hidden;
                var forward = LstmDirection(weights, k, string.Empty, hidden, inputSize);
                var backward = LstmDirection(weights, k, "_reverse", hidden, inputSize);
                graph.Floats($"lstm{k}.w", [.. forward.Input, .. backward.Input], 2, 4 * hidden, inputSize);
                graph.Floats($"lstm{k}.r", [.. forward.Recurrent, .. backward.Recurrent], 2, 4 * hidden, hidden);
                graph.Floats($"lstm{k}.b", [.. forward.Bias, .. backward.Bias], 2, 8 * hidden);
                graph.Node("LSTM", [current, $"lstm{k}.w", $"lstm{k}.r", $"lstm{k}.b"], [$"lstm{k}.y", string.Empty, string.Empty],
                    OnnxAttribute.Of("hidden_size", hidden), OnnxAttribute.Of("direction", "bidirectional"));

                // [frames, 2, 1, hidden] -> [frames, 1, 2 * hidden]: the forward and the backward answer of a column side by side, as PyTorch gives them
                graph.Node("Transpose", [$"lstm{k}.y"], [$"lstm{k}.t"], OnnxAttribute.Of("perm", 0, 2, 1, 3));
                graph.Node("Reshape", [$"lstm{k}.t", "to.sequence"], [$"lstm{k}.out"]);
                current = $"lstm{k}.out";
                inputSize = 2 * hidden;
            }

            // The linear layer: PyTorch keeps its weight as [characters, 2 * hidden], MatMul wants it the other way round
            var fc = Get(weights, "fc.weight", [classes, inputSize]);
            var fcBias = Get(weights, "fc.bias", [classes]);
            var transposed = new float[inputSize * classes];
            for (var c = 0; c < classes; c++)
            {
                for (var j = 0; j < inputSize; j++)
                    transposed[j * classes + c] = fc.Values[c * inputSize + j];
            }

            graph.Floats("fc.w", transposed, inputSize, classes);
            graph.Floats("fc.b", fcBias.Values, classes);
            graph.Node("MatMul", [current, "fc.w"], ["scores"]);
            graph.Node("Add", ["scores", "fc.b"], ["logits"]);
            return graph.ToArray();
        }

        /// <summary>The characters in the order of the answers. The answer 0 is the blank; a character a checkpoint writes as a name of a token (<c>&lt;SPACE&gt;</c>) is the letter it stands for.</summary>
        internal static string[] Alphabet(IReadOnlyDictionary<int, string> characters, int classes)
        {
            var alphabet = new string[classes];
            for (var i = 1; i < classes; i++)
            {
                var text = characters.TryGetValue(i, out var known) ? known : string.Empty;
                alphabet[i] = text is "<SPACE>" or "<space>" ? " " : text is "<ctc>" ? string.Empty : text;
            }

            return alphabet;
        }

        private static (float[] Input, float[] Recurrent, float[] Bias) LstmDirection(IReadOnlyDictionary<string, TorchTensor> weights, int layer, string suffix, int hidden, int inputSize)
        {
            var input = Get(weights, $"rnn.weight_ih_l{layer}{suffix}", [4 * hidden, inputSize]);
            var recurrent = Get(weights, $"rnn.weight_hh_l{layer}{suffix}", [4 * hidden, hidden]);
            var biasInput = Get(weights, $"rnn.bias_ih_l{layer}{suffix}", [4 * hidden]);
            var biasRecurrent = Get(weights, $"rnn.bias_hh_l{layer}{suffix}", [4 * hidden]);
            return (Gates(input.Values, hidden), Gates(recurrent.Values, hidden), [.. Gates(biasInput.Values, hidden), .. Gates(biasRecurrent.Values, hidden)]);
        }

        /// <summary>The gates of an LSTM in the order of PyTorch (input, forget, cell, output) moved to the order of ONNX (input, output, forget, cell); <paramref name="values"/> holds the four blocks of <paramref name="hidden"/> rows one after the other.</summary>
        internal static float[] Gates(float[] values, int hidden)
        {
            var block = values.Length / 4;
            if (block != 0 && block % hidden != 0 || values.Length % 4 != 0)
                throw new ArgumentException("The weights of an LSTM have four blocks of the same size.", nameof(values));

            var result = new float[values.Length];
            int[] from = [0, 3, 1, 2]; // ONNX gate -> the block of PyTorch that holds it
            for (var gate = 0; gate < 4; gate++)
                Array.Copy(values, from[gate] * block, result, gate * block, block);
            return result;
        }

        private static TorchTensor Get(IReadOnlyDictionary<string, TorchTensor> weights, string name, int[]? shape)
        {
            if (!weights.TryGetValue(name, out var tensor))
                throw new InvalidDataException($"The checkpoint has no weight {name}; it is not the network this program knows.");

            if (shape is not null && !tensor.Shape.SequenceEqual(shape))
                throw new InvalidDataException($"The weight {name} is {string.Join(" by ", tensor.Shape)}; the network wants {string.Join(" by ", shape)}.");

            return tensor;
        }
    }
}
