using PhotoLiker.Core;

namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The <c>Random</c> value of <see cref="SlideshowLook"/>, <see cref="SlideshowBorder"/> and <see cref="SlideshowCollage"/>: what is left to chance when a setting says so. A look and a card are chosen for every photo by
    /// chance (the same file gets the same in the same show, see the <c>seed</c>), the pages of the collage hold one to four photos by chance. A setting that says anything else is what it says. The effect has nothing to
    /// do with it. Nothing here knows about windows.
    /// </summary>
    public static class RandomStyle
    {
        /// <summary>Of the photos, this share (one in this many) is put on a card.</summary>
        public const int OneCardIn = 3;

        /// <summary>The look of the photo with this <paramref name="key"/>: any of the looks (a photo may stay as it is), the table's too when there is one.</summary>
        public static SlideshowLook Look(string key, int seed, bool hasTable) => LookGrade.Random(key, seed, hasTable);

        /// <summary>Whether the photo with this <paramref name="key"/> is put on a card: one photo in <see cref="OneCardIn"/>.</summary>
        public static bool Card(string key, int seed) => LookGrade.Mix(key + "|card", seed) % OneCardIn == 0;

        /// <summary>The pages of a collage that is left to chance: see <see cref="CollagePlan.RandomPages"/>.</summary>
        public static IReadOnlyList<int[]> Pages(IReadOnlyList<DateTime?> taken, int seed) => CollagePlan.RandomPages(taken, seed);
    }
}
