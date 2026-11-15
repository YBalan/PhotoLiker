using System.Drawing;
using PhotoLiker.Core;

namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The rules of the collage slides (<see cref="SlideshowCollage"/>): which photos make a page, and where each goes on the screen. Nothing here knows about windows or pictures.
    /// </summary>
    public static class CollagePlan
    {
        /// <summary>Two photos that follow each other in the list belong to one event when they were taken no more than this far apart (or when one of them has no date to say otherwise).</summary>
        public static readonly TimeSpan SameEvent = TimeSpan.FromHours(6);

        /// <summary>The most photos on a page.</summary>
        public const int MostPhotos = 4;

        private static readonly int[] MixedSizes = [3, 2, 4];

        /// <summary>
        /// Divides the photos, in the order of the list, into pages: the places in the list of the photos of each page, in order. A page has as many photos as <paramref name="collage"/> says (the sizes of
        /// <see cref="SlideshowCollage.Mixed"/> come in turn) but fewer where the event ends: a photo that is not taken within <see cref="SameEvent"/> of the one before it starts a page of its own. A photo that
        /// is alone on its page is shown as it is.
        /// </summary>
        /// <param name="taken">When each photo was taken (null for one that has no date: it does not end an event).</param>
        public static IReadOnlyList<int[]> Pages(IReadOnlyList<DateTime?> taken, SlideshowCollage collage)
        {
            var pages = new List<int[]>();
            var sizes = SizesOf(collage);
            var turn = 0;
            var next = 0;
            while (next < taken.Count)
            {
                var wanted = sizes[turn++ % sizes.Length];
                var page = new List<int> { next };
                while (page.Count < wanted && next + 1 < taken.Count && SameEventAs(taken[next], taken[next + 1]))
                    page.Add(++next);

                pages.Add([.. page]);
                next++;
            }
            return pages;
        }

        private static int[] SizesOf(SlideshowCollage collage) => collage switch
        {
            SlideshowCollage.Two => [2],
            SlideshowCollage.Three => [3],
            SlideshowCollage.Four => [4],
            SlideshowCollage.Mixed => MixedSizes,
            _ => [1],
        };

        private static bool SameEventAs(DateTime? before, DateTime? after) =>
            before is not { } a || after is not { } b || (a - b).Duration() <= SameEvent;

        /// <summary>
        /// Where each of <paramref name="count"/> photos goes on a screen of <paramref name="width"/> by <paramref name="height"/>, with <paramref name="gap"/> pixels between them and round them: two side
        /// by side (one above the other on a screen that is taller than wide), three as one large and two small, four in a grid. The cells never overlap and never go beyond the screen.
        /// </summary>
        public static RectangleF[] Cells(int count, int width, int height, int gap)
        {
            count = Math.Clamp(count, 1, MostPhotos);
            gap = Math.Max(0, gap);
            float x = gap, y = gap;
            float w = Math.Max(1, width - 2f * gap), h = Math.Max(1, height - 2f * gap);
            var wide = width >= height;
            float halfW = Math.Max(1, (w - gap) / 2), halfH = Math.Max(1, (h - gap) / 2);

            switch (count)
            {
                case 1:
                    return [new RectangleF(x, y, w, h)];
                case 2:
                    return wide
                        ? [new RectangleF(x, y, halfW, h), new RectangleF(x + halfW + gap, y, halfW, h)]
                        : [new RectangleF(x, y, w, halfH), new RectangleF(x, y + halfH + gap, w, halfH)];
                case 3:
                    if (wide)
                    {
                        var large = Math.Max(1, (w - gap) * 0.6f);
                        var small = Math.Max(1, w - gap - large);
                        return [new RectangleF(x, y, large, h), new RectangleF(x + large + gap, y, small, halfH), new RectangleF(x + large + gap, y + halfH + gap, small, halfH)];
                    }
                    else
                    {
                        var large = Math.Max(1, (h - gap) * 0.6f);
                        var small = Math.Max(1, h - gap - large);
                        return [new RectangleF(x, y, w, large), new RectangleF(x, y + large + gap, halfW, small), new RectangleF(x + halfW + gap, y + large + gap, halfW, small)];
                    }
                default:
                    return
                    [
                        new RectangleF(x, y, halfW, halfH), new RectangleF(x + halfW + gap, y, halfW, halfH),
                        new RectangleF(x, y + halfH + gap, halfW, halfH), new RectangleF(x + halfW + gap, y + halfH + gap, halfW, halfH),
                    ];
            }
        }
    }
}
