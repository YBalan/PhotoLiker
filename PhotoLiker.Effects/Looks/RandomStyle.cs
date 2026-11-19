using PhotoLiker.Core;

namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The rule that goes with <see cref="SlideshowEffect.Random"/>: when the effect is random, a look, a border and a collage that are left at <c>None</c> are random too. A look and a card are chosen for every photo by
    /// chance (the same one each time the same photo is made again in a show, see the <c>seed</c>), the pages of the collage have one to four photos by chance. A look, a border or a collage that is set to something
    /// else is what it is set to: only <c>None</c> is open to chance. Nothing here knows about windows.
    /// </summary>
    public static class RandomStyle
    {
        /// <summary>Of the photos, this share (one in this many) is put on a card.</summary>
        public const int OneCardIn = 3;

        /// <summary>Whether a look, border or collage that is set to <c>None</c> is left to chance: when the effect is <see cref="SlideshowEffect.Random"/>.</summary>
        public static bool IsRandom(SlideshowEffect effect) => effect == SlideshowEffect.Random;

        /// <summary>The look of the photo with this <paramref name="key"/>: any of the looks (a photo may stay as it is), the table's too when there is one.</summary>
        public static SlideshowLook Look(string key, int seed, bool hasTable) => LookGrade.Random(key, seed, hasTable);

        /// <summary>Whether the photo with this <paramref name="key"/> is put on a card: one photo in <see cref="OneCardIn"/>.</summary>
        public static bool Card(string key, int seed) => LookGrade.Mix(key + "|card", seed) % OneCardIn == 0;

        /// <summary>The pages of the collage of a show with a random effect and no collage set: see <see cref="CollagePlan.RandomPages"/>.</summary>
        public static IReadOnlyList<int[]> Pages(IReadOnlyList<DateTime?> taken, int seed) => CollagePlan.RandomPages(taken, seed);
    }
}
