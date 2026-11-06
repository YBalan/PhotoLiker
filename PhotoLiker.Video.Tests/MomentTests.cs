using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

/// <summary>Taking pictures out of a video, and keeping the moments that say something new. The model is replaced by vectors made of the colour of the picture.</summary>
public class MomentSelectorTests
{
    private static float[] Vector(float x, float y) => [x, y, 0f];

    [Fact]
    public void TheFirstPicture_IsAlwaysAMoment() => Assert.True(new MomentSelector().Keep(Vector(1, 0), 0));

    [Fact]
    public void APictureLikeTheLastKept_IsNot()
    {
        var selector = new MomentSelector();
        selector.Keep(Vector(1, 0), 0);

        Assert.False(selector.Keep(Vector(0.99f, 0.14f), 2)); // cosine 0.99
    }

    [Fact]
    public void APictureUnlikeTheLastKept_Is()
    {
        var selector = new MomentSelector();
        selector.Keep(Vector(1, 0), 0);

        Assert.True(selector.Keep(Vector(0.5f, 0.87f), 2)); // cosine 0.5
    }

    [Fact]
    public void ASlowChange_AddsUp_BecauseTheLastKeptIsTheStandard()
    {
        // Every step is a small turn (cosine 0.97 to the one before), but twelve steps make a quarter turn: pictures that were only compared
        // with their neighbours would never be told apart
        var selector = new MomentSelector();
        var kept = 0;
        for (var step = 0; step < 24; step++)
        {
            var angle = step * Math.PI / 2 / 24;
            if (selector.Keep(Vector((float)Math.Cos(angle), (float)Math.Sin(angle)), step * 2))
                kept++;
        }

        Assert.InRange(kept, 3, 24);
        Assert.True(kept < 24); // not every one
    }

    [Fact]
    public void AVideoThatDoesNotChange_StillHasAMomentNowAndThen()
    {
        var selector = new MomentSelector(maxGapSeconds: 15);
        var times = new List<double>();
        for (var t = 0.0; t <= 60; t += 2)
        {
            if (selector.Keep(Vector(1, 0), t))
                times.Add(t);
        }

        Assert.Equal([0, 16, 32, 48], times); // the first picture after 15 seconds
    }

    [Fact]
    public void TheDefaults_AreWhatWasMeasured()
    {
        Assert.Equal(0.90f, MomentSelector.DefaultSimilarity);
        Assert.Equal(15, MomentSelector.DefaultMaxGapSeconds);
    }
}

public class VideoAnalyzerTests
{
    private static readonly ClipModelInfo Model = ClipModels.Default;

    // A flat picture of one grey level: the "model" below turns its brightness into one of three directions
    private static VideoFrame Frame(double seconds, byte level) =>
        new(seconds, new DecodedPhoto(64, 48, Enumerable.Repeat(level, 64 * 48 * 3).ToArray()));

    private static float[][] FakeModel(float[] inputs, int count)
    {
        var length = Model.InputLength;
        var vectors = new float[count][];
        for (var i = 0; i < count; i++)
        {
            // the first number of a prepared picture is its red channel, normalized: bright and dark give other directions
            var value = inputs[i * length];
            vectors[i] = value < -1.0f ? [1f, 0f, 0f] : value < 0.6f ? [0f, 1f, 0f] : [0f, 0f, 1f];
        }
        return vectors;
    }

    [Fact]
    public void PicturesThatChange_BecomeMoments_EachWithAThumbnailAndItsTime()
    {
        var frames = new[] { Frame(0, 0), Frame(2, 0), Frame(4.03, 128), Frame(6, 128), Frame(8, 255) };

        var moments = VideoAnalyzer.Analyze(frames, Model, FakeModel, new MomentSelector(), CancellationToken.None);

        Assert.Equal([0L, 4030L, 8000L], moments.Select(m => m.TimeMs));
        Assert.All(moments, m =>
        {
            Assert.NotNull(m.Thumbnail);
            Assert.Equal(0xFF, m.Thumbnail![0]);
            Assert.Equal(0xD8, m.Thumbnail[1]); // a JPEG
        });
    }

    [Fact]
    public void ManyPictures_GoThroughTheModelInBatches()
    {
        var batches = new List<int>();
        var frames = Enumerable.Range(0, 40).Select(i => Frame(i * 2, 0));

        VideoAnalyzer.Analyze(frames, Model, (inputs, count) => { batches.Add(count); return FakeModel(inputs, count); }, new MomentSelector(), CancellationToken.None);

        Assert.Equal([16, 16, 8], batches);
    }

    [Fact]
    public void TheModel_IsToldHowFarTheVideoHasGot()
    {
        var reached = new List<double>();
        var frames = Enumerable.Range(0, 20).Select(i => Frame(i * 2, 0));

        VideoAnalyzer.Analyze(frames, Model, FakeModel, new MomentSelector(), CancellationToken.None, reached.Add);

        Assert.Equal([30.0, 38.0], reached); // after the batch of 16 (the last at 30 s) and the rest (the last at 38 s)
    }

