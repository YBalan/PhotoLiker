using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Depth
{
    /// <summary>
    /// How near each part of a picture is: one byte per point of a grid that is much coarser than the picture (about 500 points on the longer side), 0 for the farthest
    /// thing in it and 255 for the nearest. It says nothing about metres: the model sees depth in relation to the picture, and the numbers are stretched to use the whole range.
    /// </summary>
    public sealed record DepthMap(int Width, int Height, byte[] Values)
    {
        /// <summary>
        /// The depth, 0 to 1, at a place of the picture given as fractions of its width and height (0 = the left or top edge, 1 = the right or bottom one; outside is
        /// the nearest edge), smoothed between the points of the grid.
        /// </summary>
        public float At(double x, double y)
        {
            var fx = Math.Clamp(x * Width - 0.5, 0, Width - 1);
            var fy = Math.Clamp(y * Height - 0.5, 0, Height - 1);
            var x0 = (int)fx;
            var y0 = (int)fy;
            var x1 = Math.Min(x0 + 1, Width - 1);
            var y1 = Math.Min(y0 + 1, Height - 1);
            var wx = (float)(fx - x0);
            var wy = (float)(fy - y0);

            var top = Values[y0 * Width + x0] * (1 - wx) + Values[y0 * Width + x1] * wx;
            var bottom = Values[y1 * Width + x0] * (1 - wx) + Values[y1 * Width + x1] * wx;
            return (top * (1 - wy) + bottom * wy) / 255f;
        }

        /// <summary>
        /// The map with its edges softened: every point is the average of the points within <paramref name="radius"/> of it (a box of 2 * radius + 1 points square, the picture's edge repeated).
        /// A cliff between a near thing and a far one is what makes a picture that is moved by its depth tear along the edge; a slope of a few points does not.
        /// </summary>
        public DepthMap Blurred(int radius)
        {
            if (radius <= 0)
                return this;

            var across = new float[Values.Length];
            for (var y = 0; y < Height; y++)
                BoxLine(i => Values[y * Width + i], Width, radius, (i, v) => across[y * Width + i] = v);

            var result = new byte[Values.Length];
            for (var x = 0; x < Width; x++)
                BoxLine(i => across[i * Width + x], Height, radius, (i, v) => result[i * Width + x] = (byte)Math.Clamp(v + 0.5f, 0f, 255f));

            return new DepthMap(Width, Height, result);
        }

        private static void BoxLine(Func<int, float> read, int length, int radius, Action<int, float> write)
        {
            var window = 2 * radius + 1;
            float sum = 0;
            for (var k = -radius; k <= radius; k++)
                sum += read(Math.Clamp(k, 0, length - 1));
            for (var i = 0; i < length; i++)
            {
                write(i, sum / window);
                sum += read(Math.Min(i + radius + 1, length - 1)) - read(Math.Max(i - radius, 0));
            }
        }
    }

    /// <summary>
    /// The depth of a picture, worked out by Depth Anything V2 Small (<see cref="ToolModels.Depth"/>) on this computer: 0.3 to 0.5 s for a photo on six cores. It is what the
    /// parallax effect of the slideshow and of the video moves the parts of a photo by. Safe to use from several threads at once (a session of ONNX Runtime is).
    /// </summary>
    public sealed class DepthEstimator : IDisposable
    {
        /// <summary>The longer side of what the model looks at. The model takes any size that is a multiple of 14; this is the size it was trained at, and larger ones are slower and no better.</summary>
        public const int LongSide = 518;

        private const int Multiple = 14;

        /// <summary>
        /// How far (in points of the depth grid) the edges between near and far things are softened (<see cref="DepthMap.Blurred"/>). The depth is there to move a picture, and a picture that is moved by a
        /// cliff of depth tears along it into a ragged edge (seen at 1:1 on a 1080p screen); with 2 the edge is clean and still sharp, with 4 it begins to be soft.
        /// </summary>
        public const int EdgeSoftening = 2;
        private const double LowPercentile = 0.01;  // the farthest 1% and the nearest 1% are not allowed to squeeze everything else together
        private const double HighPercentile = 0.99;

        private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
        private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly RunGuard _guard;

        /// <exception cref="FileNotFoundException">The model's file is not there (see <see cref="EffectModels.DepthDownloaded"/>).</exception>
        public DepthEstimator(string modelPath, int threads)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("The depth model is not on this computer.", modelPath);

            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // the program has other things to do with the processor

            // No memory arena: this session is kept for as long as the program runs (the slideshow, the video and every later show use the same), and with the arena it keeps for good what its
            // busiest moment used (several photos at a time, each of a shape of its own): 0.4 GB after one run, 0.55 to 1.3 GB with the runs of a slideshow. Without it the runs take their
            // memory from Windows and give it back as they end: 0.15 GB open, 0.7 s a run instead of 0.6 s. (The memory pattern is off with it: it needs the arena.)
            options.EnableCpuMemArena = false;
            options.EnableMemoryPattern = false;

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
            _guard = new RunGuard(_session.Dispose, nameof(DepthEstimator));
        }

        /// <summary>The size the model is given for a picture of this size: the longer side is <see cref="LongSide"/>, both sides multiples of 14, and the shape is kept.</summary>
        public static (int Width, int Height) InputSizeFor(int width, int height)
        {
            var scale = LongSide / (double)Math.Max(width, height);
            return (Math.Max(Multiple, (int)Math.Round(width * scale / Multiple) * Multiple), Math.Max(Multiple, (int)Math.Round(height * scale / Multiple) * Multiple));
        }

        /// <summary>The depth of a photo.</summary>
        public DepthMap Estimate(DecodedPhoto photo) => Estimate(photo.Bgr, 3, photo.Width, photo.Height);

        /// <summary>The depth of a picture given as pixels of a screen: 0xAARRGGBB, row after row (what a bitmap of 32 bits keeps), the alpha not looked at.</summary>
        public DepthMap Estimate(ReadOnlySpan<uint> pixels, int width, int height)
        {
            var bytes = new byte[width * height * 4];
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels[..(width * height)]).CopyTo(bytes);
            return Estimate(bytes, 4, width, height);
        }

        private DepthMap Estimate(byte[] pixels, int bytesPerPixel, int width, int height)
        {
            var (w, h) = InputSizeFor(width, height);
            var input = Prepare(pixels, bytesPerPixel, width, height, w, h);

            _guard.Enter(); // the program may be closing: the session is not freed under a run that has begun
            try
            {
                using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<float>(input, [1, 3, h, w]))]);
                var raw = results.First().AsTensor<float>().ToArray();
                if (raw.Length != w * h)
                    throw new InvalidDataException($"The depth model gave {raw.Length} numbers for a picture of {w} by {h}.");

                return new DepthMap(w, h, Stretch(raw)).Blurred(EdgeSoftening);
            }
            finally
            {
                _guard.Leave();
            }
        }

        /// <summary>The planar RGB floats the model takes: the picture made <paramref name="w"/> by <paramref name="h"/>, normalised with the mean and deviation of ImageNet.</summary>
        internal static float[] Prepare(byte[] pixels, int bytesPerPixel, int width, int height, int w, int h)
        {
            var plane = w * h;
            var input = new float[3 * plane];

            // Every point of the result is the average of the points of the picture it covers (a smaller picture: the smooth blend of the four nearest), so that a large photo does not alias
            var xs = Spans(width, w);
            var ys = Spans(height, h);
            Parallel.For(0, h, y =>
            {
                var (y0, y1, wy) = ys[y];
                for (var x = 0; x < w; x++)
                {
                    var (x0, x1, wx) = xs[x];
                    float b = 0, g = 0, r = 0;
                    if (y1 - y0 == 1 && x1 - x0 == 1)
                    {
                        // Enlarged or about the same: the blend of the point and the next one
                        Blend(pixels, bytesPerPixel, width, height, x0, y0, wx, wy, out b, out g, out r);
                    }
                    else
                    {
                        var count = 0;
                        for (var yy = y0; yy < y1; yy++)
                        {
                            for (var xx = x0; xx < x1; xx++)
                            {
                                var p = (yy * width + xx) * bytesPerPixel;
                                b += pixels[p];
                                g += pixels[p + 1];
                                r += pixels[p + 2];
                                count++;
                            }
                        }
                        b /= count;
                        g /= count;
                        r /= count;
                    }

                    var i = y * w + x;
                    input[i] = (r / 255f - Mean[0]) / Std[0];
                    input[plane + i] = (g / 255f - Mean[1]) / Std[1];
                    input[2 * plane + i] = (b / 255f - Mean[2]) / Std[2];
                }
            });
            return input;
        }

        /// <summary>For every point of the result the range of picture points it covers (one point wide when the picture is not larger), and how far between that point and the next to blend.</summary>
        private static (int From, int To, float Weight)[] Spans(int size, int newSize)
        {
            var scale = size / (double)newSize;
            var spans = new (int, int, float)[newSize];
            for (var i = 0; i < newSize; i++)
            {
                if (scale <= 1)
                {
                    var at = Math.Clamp((i + 0.5) * scale - 0.5, 0, size - 1);
                    var from = (int)at;
                    spans[i] = (from, from + 1, (float)(at - from));
                }
                else
                {
                    var from = Math.Clamp((int)Math.Floor(i * scale), 0, size - 1);
                    var to = Math.Clamp((int)Math.Ceiling((i + 1) * scale), from + 1, size);
                    spans[i] = (from, to, 0f);
                }
            }
            return spans;
        }

        private static void Blend(byte[] pixels, int bytesPerPixel, int width, int height, int x0, int y0, float wx, float wy, out float b, out float g, out float r)
        {
            var x1 = Math.Min(x0 + 1, width - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            var a = (y0 * width + x0) * bytesPerPixel;
            var c = (y0 * width + x1) * bytesPerPixel;
            var d = (y1 * width + x0) * bytesPerPixel;
            var e = (y1 * width + x1) * bytesPerPixel;

            float Channel(int k)
            {
                var top = pixels[a + k] * (1 - wx) + pixels[c + k] * wx;
                var bottom = pixels[d + k] * (1 - wx) + pixels[e + k] * wx;
                return top * (1 - wy) + bottom * wy;
            }
            b = Channel(0);
            g = Channel(1);
            r = Channel(2);
        }

        /// <summary>The model's numbers (larger is nearer, on no fixed scale) as bytes, 0 at the first percentile and 255 at the 99th, so every picture uses the whole range.</summary>
        internal static byte[] Stretch(float[] raw)
        {
            var bytes = new byte[raw.Length];
            var valid = raw.Where(value => !float.IsNaN(value)).ToArray();
            if (valid.Length == 0)
            {
                Array.Fill(bytes, (byte)128);
                return bytes;
            }

            Array.Sort(valid);
            var low = valid[(int)(valid.Length * LowPercentile)];
            var high = valid[Math.Min(valid.Length - 1, (int)(valid.Length * HighPercentile))];
            if (!(high > low))
            {
                // Nearly all the points are the same (a flat picture): the range is all there is, and where there is none everything is at the middle
                low = valid[0];
                high = valid[^1];
                if (!(high > low))
                {
                    Array.Fill(bytes, (byte)128);
                    return bytes;
                }
            }

            var span = high - low;
            for (var i = 0; i < raw.Length; i++)
                bytes[i] = float.IsNaN(raw[i]) ? (byte)128 : (byte)Math.Clamp((raw[i] - low) / span * 255f + 0.5f, 0f, 255f);
            return bytes;
        }

        /// <summary>Lets go of the model: at once, or when the last run that is going on has ended (see <see cref="RunGuard"/>). A run begun after this is refused.</summary>
        public void Dispose() => _guard.Dispose();
    }
}
