namespace PhotoLiker.GenerateVideo
{
    /// <summary>
    /// Finds the beats of an audio file: ffmpeg turns it into plain samples, how hard the sound hits is worked out along it (the change
    /// of the spectrum from one moment to the next), the tempo is picked from how that repeats, and the beats are the hits that fit
    /// that tempo best (dynamic programming, as in D. Ellis, "Beat Tracking by Dynamic Programming", 2007). No library: it is some
    /// hundreds of lines, and the file is read with the ffmpeg that makes the video.
    /// </summary>
    public static class RhythmAnalyzer
    {
        /// <summary>The audio is looked at as 22.05 kHz mono; that is plenty for the hits of drums.</summary>
        public const int SampleRate = 22050;

        /// <summary>Samples in the piece of sound that one value of the strength is worked out from.</summary>
        internal const int FrameSize = 1024;

        /// <summary>Samples from one piece to the next: about 11.6 ms, which is the precision of the beats.</summary>
        internal const int HopSize = 256;

        public const double HopSeconds = HopSize / (double)SampleRate;

        /// <summary>The spectrum is added up in this many bands, spaced as the ear hears pitch (mel), so that a low drum counts for as much as a bright cymbal.</summary>
        internal const int Bands = 40;

        private const double LowestHz = 30;
        private const double HighestHz = 9000;

        /// <summary>A longer file is cut here (a minute of samples is 2.6 MB for ffmpeg to send and for the loop to read).</summary>
        public const int MaxSeconds = 3600;

        /// <summary>Slower and faster than this is not looked for.</summary>
        internal const double MinBpm = 55;
        internal const double MaxBpm = 215;

        /// <summary>The tempo that is the most likely when the music does not say (people tap along at about this).</summary>
        private const double LikelyBpm = 120;

        /// <summary>How far, in octaves of tempo, from the likely one a tempo is still taken as likely (the width of the bell).</summary>
        private const double LikelySpreadOctaves = 1.0;

        /// <summary>How much a beat has to be where the tempo says (higher: more regular). The value of the paper's authors' code.</summary>
        private const double Tightness = 100;

        /// <summary>The repeat of the hits at the best beat period, as a part of the repeat at nothing (1): below this there is no clear beat.</summary>
        internal const double MinConfidence = 0.12;

        /// <summary>
        /// Added to the time of the middle of the piece of sound where a hit is found: the hit is seen a little before that (about 11 ms,
        /// from clicks at known times, see the tests), not at the middle.
        /// </summary>
        internal const double HitAfterMiddleSeconds = 0.011;

        /// <summary>Audio shorter than this has no rhythm to speak of.</summary>
        private const double MinSeconds = 3;

