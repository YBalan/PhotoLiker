using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Search
{
    public readonly record struct Point2(float X, float Y);

    /// <summary>A face found in a photo. All numbers are pixels of the decoded photo.</summary>
    public sealed record DetectedFace(float X, float Y, float Width, float Height, float Score, Point2[] Landmarks);

    /// <summary>One output tensor of the detector, flattened to rows x columns.</summary>
    internal readonly record struct RawOutput(float[] Data, int Rows, int Columns);

    /// <summary>
    /// Finds faces with InsightFace's SCRFD (det_10g): a photo goes in as a 640 x 640 square, and for each of the three
    /// strides the model gives a score, a box and five landmarks (eyes, nose, mouth corners) per anchor.
    /// </summary>
    public sealed class FaceDetector : IDisposable
    {
        public const int InputSize = 640;
        public const float DefaultThreshold = 0.5f;

        private const float NmsThreshold = 0.4f;
        private const int AnchorsPerCell = 2;
        private static readonly int[] Strides = [8, 16, 32];

        private readonly InferenceSession _session;
        private readonly string _input;

        public FaceDetector(string modelPath, int threads)
        {
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // several models share the processor

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
        }

        /// <summary>
        /// The model input of a photo: scaled to fit 640 x 640 keeping its proportions, in the top left corner, the rest
        /// black; normalized, planar RGB. <c>Scale</c> is how much bigger the photo was made, to map results back.
        /// </summary>
        public static (float[] Tensor, float Scale) Prepare(DecodedPhoto photo)
        {
            int width, height;
            if (photo.Height > photo.Width)
            {
                height = InputSize;
                width = Math.Max(1, (int)(InputSize * (double)photo.Width / photo.Height));
            }
            else
            {
                width = InputSize;
                height = Math.Max(1, (int)(InputSize * (double)photo.Height / photo.Width));
            }

            var resized = Resampler.ResizeBgr(photo.Bgr, photo.Width, photo.Height, width, height);

            var plane = InputSize * InputSize;
            var tensor = new float[3 * plane];
            Array.Fill(tensor, (0f - 127.5f) / 128f); // the black border
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = (y * width + x) * 3;
                    var i = y * InputSize + x;
                    tensor[i] = (resized[p + 2] - 127.5f) / 128f;
                    tensor[plane + i] = (resized[p + 1] - 127.5f) / 128f;
                    tensor[2 * plane + i] = (resized[p] - 127.5f) / 128f;
                }
            }
            return (tensor, height / (float)photo.Height);
        }

        public List<DetectedFace> Detect(float[] tensor, float scale, float threshold = DefaultThreshold)
        {
            var input = new DenseTensor<float>(tensor, [1, 3, InputSize, InputSize]);
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, input)]);

            var outputs = new List<RawOutput>();
            foreach (var result in results)
            {
                var value = result.AsTensor<float>();
                var columns = value.Dimensions[^1];
                var data = value.ToArray();
                outputs.Add(new RawOutput(data, data.Length / columns, columns));
            }
            return Decode(outputs, scale, threshold);
        }

        /// <summary>Turns the raw outputs into faces: picks the anchors above the threshold, then drops overlapping boxes.</summary>
        internal static List<DetectedFace> Decode(IReadOnlyList<RawOutput> outputs, float scale, float threshold)
        {
            // The outputs are told apart by their width, and the strides by how many anchors they have (8 has the most)
            var scores = outputs.Where(o => o.Columns == 1).OrderByDescending(o => o.Rows).ToList();
            var boxes = outputs.Where(o => o.Columns == 4).OrderByDescending(o => o.Rows).ToList();
            var points = outputs.Where(o => o.Columns == 10).OrderByDescending(o => o.Rows).ToList();
            if (scores.Count != Strides.Length || boxes.Count != Strides.Length || points.Count != Strides.Length)
                throw new InvalidDataException("The face detector model does not have the expected outputs (scores, boxes and landmarks for 3 strides).");

            var found = new List<DetectedFace>();
            for (var s = 0; s < Strides.Length; s++)
            {
                var stride = Strides[s];
                var cells = InputSize / stride;
                if (scores[s].Rows != cells * cells * AnchorsPerCell)
                    throw new InvalidDataException($"Unexpected number of anchors for stride {stride}: {scores[s].Rows}.");

                for (var i = 0; i < scores[s].Rows; i++)
                {
                    var score = scores[s].Data[i];
                    if (score < threshold)
                        continue;

                    var cell = i / AnchorsPerCell;
                    float cx = cell % cells * stride, cy = cell / cells * stride;

                    var b = boxes[s].Data.AsSpan(i * 4, 4);
                    var x1 = (cx - b[0] * stride) / scale;
                    var y1 = (cy - b[1] * stride) / scale;
                    var x2 = (cx + b[2] * stride) / scale;
                    var y2 = (cy + b[3] * stride) / scale;

                    var k = points[s].Data.AsSpan(i * 10, 10);
                    var landmarks = new Point2[5];
                    for (var j = 0; j < 5; j++)
                        landmarks[j] = new Point2((cx + k[2 * j] * stride) / scale, (cy + k[2 * j + 1] * stride) / scale);

                    found.Add(new DetectedFace(x1, y1, x2 - x1, y2 - y1, score, landmarks));
                }
            }

            return SuppressOverlaps(found);
        }

        private static List<DetectedFace> SuppressOverlaps(List<DetectedFace> candidates)
        {
            var kept = new List<DetectedFace>();
            foreach (var candidate in candidates.OrderByDescending(f => f.Score))
            {
                if (kept.All(k => Overlap(k, candidate) < NmsThreshold))
                    kept.Add(candidate);
            }
            return kept;
        }

        private static float Overlap(DetectedFace a, DetectedFace b)
        {
            var width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
            var height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
            if (width <= 0 || height <= 0)
                return 0;

            var intersection = width * height;
            return intersection / (a.Width * a.Height + b.Width * b.Height - intersection);
        }

        public void Dispose() => _session.Dispose();
    }
}
