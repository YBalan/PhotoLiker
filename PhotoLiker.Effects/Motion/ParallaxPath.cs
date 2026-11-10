using PhotoLiker.Core;

namespace PhotoLiker.Effects.Motion
{
    /// <summary>
    /// Where the "camera" of <see cref="SlideshowEffect.Parallax"/> is at one moment: how far the picture is moved by the nearest thing in it relative to the farthest, sideways
    /// (<paramref name="ShiftX"/>) and up (<paramref name="ShiftY"/>), in pixels of the picture, and how much it is magnified (<paramref name="Zoom"/>) so that moving it never
    /// shows what is beyond its edge. A point of depth <c>d</c> (0 the farthest, 1 the nearest) is moved by <c>(d - <see cref="ParallaxPath.Focus"/>) * Shift</c>.
    /// </summary>
    public readonly record struct ParallaxView(double ShiftX, double ShiftY, double Zoom);

    /// <summary>
    /// The movement of one photo for <see cref="SlideshowEffect.Parallax"/>: the camera goes across from one side to the other at an even speed (<paramref name="Direction"/> says which,
    /// 1 is to the right and -1 to the left), leaning up or down by <paramref name="Tilt"/> (-0.5 to 0.5 of the sideways way) while it does, and the photo is pushed in a little
    /// (<paramref name="PushIn"/>) as it goes.
    /// </summary>
    public readonly record struct ParallaxPath(double Direction, double Tilt, double PushIn)
    {
        /// <summary>
        /// How far the nearest and the farthest parts of the picture move against each other, over the whole way across, as a share of the longer side of the picture
        /// (a whole way is 2 steps of the camera, from -1 to 1): 5% of the width is about 95 pixels on a full HD screen.
        /// </summary>
        public const double Amplitude = 0.025;

        /// <summary>The depth that does not move: the middle of the range, so the near parts go one way and the far ones the other, and the picture does not slide as a whole.</summary>
        public const double Focus = 0.5;

        /// <summary>The farthest the picture is pushed in over its time: 1.5% to 3.5%.</summary>
        public const double MinPush = 0.015;
        public const double MaxPush = 0.035;

        /// <summary>How the camera is at <paramref name="progress"/> of the way through (0 at the start, 1 at the end; outside it is the nearest of them) for a picture of this size.</summary>
        public ParallaxView At(double progress, double width, double height)
        {
            var p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1);
            var longer = Math.Max(width, height);
            var across = Direction * (2 * p - 1);
            var up = Tilt * (2 * p - 1);

            // The part that moves the most (the nearest or the farthest: half of the range each, from the focus) goes this far at the end of the way
            var most = Amplitude * longer * Math.Max(Focus, 1 - Focus);
            var needed = 1 + 2 * Math.Max(Math.Abs(Direction) * most / Math.Max(1, width), Math.Abs(Tilt) * most / Math.Max(1, height));
            return new ParallaxView(across * Amplitude * longer, up * Amplitude * longer, needed + PushIn * p);
        }

        /// <summary>A path chosen at random: which way across, how much it leans, how far it pushes in. <paramref name="next"/> gives numbers from 0 up to but not including 1.</summary>
        public static ParallaxPath Random(Func<double> next) =>
            new(next() < 0.5 ? -1 : 1, (next() - 0.5), MinPush + (MaxPush - MinPush) * next());
    }
}
