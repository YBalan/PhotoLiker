using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Stickers
{
    /// <summary>
    /// Finds the subject of a photo: IS-Net, "general use" (Apache 2.0, from the DIS project of Xuebin Qin and others), a network that is made to tell the thing a picture is about from what is behind
    /// it, with fine edges. It looks at the photo made 1024 by 1024 and gives, for every point of that, how likely the point is part of the subject; a photo that has no subject gets a flat map, which
    /// is a cutout of nothing. On a processor of 12 cores it takes about two seconds and a gigabyte of memory, which it gives back when it is disposed. Safe to use from several threads at once
    /// (a session of ONNX Runtime is). U2-Net, the model it grew out of, takes 320 by 320 and a third of the time, but leaves the shadow of a car on the road in the cutout.
    /// </summary>
    public sealed class SubjectDetector : IDisposable
    {
        /// <summary>The side of the square the model looks at; a photo is squeezed into it whatever its shape.</summary>
        public const int ModelSide = 1024;

        private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
        private static readonly float[] Std = [1f, 1f, 1f]; // this model is not divided by the deviation of ImageNet, only moved by its mean (the U2-Net before it was: 0.229, 0.224, 0.225)

        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly int _side;
        private readonly float[] _std;
        private readonly object _gate = new();
        private int _runs;
        private bool _disposed;

        /// <exception cref="FileNotFoundException">The model's file is not there.</exception>
        public SubjectDetector(string modelPath, int threads)
            : this(modelPath, threads, ModelSide, Std)
        {
        }

        internal SubjectDetector(string modelPath, int threads, int side, float[] std)
        {
            _side = side;
            _std = std;
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("The model that finds the subject of a photo is not on this computer.", modelPath);

            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // the program has other things to do with the processor

            // No memory arena: with it the session keeps for good what its busiest moment used (the model is 176 MB and its layers are wide), and a window that is closed gives the memory back
            // to nobody until the program ends. Without it the runs take their memory from Windows and give it back as they end. (The memory pattern is off with it: it needs the arena.)
            options.EnableCpuMemArena = false;
            options.EnableMemoryPattern = false;

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
        }

        /// <summary>The model's idea of the photo's subject, as a map of <see cref="ModelSide"/> by <see cref="ModelSide"/> points, 0 (the background) to 1 (the subject), stretched to use that whole range.</summary>
        /// <exception cref="ObjectDisposedException">The detector was let go of.</exception>
        public SubjectMap Detect(PhotoPixels photo)
        {
            ArgumentNullException.ThrowIfNull(photo);
            var input = Prepare(photo, _side, _std);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _runs++; // the session is not freed under a run that has begun
            }

            try
            {
                using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<float>(input, [1, 3, _side, _side]))]);
                var raw = results.First().AsTensor<float>().ToArray();
                if (raw.Length != _side * _side)
                    throw new InvalidDataException($"The model gave {raw.Length} numbers for a picture of {_side} by {_side}.");

                return new SubjectMap(_side, _side, Stretch(raw));
            }
            finally
            {
                lock (_gate)
                {
                    _runs--;
                    if (_disposed && _runs == 0)
                        _session.Dispose();
                }
            }
        }

        /// <summary>The planar RGB floats the model takes: the photo squeezed to 1024 by 1024, divided by its brightest value, then normalised with the mean of ImageNet and a deviation of 1 (what the code that was used to make the ONNX file does).</summary>
        internal static float[] Prepare(PhotoPixels photo, int side, float[] std)
        {
            var small = PlaneResizer.Resize(photo.Bgr, 3, photo.Width, photo.Height, side, side);
            var plane = side * side;

            float brightest = 1;
            foreach (var value in small)
                brightest = Math.Max(brightest, value);

            var input = new float[3 * plane];
            for (var i = 0; i < plane; i++)
            {
                input[i] = (small[i * 3 + 2] / brightest - Mean[0]) / std[0];
                input[plane + i] = (small[i * 3 + 1] / brightest - Mean[1]) / std[1];
                input[2 * plane + i] = (small[i * 3] / brightest - Mean[2]) / std[2];
            }
            return input;
        }

        /// <summary>The model's numbers moved to 0 to 1 by its own smallest and largest (a picture with no subject then has a map of a flat value, which is all 0).</summary>
        internal static float[] Stretch(float[] raw)
        {
            float low = float.MaxValue, high = float.MinValue;
            foreach (var value in raw)
            {
                if (float.IsNaN(value))
                    continue;
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }

            var result = new float[raw.Length];
            if (!(high > low))
                return result;

            for (var i = 0; i < raw.Length; i++)
                result[i] = float.IsNaN(raw[i]) ? 0 : Math.Clamp((raw[i] - low) / (high - low), 0f, 1f);
            return result;
        }

        /// <summary>Lets go of the model: at once, or when the last run that is going on has ended. A run begun after this is refused.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_runs == 0)
                    _session.Dispose();
            }
        }
    }
}
