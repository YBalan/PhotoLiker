using PhotoLiker.Core;

namespace PhotoLiker.GenerateVideo.Tests;

/// <summary>A pretend song for the rhythm tests: a kick on every beat, a snare on every second one and quiet hats between, as 16 bit mono samples.</summary>
internal static class RhythmTrack
{
    public static byte[] Make(double bpm, double seconds, double firstBeatSeconds = 0, bool withHats = true)
    {
        var rate = RhythmAnalyzer.SampleRate;
        var samples = new double[(int)(seconds * rate)];
        var random = new Random(7);
        var period = 60 / bpm;

        void Add(double start, Func<double, double> sound, double length)
        {
            var first = (int)(start * rate);
            for (var i = 0; i < (int)(length * rate) && first + i < samples.Length; i++)
            {
                if (first + i >= 0)
                    samples[first + i] += sound(i / (double)rate);
            }
        }

        var beat = 0;
        for (var time = firstBeatSeconds; time < seconds; time += period, beat++)
        {
            Add(time, t => 0.8 * Math.Sin(2 * Math.PI * (50 * t + (70 * (1 - Math.Exp(-t * 40)) / 40))) * Math.Exp(-t * 18), 0.25);
            if (beat % 2 == 1)
                Add(time, t => 0.4 * (random.NextDouble() - 0.5) * 2 * Math.Exp(-t * 25), 0.2);
            if (withHats)
                Add(time + (period / 2), t => 0.12 * (random.NextDouble() - 0.5) * 2 * Math.Exp(-t * 90), 0.06);
        }

        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(samples[i] * 32767, short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)(value & 0xFF);
            bytes[(2 * i) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }

    public static double[] BeatTimes(double bpm, double seconds, double firstBeatSeconds = 0)
    {
        var times = new List<double>();
        for (var time = firstBeatSeconds; time < seconds; time += 60 / bpm)
            times.Add(time);

        return [.. times];
    }

    public static RhythmAnalysis Analyze(byte[] pcm) => RhythmAnalyzer.Analyze(new MemoryStream(pcm));
}
