using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

public class VideoMetadataTests
{
    private static VideoRecord Record(VideoInfo info, DateSource source = DateSource.Exif, DateTime? taken = null) =>
        new(1, "a/b.avi", 88_800_000, 1, info, new PhotoDate(taken ?? new DateTime(2010, 5, 1, 16, 49, 25), source, PhotoDates.Version), null);

    private static VideoInfo Canon => new(47.67, 640, 480, 0, 30, "mjpeg", "pcm_s16le, 44100 Hz, mono", 14_900_000, null, null, null, null);

    [Fact]
    public void TheRowsOfAVideo_AreInTheVideoCategory_WithTheirOwnDescriptions()
    {
        var rows = VideoMetadata.Entries(Record(Canon));

        Assert.All(rows, row =>
        {
            Assert.True(row.TagId >= MetadataEntry.VideoTagBase);
            Assert.Equal(ImageHelperStrings.CategoryVideo, ExifTagMap.GetCategory(row).Name);
            Assert.False(string.IsNullOrWhiteSpace(MetadataDescriptions.For(row)));
        });
        Assert.Equal(rows.Count, rows.Select(r => r.TagId).Distinct().Count());
    }

    [Fact]
    public void TheRecordedRow_HasTheIdTheGridEditsBy_AndNoOtherRowHasIt()
    {
        // The grid makes the date row of a video editable by this id (MetadataWrapper): if the row moved to another number, the date could not be edited any more
        var rows = VideoMetadata.Entries(Record(Canon), new FileInfo(typeof(VideoMetadataTests).Assembly.Location));

        Assert.Equal(VideoMetadata.RecordedName, Assert.Single(rows, r => r.TagId == VideoMetadata.RecordedTagId).Name);
        Assert.Equal(MetadataEntry.VideoTagBase + 6, VideoMetadata.RecordedTagId);
    }

    [Fact]
    public void TheValues_AreReadable()
    {
        var rows = VideoMetadata.Entries(Record(Canon)).ToDictionary(r => r.Name, r => r.Value);

        Assert.Equal("0:47 (47.7 s)", rows[VideoMetadata.DurationName]);
        Assert.Equal("640 × 480", rows[VideoMetadata.FrameSizeName]);
        Assert.Equal("30 fps", rows[VideoMetadata.FrameRateName]);
        Assert.Equal("mjpeg", rows[VideoMetadata.VideoCodecName]);
        Assert.Equal("pcm_s16le, 44100 Hz, mono", rows[VideoMetadata.AudioName]);
        Assert.Equal("14.9 Mb/s", rows[VideoMetadata.BitrateName]);
        Assert.Equal("01 May 2010 16:49:25", rows[VideoMetadata.RecordedName]); // the way the date taken of a photo is shown
        Assert.Equal("84.7 MB", rows[VideoMetadata.FileSizeName]);
        Assert.False(rows.ContainsKey(VideoMetadata.LocationName)); // it has none
        Assert.False(rows.ContainsKey(VideoMetadata.EncoderName));
    }

    [Fact]
    public void ASidewaysVideo_ShowsTheSizeItIsSeenAt()
    {
        var phone = new VideoInfo(10, 1920, 1080, 270, 29.97, "h264", null, 17_000_000, null, 46.4362, 30.7044, "Lavf");

        var rows = VideoMetadata.Entries(Record(phone)).ToDictionary(r => r.Name, r => r.Value);

        Assert.Equal("1080 × 1920 (stored 1920 × 1080, turned 270°)", rows[VideoMetadata.FrameSizeName]);
        Assert.Equal("29.97 fps", rows[VideoMetadata.FrameRateName]);
        Assert.Equal(VideoLibStrings_NoAudio, rows[VideoMetadata.AudioName]);
        Assert.Equal("46.4362, 30.7044", rows[VideoMetadata.LocationName]);
        Assert.Equal("Lavf", rows[VideoMetadata.EncoderName]);
    }

    private static string VideoLibStrings_NoAudio => Loc.Get("VideoLibStrings", "NoAudio");

    [Theory]
    [InlineData(DateSource.Exif, "DateFromVideo")]
    [InlineData(DateSource.File, "DateFromFile")]
    [InlineData(DateSource.None, "DateNone")]
    public void WhereTheDateComesFrom_IsSaid(DateSource source, string key)
    {
        var rows = VideoMetadata.Entries(Record(Canon, source)).ToDictionary(r => r.Name, r => r.Value);

        Assert.Equal(Loc.Get("VideoLibStrings", key), rows[VideoMetadata.DateFromName]);
    }

    [Fact]
    public void TheDatesOfTheFile_AreShownNextToTheRecordedDate_AndSaidToDiffer()
    {
        var file = Path.Combine(Path.GetTempPath(), "PLVideoMeta_" + Guid.NewGuid().ToString("N") + ".avi");
        File.WriteAllText(file, "x");
        try
        {
            File.SetCreationTime(file, new DateTime(2010, 5, 2, 12, 30, 23));
            File.SetLastWriteTime(file, new DateTime(2010, 5, 2, 12, 30, 23));

            var differs = VideoMetadata.Entries(Record(Canon), new FileInfo(file)).Single(r => r.Name == VideoMetadata.FileDatesName).Value;
            var same = VideoMetadata.Entries(Record(Canon, taken: new DateTime(2010, 5, 2, 12, 30, 23)), new FileInfo(file)).Single(r => r.Name == VideoMetadata.FileDatesName).Value;

            Assert.EndsWith("(≠ Recorded)", differs); // shot on May 1, copied on May 2
            Assert.EndsWith("(= Recorded)", same);
            Assert.DoesNotContain(VideoMetadata.Entries(Record(Canon)), r => r.Name == VideoMetadata.FileDatesName); // no file to look at
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AVideoThatCouldNotBeRead_HasNoRows() =>
        Assert.Throws<ArgumentException>(() => VideoMetadata.Entries(new VideoRecord(1, "x.avi", 1, 1, null, PhotoDate.NotRead, "bad")));

    [Theory]
    [InlineData(0, "?")]
    [InlineData(705_000, "705 kb/s")]
    [InlineData(1_500_000, "1.5 Mb/s")]
    public void ABitrate_IsWrittenInTheUnitThatFits(long bitsPerSecond, string expected) =>
        Assert.Equal(expected, VideoMetadata.BitrateText(bitsPerSecond));

    [Theory]
    [InlineData(500 * 1024, "500 KB")]
    [InlineData(88_800_000, "84.7 MB")]
    [InlineData(3L << 30, "3 GB")]
    public void ASize_IsWrittenInTheUnitThatFits(long bytes, string expected) =>
        Assert.Equal(expected, VideoMetadata.SizeOfFile(bytes));
}
