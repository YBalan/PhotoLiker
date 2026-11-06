using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>
    /// Chooses which of the pictures taken out of a video are kept as its moments. Every picture goes through the model, but one that looks like the last one
    /// kept says nothing new: a picture is kept when it is no longer like the last kept one (its vectors are less alike than <c>similarity</c>), or when it
    /// is a long time since the last one. The last kept picture is the one every later one is compared with, not the one just before: a slow change, a pan over
    /// a landscape, adds up and gets its picture too, where comparing neighbours would never see it.
    /// </summary>
    /// <param name="similarity">Cosine of two vectors below which pictures count as different. Measured on handheld family video: at 0.92 a camera on a tripod keeps a quarter of the pictures, a hand-held one nearly all; 0.90 keeps one picture in 2-4 for most of it.</param>
    /// <param name="maxGapSeconds">A video that does not change at all still has a moment this often, so that a time can be found in it.</param>
    public sealed class MomentSelector(float similarity = MomentSelector.DefaultSimilarity, double maxGapSeconds = MomentSelector.DefaultMaxGapSeconds)
    {
        public const float DefaultSimilarity = 0.90f;
        public const double DefaultMaxGapSeconds = 15;

        private float[]? _last;
        private double _lastSeconds;

        /// <summary>Whether this picture, the next in time, is a moment of the video.</summary>
        public bool Keep(float[] vector, double seconds)
        {
            if (_last is null || VectorMath.Dot(_last, vector) < similarity || seconds - _lastSeconds >= maxGapSeconds)
            {
                _last = vector;
                _lastSeconds = seconds;
                return true;
            }
            return false;
        }
    }
}
