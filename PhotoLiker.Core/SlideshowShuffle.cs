namespace PhotoLiker.Core
{
    /// <summary>
    /// The order of the random slideshow: the places of the list in a random order, every place once before any comes again (a bag that is shuffled
    /// anew when it is empty), and never the same place twice in a row, also at the seam of two bags. The next few picks can be looked at before they are
    /// taken, so that the photos can be read from the disk while the one before them is still on the screen.
    /// </summary>
    public sealed class SlideshowShuffle
    {
        private readonly Random _random;
        private readonly int[] _bag;
        private int _roundLength;            // how many places of the bag are in this round
        private int _drawn;                  // how many of them were handed out
        private int _last;                   // the place handed out last, into the plan
        private int _onTheScreen;            // the photo shown before the first pick: it has had its turn in the first round
        private readonly List<int> _planned = [];

        /// <param name="shown">The place that is on the screen when the slideshow begins, if any: it is not picked again in the first round.</param>
        public SlideshowShuffle(int count, Random? random = null, int shown = -1)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            _random = random ?? Random.Shared;
            _bag = new int[count];
            for (var i = 0; i < count; i++)
                _bag[i] = i;

            _onTheScreen = shown >= 0 && shown < count ? shown : -1;
            _last = _onTheScreen;
        }

        public int Count => _bag.Length;

        /// <summary>The places that <see cref="Take"/> will give next, in that order. Looking does not use them up.</summary>
        public IReadOnlyList<int> Upcoming(int howMany)
        {
            while (_planned.Count < howMany)
                _planned.Add(Draw());
            return _planned.GetRange(0, howMany);
        }

        /// <summary>
        /// The next place. Not <paramref name="avoid"/> (the photo on the screen now, which a key may have moved to a place that was planned),
        /// unless it is the only photo there is.
        /// </summary>
        public int Take(int avoid)
        {
            var place = TakePlanned();
            if (place == avoid && Count > 1)
                place = TakePlanned();
            return place;
        }

        private int TakePlanned()
        {
            if (_planned.Count == 0)
                return Draw();

            var place = _planned[0];
            _planned.RemoveAt(0);
            return place;
        }

        private int Draw()
        {
            while (_drawn >= _roundLength)
                Refill();

            _last = _bag[_drawn++];
            return _last;
        }

        private void Refill()
        {
            _random.Shuffle(_bag);
            _roundLength = _bag.Length;
            _drawn = 0;

            if (_onTheScreen >= 0)
            {
                // The first round leaves out the photo that was on the screen at the start
                var at = Array.IndexOf(_bag, _onTheScreen);
                (_bag[at], _bag[^1]) = (_bag[^1], _bag[at]);
                _roundLength--;
                _onTheScreen = -1;
            }

            // The first place of the new bag is the last of the old one: swap it with the next so that it is not shown twice in a row
            if (_roundLength > 1 && _bag[0] == _last)
                (_bag[0], _bag[1]) = (_bag[1], _bag[0]);
        }
    }
}
