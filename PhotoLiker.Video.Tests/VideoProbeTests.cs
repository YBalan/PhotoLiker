using System.Globalization;

namespace PhotoLiker.Video.Tests;

/// <summary>Reads real ffmpeg output (copied from the videos of the archive this was made for): a Canon camera, an Android phone, a camera with no clock.</summary>
public class VideoProbeTests
{
    private const string CanonAvi = """
        Input #0, avi, from 'Y:\OldFotik\!Videos\!!Златка\!Video\MVI_4069.AVI':
          Metadata:
            creation_time   : 2010-05-01 16:49:25
          Duration: 00:00:47.67, start: 0.000000, bitrate: 14900 kb/s
          Stream #0:0: Video: mjpeg (Baseline) (MJPG / 0x47504A4D), yuvj422p(pc, bt470bg/unknown/unknown), 640x480, 14200 kb/s, 30 fps, 30 tbr, 30 tbn
          Stream #0:1: Audio: pcm_s16le ([1][0][0][0] / 0x0001), 44100 Hz, mono, s16, 705 kb/s
        At least one output file must be specified
        """;

    private const string AndroidMp4 = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'D:\Downloads\VID_20231202_143606.mp4':
          Metadata:
            major_brand     : mp42
            minor_version   : 0
            compatible_brands: isommp42
            creation_time   : 2023-12-02T12:41:50.000000Z
            location        : +46.4362+030.7044/
            location-eng    : +46.4362+030.7044/
            com.android.version: 11
          Duration: 00:05:43.22, start: 0.000000, bitrate: 17161 kb/s
          Stream #0:0[0x1](eng): Video: h264 (Baseline) (avc1 / 0x31637661), yuv420p(tv, bt709, progressive), 1920x1080, 17001 kb/s, SAR 1:1 DAR 16:9, 30 fps, 30 tbr, 90k tbn (default)
            Metadata:
              creation_time   : 2023-12-02T12:41:50.000000Z
              handler_name    : VideoHandle
              vendor_id       : [0][0][0][0]
            Side data:
              displaymatrix: rotation of -90.00 degrees
          Stream #0:1[0x2](eng): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 127 kb/s (default)
            Metadata:
              creation_time   : 2023-12-02T12:41:50.000000Z
        At least one output file must be specified
        """;

    private const string SamsungMp4 = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'SDV_0668.MP4':
          Metadata:
            creation_time   : 2003-01-31T16:06:00.000000Z
          Duration: 00:00:09.08, start: 0.000000, bitrate: 7648 kb/s
          Stream #0:0[0x1](eng): Video: h264 (Baseline) (avc1 / 0x31637661), yuv420p(progressive), 720x576, 7471 kb/s, 25 fps, 25 tbr, 90k tbn (default)
            Metadata:
              creation_time   : 2003-01-31T16:06:00.000000Z
              encoder         : amsung AVC Encoder
          Stream #0:1[0x2](eng): Audio: aac (LC) (mp4a / 0x6134706D), 24000 Hz, stereo, fltp, 122 kb/s (default)
        """;

    private const string OnlyStreamDates = """
        Input #0, matroska,webm, from 'clip.mkv':
          Duration: 00:01:04.71, start: 0.000000, bitrate: 1094 kb/s
          Stream #0:0: Video: vp9 (Profile 0), yuv420p(tv), 848x480, 29.97 fps, 30 tbr, 1k tbn (default)
            Metadata:
              creation_time   : 2025-01-13T20:13:52.000000Z
          Stream #0:1: Audio: opus, 48000 Hz, stereo, fltp (default)
        """;

    [Fact]
    public void ACanonAvi_IsReadWithItsClockAsItIs()
    {
        var info = VideoProbe.Parse(CanonAvi);

        Assert.NotNull(info);
        Assert.Equal(47.67, info.DurationSeconds, 0.001);
        Assert.Equal((640, 480), (info.Width, info.Height));
        Assert.Equal(0, info.Rotation);
        Assert.Equal(30, info.FramesPerSecond);
        Assert.Equal("mjpeg", info.VideoCodec);
        Assert.Equal("pcm_s16le, 44100 Hz, mono", info.Audio);
        Assert.Equal(14_900_000, info.BitsPerSecond);
        Assert.Equal(new DateTime(2010, 5, 1, 16, 49, 25), info.Recorded); // no time zone: the clock of the camera, not converted
        Assert.Null(info.Latitude);
    }

