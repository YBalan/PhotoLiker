using PhotoLiker.Core;
using PhotoLiker.Effects.Transitions;

namespace PhotoLiker.Effects.Motion
{
    /// <summary>
    /// The slow movement of one photo for <see cref="SlideshowEffect.KenBurns"/>: it is magnified from <see cref="FromScale"/> to <see cref="ToScale"/>
    /// (one of them is 1: it either zooms in or out) around a point of the picture that stays where it is, <see cref="AnchorX"/> and <see cref="AnchorY"/>,
    /// from -1 (the left / top edge) through 0 (the middle) to 1 (the right / bottom edge). So the picture drifts as it grows, and it never shrinks
    /// below the size it was made, so the part of the screen it covered at the start is covered all the time.
    /// </summary>
    public readonly record struct KenBurnsPath(double FromScale, double ToScale, double AnchorX, double AnchorY)
    {
        /// <summary>The farthest the picture grows or shrinks: 1.08 to 1.16 times its size.</summary>
        public const double MinZoom = 0.08;
        public const double MaxZoom = 0.16;

        /// <summary>Where the picture is <paramref name="progress"/> of the way through (0 at the start, 1 at the end; outside it is the nearest of them), offsets as fractions of the picture.</summary>
        public SlideshowLayer At(double progress)
        {
            var p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1);
            var scale = FromScale + (ToScale - FromScale) * p;

            // The picture magnified about the anchor point has its middle moved by (1 - scale) times the distance from the anchor to the middle
            var shift = (1 - scale) / 2;
            return new SlideshowLayer(1, scale, shift * AnchorX, shift * AnchorY);
        }

        /// <summary>A path chosen at random: zooming in or out, by how much, and around which point. <paramref name="next"/> gives numbers from 0 up to but not including 1.</summary>
        public static KenBurnsPath Random(Func<double> next)
        {
            var zoom = 1 + MinZoom + (MaxZoom - MinZoom) * next();
            var zoomsIn = next() < 0.5;
            return new KenBurnsPath(zoomsIn ? 1 : zoom, zoomsIn ? zoom : 1, next() * 2 - 1, next() * 2 - 1);
        }
    }
}