    [Fact]
    public void FacesAreLookedForOnlyInThePicturesThatAreKept_AndComeWithTheirMoment()
    {
        var asked = new List<byte>();
        var frames = new[] { Frame(0, 0), Frame(2, 0), Frame(4, 128), Frame(6, 128), Frame(8, 255) };
        IReadOnlyList<MomentFace> Faces(DecodedPhoto picture)
        {
            lock (asked)
                asked.Add(picture.Bgr[0]);
            return [new MomentFace(0.1f, 0.2f, 0.3f, 0.4f, 0.9f, [picture.Bgr[0], 1f])];
        }

        var moments = VideoAnalyzer.Analyze(frames, Model, FakeModel, new MomentSelector(), CancellationToken.None, findFaces: Faces, faceWorkers: 3);

        Assert.Equal(3, moments.Count);
        Assert.Equal(new byte[] { 0, 128, 255 }, asked.Order()); // not the two pictures that were only like the one before
        Assert.Equal([0f, 128f, 255f], moments.Select(m => m.Faces!.Single().Embedding[0])); // each moment has the faces of its own picture, in order
    }

    [Fact]
    public void WithNoFaceFinder_TheMomentsHaveNoFaces()
    {
        var moments = VideoAnalyzer.Analyze([Frame(0, 0)], Model, FakeModel, new MomentSelector(), CancellationToken.None);

        Assert.Null(Assert.Single(moments).Faces);
    }
    [Fact]
    public void ANoVideo_HasNoMoments() =>
        Assert.Empty(VideoAnalyzer.Analyze([], Model, FakeModel, new MomentSelector(), CancellationToken.None));

    [Fact]
    public void Stopping_StopsTheAnalysis()
    {
        using var stop = new CancellationTokenSource();
        var frames = Enumerable.Range(0, 100).Select(i =>
        {
            if (i == 5)
                stop.Cancel();
            return Frame(i * 2, 0);
        });

        Assert.Throws<OperationCanceledException>(() => VideoAnalyzer.Analyze(frames, Model, FakeModel, new MomentSelector(), stop.Token));
    }

    [Fact]
    public void KeyFramesFarApart_AreSaidSo_SoThatEveryPictureIsTakenInstead()
    {
        // A key frame every 10 seconds: no way to find a time in the video with only those
        var frames = Enumerable.Range(0, 10).Select(i => Frame(i * 10, 0));

        Assert.Throws<SparseKeyFramesException>(() => VideoAnalyzer.Analyze(frames, Model, FakeModel, new MomentSelector(), CancellationToken.None, keyFramesOnly: true));
    }

    [Fact]
    public void KeyFramesCloseTogether_AreFine()
    {
        var frames = Enumerable.Range(0, 10).Select(i => Frame(i * 2, (byte)(i * 25)));

        var moments = VideoAnalyzer.Analyze(frames, Model, FakeModel, new MomentSelector(), CancellationToken.None, keyFramesOnly: true);

        Assert.NotEmpty(moments);
    }

    [Fact]
    public void AShortVideoWithFewKeyFrames_IsNotCalledSparse()
    {
        var frames = new[] { Frame(0, 0), Frame(9, 50) };

        var moments = VideoAnalyzer.Analyze(frames, Model, FakeModel, new MomentSelector(), CancellationToken.None, keyFramesOnly: true);

        Assert.NotEmpty(moments);
    }
}

public class VideoFramesTests
{
    private static VideoInfo Info(int width, int height, int rotation = 0, string codec = "h264") =>
        new(10, width, height, rotation, 30, codec, null, 0, null, null, null, null);

