using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

[Collection(FfmpegCollection.Name)]
public sealed class VideoFrameSaverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLVideoFrameSaver_" + Guid.NewGuid().ToString("N"));

    public VideoFrameSaverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(0, "MVI_4069 0m00s.jpg")]
    [InlineData(83_000, "MVI_4069 1m23s.jpg")]
    [InlineData(83_999, "MVI_4069 1m23s.jpg")]
    [InlineData(3_723_000, "MVI_4069 1h02m03s.jpg")]
    [InlineData(-5, "MVI_4069 0m00s.jpg")]
    public void TheName_SaysWhereInTheVideoThePictureIs(long ms, string expected) =>
        Assert.Equal(expected, VideoFrameSaver.NameFor(@"Y:\OldFotik\!Videos\MVI_4069.AVI", ms));

    [Fact]
    public async Task APictureOfTheVideo_IsAJpegInTheSizeOfTheVideo_WithTheDateItWasRecordedAt()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var video = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 5)!;
        var recorded = new DateTime(2010, 5, 1, 16, 49, 25);

        var saved = await VideoFrameSaver.SaveAsync(video, 2_000, Path.Combine(_root, "Liked"), recorded);

        Assert.Equal(Path.Combine(_root, "Liked", "clip 0m02s.jpg"), saved);
        var bytes = await File.ReadAllBytesAsync(saved);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        var decoder = new System.Windows.Media.Imaging.JpegBitmapDecoder(new Uri(saved), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        Assert.Equal((320, 240), (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight)); // not scaled down

        using var stream = File.OpenRead(saved);
        var exif = ExifReader.Read(stream);
        Assert.Equal(new DateTime(2010, 5, 1, 16, 49, 27), exif.Original); // recorded at 16:49:25, two seconds in
        Assert.Equal(new DateTime(2010, 5, 1, 16, 49, 27).ToUniversalTime(), File.GetLastWriteTimeUtc(saved).ToUniversalTime(), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WithNoDateOfRecording_ThePictureHasNoDateTaken()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var video = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 3)!;

        var saved = await VideoFrameSaver.SaveAsync(video, 1_000, Path.Combine(_root, "out"), recorded: null);

        using var stream = File.OpenRead(saved);
        Assert.Null(ExifReader.Read(stream).Original);
    }

    [Fact]
    public async Task ANameThatIsTaken_IsNotOverwritten()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var video = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 3)!;
        var folder = Path.Combine(_root, "out");

        var first = await VideoFrameSaver.SaveAsync(video, 1_000, folder, null);
        var second = await VideoFrameSaver.SaveAsync(video, 1_000, folder, null);
        var third = await VideoFrameSaver.SaveAsync(video, 1_000, folder, null);

        Assert.Equal(["clip 0m01s (2).jpg", "clip 0m01s (3).jpg", "clip 0m01s.jpg"], new[] { first, second, third }.Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task AVideoThatCannotBeRead_GivesNoPicture_AndLeavesNoFile()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var broken = Path.Combine(_root, "broken.mp4");
        File.WriteAllText(broken, "not a video");

        await Assert.ThrowsAsync<InvalidDataException>(() => VideoFrameSaver.SaveAsync(broken, 0, Path.Combine(_root, "out"), null));

        Assert.False(Directory.Exists(Path.Combine(_root, "out")));
    }
}
