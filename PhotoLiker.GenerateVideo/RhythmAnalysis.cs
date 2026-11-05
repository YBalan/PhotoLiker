namespace PhotoLiker.GenerateVideo
{
    /// <summary>
    /// What the rhythm of an audio file looks like: how strong the sound hits are along its length (what the bars of the Generate Video
    /// dialog show), and where the beats are. Made by <see cref="RhythmAnalyzer"/>; an experimental feature of the video.
    /// </summary>
    public sealed class RhythmAnalysis
    {
        /// <param name="hopSeconds">The time between two values of <paramref name="strength"/>.</param>
        /// <param name="strength">How strong a hit is at each step, from 0 (nothing) to 1 (the strongest); value <c>i</c> is the strength at the time <c>i * hopSeconds</c>, the same time line as the beats.</param>
        /// <param name="beats">The times of the beats in seconds, from the smallest; none when no clear beat was found.</param>
        /// <param name="bpm">Beats per minute; 0 when there are no beats.</param>
        /// <param name="durationSeconds">How long the audio is, as it was decoded.</param>
        public RhythmAnalysis(double hopSeconds, float[] strength, double[] beats, double bpm, double durationSeconds)
        {
            HopSeconds = hopSeconds;
            Strength = strength;
            Beats = beats;
            Bpm = bpm;
            DurationSeconds = durationSeconds;
        }

        public double HopSeconds { get; }

        public IReadOnlyList<float> Strength { get; }

        public IReadOnlyList<double> Beats { get; }

        public double Bpm { get; }

        public double DurationSeconds { get; }

        /// <summary>At least two beats were found: enough to cut on them.</summary>
        public bool HasBeats => Beats.Count >= 2;

        /// <summary>The usual time from one beat to the next (the middle one, so a missed or an extra beat does not move it).</summary>
        public double BeatPeriodSeconds
        {
            get
            {
                if (Beats.Count < 2)
                    return Bpm > 0 ? 60 / Bpm : 0;

                var gaps = new double[Beats.Count - 1];
                for (var i = 0; i < gaps.Length; i++)
                    gaps[i] = Beats[i + 1] - Beats[i];

                Array.Sort(gaps);
                return gaps[gaps.Length / 2];
            }
        }

        /// <summary>How long the audio is when it is played over and over (a beat that sits at the very end, or past it, does not overlap the next turn).</summary>
        private double LoopSeconds => Math.Max(DurationSeconds, Beats[^1] + 0.01);

        /// <summary>
        /// The time of the beat with this number. Past the last one the beats of the audio come again, one audio length later each time:
        /// that is what is heard when the audio is played over and over, and the best guess when it has ended.
        /// </summary>
        public double BeatTime(int index)
        {
            if (!HasBeats)
                throw new InvalidOperationException("There are no beats.");

            var count = Beats.Count;
            var length = LoopSeconds;
            var turns = Math.DivRem(index, count, out var rest);
            if (rest < 0)
            {
                rest += count;
                turns--;
            }

            return Beats[rest] + (turns * length);
        }

        /// <summary>The number of the beat that is the nearest to this time (the beats of <see cref="BeatTime"/>, past the end too; never below 0).</summary>
        public int NearestBeatIndex(double seconds)
        {
            if (!HasBeats)
                throw new InvalidOperationException("There are no beats.");

            var count = Beats.Count;
            var length = LoopSeconds;
            var turns = (int)Math.Floor(seconds / length);
            var inside = seconds - (turns * length);

            // The first beat that is not before the time, in this turn
            var low = 0;
            var high = count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (Beats[middle] < inside)
                    low = middle + 1;
                else
                    high = middle;
            }

            // Of that one and the one before it (which may be the last of the turn before), the nearer
            // (no beat has a number below 0: the one before the first is not a beat of the audio)
            var after = (turns * count) + low;
            var before = after - 1;
            if (before < 0)
                return after;

            return Math.Abs(BeatTime(after) - seconds) <= Math.Abs(seconds - BeatTime(before)) ? after : before;
        }

        /// <summary>
        /// The same rhythm taken <paramref name="octaves"/> times as fast (negative: as slow), for when the tracker took the beat for
        /// half of it or twice of it. Faster: a beat is put between each two; slower: every second beat is kept, of the two sets the
        /// one that hits harder.
        /// </summary>
        public RhythmAnalysis Scaled(int octaves)
        {
            if (!HasBeats || octaves == 0)
                return this;

            var beats = Beats.ToArray();
            for (var step = 0; step < Math.Abs(octaves); step++)
                beats = octaves > 0 ? Doubled(beats) : Halved(beats);

            return new RhythmAnalysis(HopSeconds, [.. Strength], beats, Bpm * Math.Pow(2, octaves), DurationSeconds);
        }

        private static double[] Doubled(double[] beats)
        {
            var result = new double[(beats.Length * 2) - 1];
            for (var i = 0; i < beats.Length; i++)
            {
                result[2 * i] = beats[i];
                if (i + 1 < beats.Length)
                    result[(2 * i) + 1] = (beats[i] + beats[i + 1]) / 2;
            }

            return result;
        }

        private double[] Halved(double[] beats)
        {
            if (beats.Length < 4)
                return beats;

            double Hit(double time)
            {
                var step = (int)Math.Round(time / HopSeconds);
                return step >= 0 && step < Strength.Count ? Strength[step] : 0;
            }

            var even = 0d;
            var odd = 0d;
            for (var i = 0; i < beats.Length; i++)
            {
                if (i % 2 == 0)
                    even += Hit(beats[i]);
                else
                    odd += Hit(beats[i]);
            }

            var start = even >= odd ? 0 : 1;
            return [.. beats.Where((_, i) => i % 2 == start)];
        }
    }
}
