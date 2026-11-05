using PhotoLiker.Core;

namespace PhotoLiker.GenerateVideo.Tests;

public class RhythmAnalyzerTests
{
    private const double Tolerance = 0.03; // seconds: a beat found this close to a real one is on it

    /// <summary>The distance from a time to the nearest of the times.</summary>
    private static double Nearest(double time, IEnumerable<double> times) => times.Min(t => Math.Abs(t - time));

    [Theory]
    [InlineData(120, 0.0)]
    [InlineData(140, 0.25)]
    [InlineData(100, 0.4)]
    public void Analyze_ASteadySong_FindsItsTempoAndPutsTheBeatsOnTheKicks(double bpm, double first)
    {
        var analysis = RhythmTrack.Analyze(RhythmTrack.Make(bpm, 60, first));

        Assert.True(analysis.HasBeats);
        Assert.InRange(analysis.Bpm, bpm * 0.98, bpm * 1.02);

        var kicks = RhythmTrack.BeatTimes(bpm, 60, first);
        var onKicks = analysis.Beats.Count(beat => Nearest(beat, kicks) < Tolerance);
        Assert.True(onKicks >= 0.95 * analysis.Beats.Count, $"{onKicks} of {analysis.Beats.Count} beats are on a kick");

        // Every kick (but the first and last seconds, where the tracker starts and ends) has a beat
        var found = kicks.Where(kick => kick > 3 && kick < 57).Count(kick => Nearest(kick, analysis.Beats) < Tolerance);
        Assert.True(found >= 0.95 * kicks.Count(kick => kick > 3 && kick < 57), $"{found} kicks have a beat");
    }

    [Theory]
    [InlineData(90, 0.35)]
    [InlineData(75, 0.0)]
    [InlineData(170, 0.2)]
    public void Analyze_ASongWhoseBeatCanBeTakenForHalfOrDoubleTime_FindsOneOfThose_OnTheHits(double bpm, double first)
    {
        var analysis = RhythmTrack.Analyze(RhythmTrack.Make(bpm, 60, first));

        Assert.True(analysis.HasBeats);
        Assert.True(
            new[] { bpm, bpm * 2, bpm / 2 }.Any(candidate => Math.Abs(analysis.Bpm - candidate) < candidate * 0.03),
            $"{analysis.Bpm} bpm for a song of {bpm}");

        // Whatever the pick, the beats are on hits: a kick, or a hat half a beat after it
        var hits = RhythmTrack.BeatTimes(bpm * 2, 60, first);
        var onHits = analysis.Beats.Count(beat => Nearest(beat, hits) < Tolerance);
        Assert.True(onHits >= 0.95 * analysis.Beats.Count, $"{onHits} of {analysis.Beats.Count} beats are on a hit");
    }

    [Fact]
    public void Analyze_Silence_HasNoBeats_ButStillAStrengthAndALength()
    {
        var analysis = RhythmTrack.Analyze(new byte[22050 * 2 * 10]);

        Assert.False(analysis.HasBeats);
        Assert.Equal(0, analysis.Bpm);
        Assert.Equal(10, analysis.DurationSeconds, 3);
        Assert.NotEmpty(analysis.Strength);
        Assert.All(analysis.Strength, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Analyze_TooShortToHaveARhythm_HasNoBeats()
    {
        var analysis = RhythmTrack.Analyze(RhythmTrack.Make(120, 2));

        Assert.False(analysis.HasBeats);
    }

    [Fact]
    public void Analyze_NothingAtAll_IsLengthZero()
    {
        var analysis = RhythmTrack.Analyze([]);

        Assert.Equal(0, analysis.DurationSeconds);
        Assert.False(analysis.HasBeats);
        Assert.Empty(analysis.Strength);
    }

    [Fact]
    public void Analyze_Noise_HasNoBeats()
    {
        var random = new Random(3);
        var bytes = new byte[22050 * 2 * 20];
        for (var i = 0; i < bytes.Length; i += 2)
        {
            var value = (short)(random.Next(-6000, 6000));
            bytes[i] = (byte)(value & 0xFF);
            bytes[i + 1] = (byte)((value >> 8) & 0xFF);
        }

        Assert.False(RhythmTrack.Analyze(bytes).HasBeats);
    }

    [Fact]
    public void Analyze_TheStrengthIsFromZeroToOne_AndIsHighestAtTheHits()
    {
        var analysis = RhythmTrack.Analyze(RhythmTrack.Make(120, 20));

        Assert.All(analysis.Strength, value => Assert.InRange(value, 0f, 1f));
        Assert.Equal(1f, analysis.Strength.Max());

        // A kick at 10.0 s, a hat at 10.25, and at 10.4 nothing
        double Around(double seconds) => analysis.Strength.Skip((int)((seconds - 0.04) / analysis.HopSeconds)).Take(8).Max();
        Assert.True(Around(10.0) > 0.3);
        Assert.True(Around(10.4) < 0.1);
    }

    [Fact]
    public void Analyze_TheStrengthPeaksAtTheBeats_SoTheBarsAndTheBeatLinesAgree()
    {
        var analysis = RhythmTrack.Analyze(RhythmTrack.Make(120, 40, 0.1));
        Assert.True(analysis.HasBeats);

        // The mean strength at the beats, and at a few steps (about 12 ms each) before and after them: the top is at the beats
        double Mean(int offset) => analysis.Beats
            .Select(beat => (int)Math.Round(beat / analysis.HopSeconds) + offset)
            .Where(i => i >= 0 && i < analysis.Strength.Count)
            .Average(i => analysis.Strength[i]);

        var best = Enumerable.Range(-6, 13).MaxBy(Mean);
        Assert.InRange(best, -1, 1);
    }

    [Fact]
    public void Analyze_SamplesComeInAnyPieces_SameResult()
    {
        var bytes = RhythmTrack.Make(120, 30);

        var whole = RhythmAnalyzer.Analyze(new MemoryStream(bytes));
        var pieces = RhythmAnalyzer.Analyze(new SmallReads(bytes, 7)); // an odd number of bytes at a time: a sample is split between two reads

        Assert.Equal(whole.DurationSeconds, pieces.DurationSeconds);
        Assert.Equal(whole.Beats, pieces.Beats);
        Assert.Equal(whole.Bpm, pieces.Bpm);
    }

    [Fact]
    public void Analyze_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => RhythmAnalyzer.Analyze(new MemoryStream(RhythmTrack.Make(120, 10)), cancellation.Token));
    }

    [Fact]
    public void BuildArguments_AsksForMonoSamplesOfTheRightRateOnTheOutputPipe()
    {
        var arguments = RhythmAnalyzer.BuildArguments(@"C:\Music\a song.mp3");

        Assert.Contains("-i \"C:\\Music\\a song.mp3\"", arguments);
        Assert.Contains("-ac 1", arguments);
        Assert.Contains($"-ar {RhythmAnalyzer.SampleRate}", arguments);
        Assert.Contains("-f s16le", arguments);
        Assert.EndsWith("pipe:1", arguments);
        Assert.Contains("-vn", arguments);
    }

    /// <summary>A stream that gives a few bytes at a time, as a pipe does.</summary>
    private sealed class SmallReads(byte[] bytes, int size) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(Math.Min(size, count), bytes.Length - _position);
            Array.Copy(bytes, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
