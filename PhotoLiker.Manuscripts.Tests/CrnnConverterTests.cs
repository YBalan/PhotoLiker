using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoLiker.Manuscripts.Handwriting;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>
    /// The converter against PyTorch itself: Fixtures/tiny_crnn.pt is a network of the kind of the handwriting models (convolutions with batch normalization and pools, a two layer bidirectional LSTM,
    /// a linear layer) with random weights and random batch normalization statistics, saved by torch.save, and tiny_crnn_expected.json holds what PyTorch gave for a picture. The ONNX file the
    /// converter makes from the checkpoint has to give the same numbers when ONNX Runtime runs it.
    /// </summary>
    public sealed class CrnnConverterTests
    {
        private static readonly CrnnConfig TinyConfig = new() { ImageHeight = 16, Filters = [3, 4, 4], Pools = [true, false, true], Hidden = 5, Layers = 2 };

        private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        private sealed record Expected(int Height, int Width, int Frames, int Classes, float[] Input, float[] Logits);

        private static Expected LoadExpected()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Fixture("tiny_crnn_expected.json")));
            var root = document.RootElement;
            float[] Floats(string name) => root.GetProperty(name).EnumerateArray().Select(e => e.GetSingle()).ToArray();
            return new Expected(root.GetProperty("height").GetInt32(), root.GetProperty("width").GetInt32(), root.GetProperty("frames").GetInt32(), root.GetProperty("classes").GetInt32(), Floats("input"), Floats("logits"));
        }

        private static float[] Run(byte[] onnx, Expected expected)
        {
            using var session = new InferenceSession(onnx);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor("image", new DenseTensor<float>(expected.Input, [1, 1, expected.Height, expected.Width]))]);
            var output = results.First().AsTensor<float>();
            Assert.Equal([expected.Frames, 1, expected.Classes], output.Dimensions.ToArray());
            return output.ToArray();
        }

        [Fact]
        public void The_checkpoint_of_PyTorch_is_read_with_its_weights_and_its_characters()
        {
            var checkpoint = TorchCheckpoint.Read(Fixture("tiny_crnn.pt"));

            Assert.Equal([3, 1, 3, 3], checkpoint.Weights["cnn.0.weight"].Shape);
            Assert.Equal([9, 10], checkpoint.Weights["fc.weight"].Shape);
            Assert.Equal(3 * 1 * 3 * 3, checkpoint.Weights["cnn.0.weight"].Values.Length);
            Assert.Equal("ї", checkpoint.Characters![5]);
            Assert.Equal(" ", checkpoint.Characters[1]);
            Assert.Equal(9, checkpoint.Characters.Count);
        }

        [Fact]
        public void The_converted_network_gives_the_numbers_PyTorch_gives()
        {
            var expected = LoadExpected();
            var onnx = CrnnConverter.ToOnnx(TorchCheckpoint.Read(Fixture("tiny_crnn.pt")), TinyConfig);

            var actual = Run(onnx, expected);

            Assert.Equal(expected.Logits.Length, actual.Length);
            for (var i = 0; i < actual.Length; i++)
                Assert.True(Math.Abs(actual[i] - expected.Logits[i]) < 1e-4f, $"Number {i}: {actual[i]} against {expected.Logits[i]} of PyTorch.");
        }

        [Fact]
        public void A_wider_picture_gives_more_columns_and_a_picture_that_is_not_a_multiple_of_the_stride_loses_its_last_pixels_as_PyTorch_does()
        {
            var onnx = CrnnConverter.ToOnnx(TorchCheckpoint.Read(Fixture("tiny_crnn.pt")), TinyConfig);
            using var session = new InferenceSession(onnx);

            int Frames(int width)
            {
                using var results = session.Run([NamedOnnxValue.CreateFromTensor("image", new DenseTensor<float>(new float[16 * width], [1, 1, 16, width]))]);
                return results.First().AsTensor<float>().Dimensions[0];
            }

            Assert.Equal(4, Frames(16));
            Assert.Equal(9, Frames(37)); // 37 / 4 = 9, as in the expected numbers of PyTorch
            Assert.Equal(25, Frames(100));
        }

        [Fact]
        public void The_characters_travel_in_the_file_with_the_network()
        {
            var path = Path.Combine(Path.GetTempPath(), "PhotoLikerCrnn_" + Guid.NewGuid().ToString("N") + ".onnx");
            try
            {
                File.WriteAllBytes(path, CrnnConverter.ToOnnx(TorchCheckpoint.Read(Fixture("tiny_crnn.pt")), TinyConfig));
                using var reader = new HandwritingReader(path, TinyConfig);

                // A picture of nothing is no text: whatever the characters are, they only come out when the network chooses them
                var (text, confidence) = reader.Read(new float[16 * 40], 40);
                Assert.NotNull(text);
                Assert.InRange(confidence, 0f, 1f);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void A_checkpoint_that_is_not_the_network_is_refused_with_the_weight_that_is_missing()
        {
            var checkpoint = TorchCheckpoint.Read(Fixture("tiny_crnn.pt"));

            // Three convolutions in the file, four asked for
            var ex = Assert.Throws<InvalidDataException>(() => CrnnConverter.ToOnnx(checkpoint, TinyConfig with { Filters = [3, 4, 4, 4], Pools = [true, false, true, false] }));
            Assert.Contains("cnn.", ex.Message);

            // The right number of convolutions, but not the sizes
            var wrong = Assert.Throws<InvalidDataException>(() => CrnnConverter.ToOnnx(checkpoint, TinyConfig with { Filters = [3, 5, 4] }));
            Assert.Contains("wants", wrong.Message);
        }

        [Fact]
        public void A_file_that_is_not_a_checkpoint_is_refused()
        {
            using var notZip = new MemoryStream("this is not a PyTorch file"u8.ToArray());
            Assert.Throws<InvalidDataException>(() => TorchCheckpoint.Read(notZip));
        }

        [Fact]
        public void The_gates_of_an_LSTM_move_from_the_order_of_PyTorch_to_the_order_of_ONNX()
        {
            // Four blocks of two numbers: PyTorch has input, forget, cell, output; ONNX wants input, output, forget, cell
            float[] torch = [1, 1, 2, 2, 3, 3, 4, 4];
            Assert.Equal([1f, 1, 4, 4, 2, 2, 3, 3], CrnnConverter.Gates(torch, 2));
        }
    }
}