    [Fact]
    public void APhoneVideo_HasItsRotationItsPlaceAndItsDateInLocalTime()
    {
        var info = VideoProbe.Parse(AndroidMp4);

        Assert.NotNull(info);
        Assert.Equal(343.22, info.DurationSeconds, 0.001);
        Assert.Equal((1920, 1080), (info.Width, info.Height));
        Assert.Equal(270, info.Rotation); // -90
        Assert.True(info.Turned);
        Assert.Equal((1080, 1920), (info.DisplayWidth, info.DisplayHeight));
        Assert.Equal("aac, 48000 Hz, stereo", info.Audio);
        Assert.Equal(46.4362, info.Latitude!.Value, 0.00001);
        Assert.Equal(30.7044, info.Longitude!.Value, 0.00001);

        var expected = DateTimeOffset.Parse("2023-12-02T12:41:50Z", CultureInfo.InvariantCulture).LocalDateTime;
        Assert.Equal(expected, info.Recorded);
    }

    [Fact]
    public void TheEncoderAndAnUnsetClock_AreKeptAsWritten()
    {
        var info = VideoProbe.Parse(SamsungMp4);

        Assert.NotNull(info);
        Assert.Equal((720, 576), (info.Width, info.Height));
        Assert.Equal(25, info.FramesPerSecond);
        Assert.Equal(2003, info.Recorded!.Value.Year); // reading is not judging: the rules of the dates decide whether it is believed
    }

    [Fact]
    public void ADateOnTheStreamsOnly_IsUsed()
    {
        var info = VideoProbe.Parse(OnlyStreamDates);

        Assert.NotNull(info);
        Assert.Equal(29.97, info.FramesPerSecond, 0.001);
        Assert.NotNull(info.Recorded);
        Assert.Equal(2025, info.Recorded!.Value.Year);
    }

    [Theory]
    [InlineData("Y:\\x.avi: Invalid data found when processing input")]
    [InlineData("")]
    public void ATextWithNoVideoInIt_IsNotAVideo(string text) => Assert.Null(VideoProbe.Parse(text));

    [Fact]
    public void ASoundWithACoverPicture_IsNotAVideo()
    {
        const string music = """
            Input #0, mp3, from 'song.mp3':
              Duration: 00:03:30.00, start: 0.025057, bitrate: 320 kb/s
              Stream #0:0: Audio: mp3, 44100 Hz, stereo, fltp, 320 kb/s
              Stream #0:1: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 500x500 [SAR 1:1 DAR 1:1], 90k tbr, 90k tbn (attached pic)
            """;

        Assert.Null(VideoProbe.Parse(music));
    }

    [Fact]
    public void ACodecTag_IsNotTakenForTheFrameSize()
    {
        // "0x47504A4D" has digits, an x and digits: it must not be read as a size
        var info = VideoProbe.Parse(CanonAvi);

        Assert.NotEqual(0, info!.Width);
        Assert.Equal(640, info.Width);
    }

    [Theory]
    [InlineData("2010-05-01 16:49:25", 2010, 5, 1, 16, 49, 25)]
    [InlineData("2010-05-01T16:49:25", 2010, 5, 1, 16, 49, 25)]
    public void ADateWithNoZone_IsTheCameraClock(string text, int y, int mo, int d, int h, int mi, int s) =>
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), VideoProbe.ParseDate(text));

    [Fact]
    public void ADateInUtc_BecomesLocalTime()
    {
        var expected = DateTimeOffset.Parse("2021-08-20T16:01:04.000000Z", CultureInfo.InvariantCulture).LocalDateTime;

        Assert.Equal(expected, VideoProbe.ParseDate("2021-08-20T16:01:04.000000Z"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    public void NoDate_IsNull(string? text) => Assert.Null(VideoProbe.ParseDate(text));

    [Theory]
    [InlineData("+46.4362+030.7044/", 46.4362, 30.7044)]
    [InlineData("-33.8688+151.2093+012.000/", -33.8688, 151.2093)]
    [InlineData("+47.5980+029.3143/", 47.598, 29.3143)]
    public void ALocationOfAPhone_IsReadAsDegrees(string text, double latitude, double longitude)
    {
        var (lat, lon) = VideoProbe.LocationOf(text);

        Assert.Equal(latitude, lat!.Value, 0.00001);
        Assert.Equal(longitude, lon!.Value, 0.00001);
    }

    [Theory]
    [InlineData("+00.0000+000.0000/")] // a phone that had no fix writes zeros
    [InlineData("+95.0000+030.0000/")]
    [InlineData("nowhere")]
    [InlineData(null)]
    public void NoPlace_IsNull(string? text) => Assert.Equal((null, null), VideoProbe.LocationOf(text));
}
