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

        /// <summary>A zoom smaller than this (as a share of the size) is not a movement the eye sees: a subject that leaves no more room than that gets a photo that stands still.</summary>
        public const double MinUsefulZoom = 0.02;

        /// <summary>A face smaller than this share of the area of the largest face is a passer-by: the drift is allowed to cut it off.</summary>
        public const double PasserbyShare = 0.25;

        /// <summary>
        /// How much of a face box is kept in view besides the box itself, as shares of its width and height: the box the detector gives is the face from the brow to the chin, and the head
        /// is more than that (hair above it, the ears and the neck beside and below it).
        /// </summary>
        public const double PaddingSides = 0.25, PaddingTop = 0.5, PaddingBottom = 0.25;

        /// <summary>
        /// A path that keeps the people of the photo in view: it zooms in or out like <see cref="Random"/> (the same kind of movement, the same two numbers asked for), but about a point that
        /// lies on the faces, and no more than the faces allow, so that the drift never slices through a head. Faces that are less than <see cref="PasserbyShare"/> of the largest one are
        /// passers-by and are left to chance. With no usable face it is <see cref="Random"/>; with faces that fill the photo (nothing to zoom into) the path stands still.
        /// </summary>
        /// <param name="faces">Where the faces are in the photo, as fractions of it.</param>
        /// <param name="next">Gives numbers from 0 up to but not including 1.</param>
        public static KenBurnsPath Aimed(IReadOnlyList<PhotoRegion> faces, Func<double> next)
        {
            if (Subject(faces) is not { } subject)
                return Random(next);

            var wanted = 1 + MinZoom + (MaxZoom - MinZoom) * next();
            var zoomsIn = next() < 0.5;

            // The group of faces magnified must still fit in the photo (about any point), and it is then kept in it by where the point is
            var zoom = Math.Min(wanted, 1 / Math.Max(subject.Width, subject.Height));
            if (zoom < 1 + MinUsefulZoom)
                return new KenBurnsPath(1, 1, 0, 0);

            var anchorX = Anchor(subject.X, subject.Right, zoom);
            var anchorY = Anchor(subject.Y, subject.Bottom, zoom);
            return new KenBurnsPath(zoomsIn ? 1 : zoom, zoomsIn ? zoom : 1, anchorX, anchorY);
        }

        /// <summary>
        /// The point (-1 the first edge of the photo, 1 the last) to magnify about so that the stretch from <paramref name="from"/> to <paramref name="to"/> (fractions of the photo) stays inside the
        /// photo at <paramref name="zoom"/>: the middle of the stretch if that does, else the nearest point that does (a face at the edge is kept by an anchor at the edge).
        /// A point <c>u</c> (-1 to 1) magnified about <c>a</c> goes to <c>a + (u - a) * zoom</c>, which must lie within -1 and 1 for both ends.
        /// </summary>
        private static double Anchor(double from, double to, double zoom)
        {
            var first = from * 2 - 1;
            var last = to * 2 - 1;
            var lowest = Math.Max(-1, (last * zoom - 1) / (zoom - 1));
            var highest = Math.Min(1, (1 + first * zoom) / (zoom - 1));
            if (lowest > highest)
                return (lowest + highest) / 2; // only a rounding error leaves no room: the zoom was made to fit

            return Math.Clamp((first + last) / 2, lowest, highest);
        }

        /// <summary>The part of the photo the drift must keep: the faces that count, each with the head round it, as one box (fractions of the photo); null when there is no usable face.</summary>
        private static PhotoRegion? Subject(IReadOnlyList<PhotoRegion> faces)
        {
            var usable = faces.Where(face => face.IsUsable).ToList();
            if (usable.Count == 0)
                return null;

            var largest = usable.Max(face => face.Area);
            var kept = usable.Where(face => face.Area >= PasserbyShare * largest).Select(face => face.WithHead()).ToList();
            var left = kept.Min(face => face.X);
            var top = kept.Min(face => face.Y);
            return new PhotoRegion(left, top, kept.Max(face => face.Right) - left, kept.Max(face => face.Bottom) - top);
        }
    }

    /// <summary>A part of a photo (a face), as fractions of the photo: 0 is its left or top edge and 1 its right or bottom one.</summary>
    public readonly record struct PhotoRegion(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public double Area => Width * Height;

        /// <summary>It has a size and is made of numbers.</summary>
        public bool IsUsable => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;

        /// <summary>The box with the head round the face added (see <see cref="KenBurnsPath.PaddingSides"/>), cut to the photo.</summary>
        public PhotoRegion WithHead()
        {
            var left = Math.Clamp(X - Width * KenBurnsPath.PaddingSides, 0, 1);
            var top = Math.Clamp(Y - Height * KenBurnsPath.PaddingTop, 0, 1);
            var right = Math.Clamp(Right + Width * KenBurnsPath.PaddingSides, left, 1);
            var bottom = Math.Clamp(Bottom + Height * KenBurnsPath.PaddingBottom, top, 1);
            return new PhotoRegion(left, top, right - left, bottom - top);
        }
    }
}