    [Theory]
    [InlineData(1920, 1080, 0, 640, 360)]
    [InlineData(1080, 1920, 0, 360, 640)]   // stored upright
    [InlineData(1920, 1080, 90, 360, 640)]  // stored sideways, shown upright
    [InlineData(1920, 1080, 270, 360, 640)]
    [InlineData(1920, 1080, 180, 640, 360)]
    [InlineData(640, 480, 0, 640, 480)]
    [InlineData(320, 240, 0, 320, 240)]     // not enlarged
    public void ThePictures_AreAsTheVideoIsSeen_NoBiggerThanTheLimit(int width, int height, int rotation, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), VideoFrames.SizeOf(Info(width, height, rotation)));

    [Fact]
    public void AVideoWithNoSize_HasNoPictures() =>
        Assert.Throws<InvalidDataException>(() => VideoFrames.SizeOf(Info(0, 0)));

    [Theory]
    [InlineData("mjpeg", FrameSampling.EveryFrame)]
    [InlineData("MJPEG", FrameSampling.EveryFrame)]
    [InlineData("prores", FrameSampling.EveryFrame)]
    [InlineData("h264", FrameSampling.KeyFrames)]
    [InlineData("hevc", FrameSampling.KeyFrames)]
    [InlineData("vp9", FrameSampling.KeyFrames)]
    public void OnlyACodecOfWholePictures_NeedsEveryPicture(string codec, FrameSampling expected) =>
        Assert.Equal(expected, VideoFrames.SamplingFor(Info(640, 480, codec: codec)));

    [Fact]
    public void TheArguments_SkipToKeyFrames_OnlyWhenAsked()
    {
        var keys = VideoFrames.Arguments(@"C:\v\a.mp4", 640, 360, FrameSampling.KeyFrames);
        var all = VideoFrames.Arguments(@"C:\v\a.avi", 640, 480, FrameSampling.EveryFrame);

        Assert.Contains("-skip_frame", keys);
        Assert.DoesNotContain("-skip_frame", all);
        Assert.Equal(@"C:\v\a.mp4", keys[Array.IndexOf(keys, "-i") + 1]);
        Assert.Equal("pipe:1", keys[^1]);
        Assert.Contains("bgr24", keys);
        Assert.Contains("rawvideo", keys);
    }

    [Fact]
    public void TheFilter_TakesAPictureTwoSecondsAfterTheLast_ScalesAndSaysWhere()
    {
        var arguments = VideoFrames.Arguments(@"C:\v\a.mp4", 640, 360, FrameSampling.KeyFrames);
        var filter = arguments[Array.IndexOf(arguments, "-vf") + 1];

        Assert.Equal("select='isnan(prev_selected_t)+gte(t-prev_selected_t,2)',scale=640:360,showinfo", filter);
    }

    [Fact]
    public void ThePathWithSpacesAndLetters_GoesAsOneArgument()
    {
        var arguments = VideoFrames.Arguments(@"Y:\OldFotik\!Videos\!!Златка\!Video\MVI 4069.AVI", 640, 480, FrameSampling.EveryFrame);

        Assert.Equal(@"Y:\OldFotik\!Videos\!!Златка\!Video\MVI 4069.AVI", arguments[Array.IndexOf(arguments, "-i") + 1]);
    }
}

[Collection(FfmpegCollection.Name)]
public sealed class VideoFramesReadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLVideoFrames_" + Guid.NewGuid().ToString("N"));

    public VideoFramesReadingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static VideoInfo Read(string file) => VideoProbe.ReadAsync(file).GetAwaiter().GetResult();

    [Theory]
    [InlineData(FrameSampling.KeyFrames)]
    [InlineData(FrameSampling.EveryFrame)]
    public void ARealVideo_GivesAPictureEveryTwoSeconds_WithTheirTimes(FrameSampling sampling)
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 7)!;

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var info = Read(file);
        var frames = VideoFrames.Read(file, info, sampling).ToList();

        Assert.InRange(frames.Count, 3, 5); // at 0, 2, 4 and 6 seconds
        Assert.Equal(0, frames[0].Seconds, 0.2);
        for (var i = 1; i < frames.Count; i++)
            Assert.InRange(frames[i].Seconds - frames[i - 1].Seconds, 1.9, 2.6);
        Assert.All(frames, f =>
        {
            Assert.Equal((320, 240), (f.Picture.Width, f.Picture.Height));
            Assert.Equal(320 * 240 * 3, f.Picture.Bgr.Length);
            Assert.Contains(f.Picture.Bgr, b => b != 0); // not an empty picture
        });
    }

    [Fact]
    public void ThePicturesOfASecondVideo_DifferFromTheFirst()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 5)!;

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var frames = VideoFrames.Read(file, Read(file), FrameSampling.EveryFrame).ToList();

        Assert.False(frames[0].Picture.Bgr.SequenceEqual(frames[1].Picture.Bgr)); // the test pattern moves
    }

    [Fact]
    public void AFileThatIsNoVideo_Throws_AndTheReaderCanBeAbandoned()
    {
        if (RealFfmpeg.Path is null) return;
        var file = Path.Combine(_root, "broken.mp4");
        File.WriteAllText(file, "not a video");
        var info = new VideoInfo(5, 320, 240, 0, 25, "h264", null, 0, null, null, null, null);

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);

        Assert.Throws<InvalidDataException>(() => VideoFrames.Read(file, info, FrameSampling.KeyFrames).ToList());
    }

    [Fact]
    public void Stopping_KillsFfmpeg()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 30)!;

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var stop = new CancellationTokenSource();
        var info = Read(file);

        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            foreach (var frame in VideoFrames.Read(file, info, FrameSampling.EveryFrame, stop.Token))
                stop.Cancel();
        });
    }

    [Fact]
    public void TheRealPipeline_MakesMomentsInOrderOfTime()
    {
        // the real pipeline of the job on a real video, with the stand-in for the model: pictures -> moments, times in order
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 9)!;

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var model = ClipModels.Default;
        var moments = VideoAnalyzer.Analyze(
            VideoFrames.Read(file, Read(file), FrameSampling.KeyFrames), model,
            (inputs, count) => Enumerable.Range(0, count).Select(i => new[] { inputs[i * model.InputLength], 0.5f }).ToArray(),
            new MomentSelector(similarity: float.MaxValue), CancellationToken.None); // nothing is "like the last one": every picture is kept

        Assert.InRange(moments.Count, 3, 6);
        Assert.Equal(moments.Select(m => m.TimeMs).Order(), moments.Select(m => m.TimeMs));
    }
}