        /// <summary>Reads the file with ffmpeg and finds its rhythm.</summary>
        /// <exception cref="InvalidOperationException">ffmpeg is not there, or cannot read the file as audio.</exception>
        public static async Task<RhythmAnalysis> AnalyzeAsync(string audioPath, CancellationToken cancellationToken)
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = VideoGenerationService.ResolveFfmpegExecutablePath(),
                    Arguments = BuildArguments(audioPath),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(VideoGenerationService.FfmpegNotFoundMessage, ex);
            }

            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                var analysis = await Task.Run(() => Analyze(process.StandardOutput.BaseStream, cancellationToken), cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                if (analysis.DurationSeconds <= 0)
                {
                    var lastLine = (await error).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                    throw new InvalidOperationException(GenerateVideoStrings.AudioUnreadable + (lastLine is null ? "." : ": " + lastLine));
                }

                return analysis;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* it ended by itself */ }
                throw;
            }
        }

        internal static string BuildArguments(string audioPath) =>
            $"-hide_banner -loglevel error -i \"{audioPath}\" -vn -t {MaxSeconds} -ac 1 -ar {SampleRate} -f s16le pipe:1";

        /// <summary>Finds the rhythm of mono samples (16 bits, little endian, <see cref="SampleRate"/> a second) read from a stream.</summary>
        public static RhythmAnalysis Analyze(Stream pcm, CancellationToken cancellationToken = default)
        {
            var onset = OnsetStrength(pcm, cancellationToken, out var sampleCount);
            var duration = sampleCount / (double)SampleRate;
            var display = ForDisplay(onset);

            if (duration < MinSeconds || onset.Length < 8)
                return new RhythmAnalysis(HopSeconds, display, [], 0, duration);

            var (bpm, confidence) = EstimateTempo(onset);
            if (bpm <= 0 || confidence < MinConfidence)
                return new RhythmAnalysis(HopSeconds, display, [], 0, duration);

            var frames = TrackBeats(onset, 60 / bpm / HopSeconds);
            var beats = frames.Select(frame => (((frame * HopSize) + (FrameSize / 2.0)) / SampleRate) + HitAfterMiddleSeconds).Where(time => time >= 0).ToArray();
            return beats.Length < 2
                ? new RhythmAnalysis(HopSeconds, display, [], 0, duration)
                : new RhythmAnalysis(HopSeconds, display, beats, bpm, duration);
        }

        /// <summary>
        /// How hard the sound hits at each step of <see cref="HopSize"/> samples: the rise of the spectrum (in a logarithmic scale, as the ear
        /// takes it) since the step before, added over the frequencies. Worked out while the samples are read, so none are kept.
        /// </summary>
        internal static float[] OnsetStrength(Stream pcm, CancellationToken cancellationToken, out long sampleCount)
        {
            const int bins = FrameSize / 2;
            var filters = MelFilters();
            var window = new double[FrameSize];
            for (var i = 0; i < FrameSize; i++)
                window[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / FrameSize));

            var fft = new Fft(FrameSize);
            var re = new double[FrameSize];
            var im = new double[FrameSize];
            var previous = new double[Bands];
            var power = new double[bins];
            var havePrevious = false;
            var onset = new List<float>();

            var frame = new float[FrameSize];
            var filled = 0;
            sampleCount = 0;

            var chunk = new byte[1 << 16];
            var carried = false;
            byte carry = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var offset = 0;
                if (carried)
                    chunk[offset++] = carry;

                var read = pcm.Read(chunk, offset, chunk.Length - offset);
                if (read <= 0)
                    break;

                var bytes = offset + read;
                var pairs = bytes / 2;
                carried = (bytes & 1) == 1;
                if (carried)
                    carry = chunk[bytes - 1];

                for (var p = 0; p < pairs; p++)
                {
                    frame[filled++] = (short)(chunk[2 * p] | (chunk[(2 * p) + 1] << 8)) / 32768f;
                    sampleCount++;
                    if (filled < FrameSize)
                        continue;

                    for (var i = 0; i < FrameSize; i++)
                    {
                        re[i] = frame[i] * window[i];
                        im[i] = 0;
                    }

                    fft.Transform(re, im);

                    for (var k = 0; k < bins; k++)
                        power[k] = (re[k] * re[k]) + (im[k] * im[k]);

                    // How much louder (in decibels) each band is than it was a step ago; a band that gets quieter counts for nothing
                    var rise = 0d;
                    for (var band = 0; band < Bands; band++)
                    {
                        var (first, weights) = filters[band];
                        var energy = 0d;
                        for (var i = 0; i < weights.Length; i++)
                            energy += weights[i] * power[first + i];

                        var level = 10 * Math.Log10(energy + QuietPower);
                        if (havePrevious && level > previous[band])
                            rise += level - previous[band];

                        previous[band] = level;
                    }

                    onset.Add(havePrevious ? (float)(rise / Bands) : 0f);
                    havePrevious = true;

                    Array.Copy(frame, HopSize, frame, 0, FrameSize - HopSize);
                    filled = FrameSize - HopSize;
                }
            }

            return [.. onset];
        }

        /// <summary>The power below which a band counts as silent (about -30 dB when a full-scale tone is about 48 dB), so that the noise of a quiet file is not a rhythm.</summary>
        private const double QuietPower = 1e-3;

        /// <summary>For each of the <see cref="Bands"/>: the first bin of the spectrum it covers and the weight of each bin from there (a triangle).</summary>
        private static (int First, double[] Weights)[] MelFilters()
        {
            static double ToMel(double hz) => 2595 * Math.Log10(1 + (hz / 700));
            static double ToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);

            var binHz = SampleRate / (double)FrameSize;
            var edges = new double[Bands + 2];
            for (var i = 0; i < edges.Length; i++)
                edges[i] = ToHz(ToMel(LowestHz) + ((ToMel(HighestHz) - ToMel(LowestHz)) * i / (Bands + 1)));

            var filters = new (int, double[])[Bands];
            for (var band = 0; band < Bands; band++)
            {
                var low = edges[band];
                var centre = edges[band + 1];
                var high = edges[band + 2];
                var first = Math.Max(1, (int)Math.Floor(low / binHz));
                var last = Math.Min((FrameSize / 2) - 1, (int)Math.Ceiling(high / binHz));
                var weights = new double[Math.Max(1, last - first + 1)];
                for (var bin = first; bin <= last; bin++)
                {
                    var hz = bin * binHz;
                    weights[bin - first] = hz <= centre ? Math.Max(0, (hz - low) / (centre - low)) : Math.Max(0, (high - hz) / (high - centre));
                }

                // A band narrower than a bin (the lowest ones) takes the bin that is the nearest to its middle
                if (weights.All(w => w <= 0))
                {
                    first = Math.Max(1, (int)Math.Round(centre / binHz));
                    weights = [1];
                }

                filters[band] = (first, weights);
            }

            return filters;
        }

        /// <summary>The strength as the bars show it: from 0 to 1, where the hardest hits (leaving out the very few loudest) are 1, on the time line of the beats.</summary>
        private static float[] ForDisplay(float[] onset)
        {
            if (onset.Length == 0)
                return [];

            var sorted = (float[])onset.Clone();
            Array.Sort(sorted);
            var top = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.995))];
            if (top <= 0)
                return new float[onset.Length + DisplayDelaySteps];

            // Each value goes to the time its hit is heard at (the middle of its piece of sound, and a little after, as the beats are), so
            // that the bars and the beats of an analysis are on one time line: value i is the strength at i * HopSeconds
            return [.. new float[DisplayDelaySteps].Concat(onset.Select(value => Math.Min(1f, value / top)))];
        }

        /// <summary>The steps a value of the onset strength is moved later by, to be at the time of its hit: half a piece of sound and <see cref="HitAfterMiddleSeconds"/>.</summary>
        private static readonly int DisplayDelaySteps = (int)Math.Round(((FrameSize / 2.0 / SampleRate) + HitAfterMiddleSeconds) / HopSeconds);

        /// <summary>The tempo (beats per minute) the hits repeat at, and how sure that is (0 to 1).</summary>
        internal static (double Bpm, double Confidence) EstimateTempo(float[] onset)
        {
            var count = onset.Length;
            var mean = onset.Average();
            var x = new double[count];
            var energy = 0d;
            for (var i = 0; i < count; i++)
            {
                x[i] = onset[i] - mean;
                energy += x[i] * x[i];
            }

            if (energy <= 1e-12)
                return (0, 0);

            var minLag = (int)Math.Floor(60 / MaxBpm / HopSeconds);
            var maxLag = (int)Math.Ceiling(60 / MinBpm / HopSeconds);
            if (maxLag + 2 >= count)
                return (0, 0);

            // How much the hits at one time are like those a lag later, 1 being the same (the unbiased version: a long lag has fewer pairs)
            var average = energy / count;
            var autocorrelation = new double[maxLag + 2];
            for (var lag = Math.Max(1, minLag - 1); lag <= maxLag + 1; lag++)
            {
                var sum = 0d;
                for (var i = 0; i + lag < count; i++)
                    sum += x[i] * x[i + lag];

                autocorrelation[lag] = sum / (count - lag) / average;
            }

            var best = 0;
            var bestScore = double.MinValue;
            for (var lag = minLag; lag <= maxLag; lag++)
            {
                var bpm = 60 / (lag * HopSeconds);
                var octaves = Math.Log2(bpm / LikelyBpm) / LikelySpreadOctaves;
                var score = autocorrelation[lag] * Math.Exp(-0.5 * octaves * octaves);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = lag;
                }
            }

            if (best == 0)
                return (0, 0);

            // Between two steps: the top of the curve through the best one and its neighbours
            var refined = (double)best;
            if (best > minLag && best < maxLag)
            {
                var left = autocorrelation[best - 1];
                var middle = autocorrelation[best];
                var right = autocorrelation[best + 1];
                var bend = left - (2 * middle) + right;
                if (bend < 0)
                    refined = best + (0.5 * (left - right) / bend);
            }

            return (60 / (refined * HopSeconds), Math.Clamp(autocorrelation[best], 0, 1));
        }

        /// <summary>
        /// The steps of the beats: the ones where the hits are strong and which are, as far as can be had, one period apart. For each step the
        /// best score of a row of beats that ends there is kept (a hit adds its strength, a gap that is not a period costs), and the best
        /// row is read back from its end.
        /// </summary>
        internal static List<int> TrackBeats(float[] onset, double periodSteps)
        {
            var count = onset.Length;
            var period = Math.Max(2, periodSteps);

            // Hits are not exactly on one step: each is spread a little (a bell a thirty-second of a period wide), and the strengths are
            // taken as parts of their spread, so that the tightness means the same for a loud or a quiet piece
            var deviation = Math.Sqrt(onset.Select(v => (double)v * v).Average() - Math.Pow(onset.Average(), 2));
            var scale = deviation > 1e-9 ? 1 / deviation : 1;
            var reach = (int)Math.Round(period);
            var bell = new double[(2 * reach) + 1];
            for (var i = -reach; i <= reach; i++)
            {
                var z = i * 32.0 / period;
                bell[i + reach] = Math.Exp(-0.5 * z * z);
            }

            var local = new double[count];
            for (var i = 0; i < count; i++)
            {
                var sum = 0d;
                for (var k = Math.Max(0, i - reach); k <= Math.Min(count - 1, i + reach); k++)
                    sum += onset[k] * scale * bell[k - i + reach];

                local[i] = sum;
            }

            var strongest = local.Max();
            var cumulative = new double[count];
            var link = new int[count];
            var first = true;
            var from = (int)Math.Round(period / 2);
            var to = (int)(2 * period);
            for (var i = 0; i < count; i++)
            {
                var bestScore = double.NegativeInfinity;
                var bestAt = -1;
                for (var back = from; back <= to; back++)
                {
                    var at = i - back;
                    if (at < 0)
                        break;

                    var cost = Math.Log(back) - Math.Log(period);
                    var score = cumulative[at] - (Tightness * cost * cost);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAt = at;
                    }
                }

                cumulative[i] = bestAt >= 0 ? local[i] + bestScore : local[i];

                // A beat with nothing before it is only a start when it is a real hit
                if (first && local[i] < 0.01 * strongest)
                {
                    link[i] = -1;
                }
                else
                {
                    link[i] = bestAt;
                    first = false;
                }
            }

            // The row ends at the last top of the score that is not a small one
            var tops = new List<int>();
            for (var i = 1; i < count - 1; i++)
            {
                if (cumulative[i] > cumulative[i - 1] && cumulative[i] >= cumulative[i + 1])
                    tops.Add(i);
            }

            if (tops.Count == 0)
                return [];

            var sortedTops = tops.Select(i => cumulative[i]).OrderBy(v => v).ToArray();
            var threshold = 0.5 * sortedTops[sortedTops.Length / 2];
            var last = tops.LastOrDefault(i => cumulative[i] >= threshold, tops[^1]);

            var beats = new List<int>();
            for (var at = last; at >= 0; at = link[at])
            {
                beats.Add(at);
                if (link[at] < 0)
                    break;
            }

            beats.Reverse();
            return Trimmed(beats, local);
        }

        /// <summary>Takes off the beats at the start and the end of the row that are not on a hit (silence before the music, a fade out).</summary>
        private static List<int> Trimmed(List<int> beats, double[] local)
        {
            if (beats.Count < 5)
                return beats;

            // The strength of the beats a little smoothed, and the beats kept from the first to the last that are above half the usual one
            var smooth = new double[beats.Count];
            for (var i = 0; i < beats.Count; i++)
            {
                var sum = 0d;
                var weights = 0d;
                for (var k = -2; k <= 2; k++)
                {
                    var at = i + k;
                    if (at < 0 || at >= beats.Count)
                        continue;

                    var weight = 0.5 + (0.5 * Math.Cos(Math.PI * k / 3));
                    sum += weight * local[beats[at]];
                    weights += weight;
                }

                smooth[i] = sum / weights;
            }

            var threshold = 0.5 * Math.Sqrt(smooth.Select(v => v * v).Average());
            var start = 0;
            while (start < smooth.Length && smooth[start] <= threshold)
                start++;

            var end = smooth.Length - 1;
            while (end > start && smooth[end] <= threshold)
                end--;

            return start > end ? beats : beats.GetRange(start, end - start + 1);
        }

        /// <summary>A fast Fourier transform of a power of two number of values, in place.</summary>
        private sealed class Fft
        {
            private readonly int _size;
            private readonly int[] _reversed;
            private readonly double[] _cos;
            private readonly double[] _sin;

            public Fft(int size)
            {
                _size = size;
                _reversed = new int[size];
                var bits = (int)Math.Log2(size);
                for (var i = 0; i < size; i++)
                {
                    var reversed = 0;
                    for (var bit = 0; bit < bits; bit++)
                    {
                        if ((i & (1 << bit)) != 0)
                            reversed |= 1 << (bits - 1 - bit);
                    }

                    _reversed[i] = reversed;
                }

                _cos = new double[size / 2];
                _sin = new double[size / 2];
                for (var i = 0; i < size / 2; i++)
                {
                    _cos[i] = Math.Cos(2 * Math.PI * i / size);
                    _sin[i] = -Math.Sin(2 * Math.PI * i / size);
                }
            }

            public void Transform(double[] re, double[] im)
            {
                for (var i = 0; i < _size; i++)
                {
                    var j = _reversed[i];
                    if (j > i)
                    {
                        (re[i], re[j]) = (re[j], re[i]);
                        (im[i], im[j]) = (im[j], im[i]);
                    }
                }

                for (var length = 2; length <= _size; length <<= 1)
                {
                    var half = length / 2;
                    var stride = _size / length;
                    for (var start = 0; start < _size; start += length)
                    {
                        for (var k = 0; k < half; k++)
                        {
                            var c = _cos[k * stride];
                            var s = _sin[k * stride];
                            var a = start + k;
                            var b = a + half;
                            var tr = (re[b] * c) - (im[b] * s);
                            var ti = (re[b] * s) + (im[b] * c);
                            re[b] = re[a] - tr;
                            im[b] = im[a] - ti;
                            re[a] += tr;
                            im[a] += ti;
                        }
                    }
                }
            }
        }
    }
}
