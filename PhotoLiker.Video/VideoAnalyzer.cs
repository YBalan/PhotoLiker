using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>The key frames of a video are too far apart to find anything with (a long GOP): the pictures have to be taken another way.</summary>
    internal sealed class SparseKeyFramesException : Exception;

    /// <summary>
    /// Turns the pictures of a video into its moments: every picture goes through the model (in batches, which is what makes it fast), and the
    /// <see cref="MomentSelector"/> keeps those that say something new, each with a small picture of itself and the faces in it.
    /// </summary>
    internal static class VideoAnalyzer
    {
        public const int BatchSize = 16;

        /// <summary>Key frames further apart than this on average are no way to find a time in a video.</summary>
        public const double SparsestKeyFrameSeconds = 8;

        /// <param name="frames">The pictures, in order of time.</param>
        /// <param name="encode">The model's photo half: the prepared pictures lie one after another in the array, and the number says how many; the vectors come back.</param>
        /// <param name="reached">Told how far into the video (seconds) the pictures have got, after each batch.</param>
        /// <param name="keyFramesOnly">The pictures are the key frames only: if they turn out to be far apart, <see cref="SparseKeyFramesException"/> says to take them another way.</param>
        /// <param name="findFaces">Finds the faces in the picture of a moment (only the pictures that are kept are looked at); null looks for none.</param>
        /// <param name="faceWorkers">How many pictures are looked at for faces at the same time.</param>
        public static List<MomentData> Analyze(
            IEnumerable<VideoFrame> frames, ClipModelInfo model, Func<float[], int, float[][]> encode, MomentSelector selector,
            CancellationToken cancellationToken, Action<double>? reached = null, bool keyFramesOnly = false,
            Func<DecodedPhoto, IReadOnlyList<MomentFace>>? findFaces = null, int faceWorkers = 1)
        {
            var moments = new List<MomentData>();
            var length = model.InputLength;
            var buffer = new float[BatchSize * length];
            var batch = new List<VideoFrame>(BatchSize);
            var count = 0;
            double first = 0, last = 0;

            void Flush()
            {
                if (batch.Count == 0)
                    return;

                for (var i = 0; i < batch.Count; i++)
                    Array.Copy(ClipImageEncoder.Prepare(batch[i].Picture, model), 0, buffer, i * length, length);

                var vectors = encode(buffer, batch.Count);
                var kept = new List<int>();
                for (var i = 0; i < batch.Count; i++)
                {
                    if (selector.Keep(vectors[i], batch[i].Seconds))
                        kept.Add(i);
                }

                // Faces only in the pictures that are kept, and a few at a time: the face models are the slow part
                var faces = new IReadOnlyList<MomentFace>?[kept.Count];
                if (findFaces is not null && kept.Count > 0)
                {
                    Parallel.For(0, kept.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, faceWorkers), CancellationToken = cancellationToken },
                        k => faces[k] = findFaces(batch[kept[k]].Picture));
                }

                for (var k = 0; k < kept.Count; k++)
                {
                    var frame = batch[kept[k]];
                    moments.Add(new MomentData((long)Math.Round(frame.Seconds * 1000), vectors[kept[k]], FrameThumbnails.Encode(frame.Picture), faces[k]));
                }
                reached?.Invoke(batch[^1].Seconds);
                batch.Clear();
            }

            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(frame);
                if (count++ == 0)
                    first = frame.Seconds;
                last = frame.Seconds;

                if (keyFramesOnly && count == 4 && (last - first) / (count - 1) > SparsestKeyFrameSeconds)
                    throw new SparseKeyFramesException();

                if (batch.Count == BatchSize)
                    Flush();
            }

            Flush();
            return moments;
        }
    }
}
