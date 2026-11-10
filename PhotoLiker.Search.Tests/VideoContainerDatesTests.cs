using System.Buffers.Binary;
using System.Text;

namespace PhotoLiker.Search.Tests;

/// <summary>Small MP4 and AVI files that are only headers: enough to hold the places a date is written in.</summary>
internal static class VideoBytes
{
    public static readonly DateTime Epoch1904 = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static byte[] Box(string type, params byte[][] parts)
    {
        var body = parts.SelectMany(p => p).ToArray();
        var bytes = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        body.CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>A header box: version, flags, then creation time and time of change (4 or 8 bytes each), then some more bytes of the kind a real header has.</summary>
    public static byte[] Header(string type, int version, long creation, long modification, int more = 12)
    {
        var width = version == 1 ? 8 : 4;
        var body = new byte[4 + 2 * width + more];
        body[0] = (byte)version;
        if (width == 8)
        {
            BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(4), (ulong)creation);
            BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(12), (ulong)modification);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), (uint)creation);
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(8), (uint)modification);
        }
        for (var i = 0; i < more; i++)
            body[4 + 2 * width + i] = (byte)(0xA0 + i); // not zeros: nothing after the times may be touched
        return Box(type, body);
    }

    public static long Seconds(DateTime utc) => (long)(utc - Epoch1904).TotalSeconds;

    /// <summary>ftyp, an mdat (big, so that the movie box is far from the start), and the movie box: a movie header and two tracks.</summary>
    public static byte[] Mp4(long movieCreation, long trackCreation, long mediaCreation, long secondMediaCreation = 0, int mediaVersion = 0, int mdatSize = 70_000)
    {
        var track1 = Box("trak", Header("tkhd", 0, trackCreation, trackCreation), Box("mdia", Header("mdhd", mediaVersion, mediaCreation, mediaCreation + 5)));
        var track2 = Box("trak", Header("tkhd", 0, 0, 0), Box("mdia", Header("mdhd", 0, secondMediaCreation, secondMediaCreation)));
        var moov = Box("moov", Header("mvhd", 0, movieCreation, movieCreation), track1, track2);
        return [.. Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\0\0isom")), .. Box("mdat", new byte[mdatSize]), .. moov];
    }

    public static byte[] Chunk(string id, byte[] data)
    {
        var bytes = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(id).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        data.CopyTo(bytes, 8);
        return bytes;
    }

    public static byte[] List(string type, params byte[][] children)
    {
        var body = Encoding.ASCII.GetBytes(type).Concat(children.SelectMany(c => c)).ToArray();
        return Chunk("LIST", body);
    }

    /// <summary>An AVI the way a camera makes it: the header list, the date chunk, some padding, then the pictures.</summary>
    public static byte[] Avi(string? date, bool dateAfterPictures = false)
    {
        var idit = date is null ? [] : Chunk("IDIT", [.. Encoding.ASCII.GetBytes(date), (byte)'\n', 0]);
        var hdrl = List("hdrl", Chunk("avih", new byte[56]));
        var movi = List("movi", Chunk("00dc", new byte[500]));
        byte[] body = dateAfterPictures
            ? [.. Encoding.ASCII.GetBytes("AVI "), .. hdrl, .. movi, .. idit]
            : [.. Encoding.ASCII.GetBytes("AVI "), .. hdrl, .. idit, .. Chunk("JUNK", new byte[100]), .. movi];
        var bytes = new byte[8 + body.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)body.Length);
        body.CopyTo(bytes, 8);
        return bytes;
    }

    public static byte[] Apply(byte[] file, IEnumerable<VideoDatePatch> patches)
    {
        var copy = (byte[])file.Clone();
        foreach (var patch in patches)
        {
            Assert.True(patch.Old.AsSpan().SequenceEqual(copy.AsSpan((int)patch.Offset, patch.Old.Length)), "the patch says what is there");
            patch.New.CopyTo(copy, (int)patch.Offset);
        }
        return copy;
    }

    public static IReadOnlyList<VideoDatePatch>? Plan(byte[] file, DateTime taken) => VideoContainerDates.Plan(new MemoryStream(file), taken);
}

public class VideoContainerDatesTests
{
    // The camera's clock is taken as this computer's, as for photos: the file holds UTC
    private static readonly DateTime Taken = new(2011, 5, 23, 3, 25, 4);
    private static long TakenSeconds => VideoBytes.Seconds(DateAlignment.ToUtc(Taken));

    [Fact]
    public void AnMp4_GetsTheDateInItsMovieHeader_AndInTheTracksThatHadOne_AndNothingElseChanges()
    {
        var old = VideoBytes.Seconds(new DateTime(2003, 1, 31, 15, 6, 0, DateTimeKind.Utc));
        var file = VideoBytes.Mp4(movieCreation: old, trackCreation: old, mediaCreation: old);

        var patches = VideoBytes.Plan(file, Taken)!;
        var after = VideoBytes.Apply(file, patches);

        Assert.Equal(file.Length, after.Length);
        Assert.Equal(3, patches.Count); // mvhd, the first track's tkhd, its mdhd; the second track had none and keeps having none
        var changed = Enumerable.Range(0, file.Length).Where(i => file[i] != after[i]).ToList();
        Assert.All(changed, i => Assert.Contains(patches, p => i >= p.Offset && i < p.Offset + p.New.Length));

        // The date as ffmpeg reads it: creation_time of the movie header and of the track
        var mvhd = IndexOf(after, "mvhd") + 4; // the start of the body: version and flags, then the times
        Assert.Equal((uint)TakenSeconds, BinaryPrimitives.ReadUInt32BigEndian(after.AsSpan(mvhd + 4)));
        Assert.Equal((uint)TakenSeconds, BinaryPrimitives.ReadUInt32BigEndian(after.AsSpan(mvhd + 8))); // the time of change was the same: it follows
        var mdhd = IndexOf(after, "mdhd") + 4;
        Assert.Equal((uint)TakenSeconds, BinaryPrimitives.ReadUInt32BigEndian(after.AsSpan(mdhd + 4)));
        Assert.Equal((uint)old + 5, BinaryPrimitives.ReadUInt32BigEndian(after.AsSpan(mdhd + 8)));  // it was not the creation time: it stays
        Assert.Empty(VideoBytes.Plan(after, Taken)!); // it says so now
    }

    [Fact]
    public void AnMp4ThatHadNoDate_GetsOneInItsMovieHeader_ButItsTracksKeepSayingNone()
    {
        var file = VideoBytes.Mp4(0, 0, 0);

        var after = VideoBytes.Apply(file, VideoBytes.Plan(file, Taken)!);

        var mvhd = IndexOf(after, "mvhd") + 4; // the start of the body: version and flags, then the times
        Assert.Equal((uint)TakenSeconds, BinaryPrimitives.ReadUInt32BigEndian(after.AsSpan(mvhd + 4)));
        var tkhd = IndexOf(after, "tkhd") + 4;
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(after.AsSpan(tkhd + 4)));
    }

    [Fact]
    public void AMovieWithSixtyFourBitTimes_IsWrittenToo()
    {
        var movie = VideoBytes.Header("mvhd", 1, 100, 100);
        var file = VideoBytes.Box("moov", movie, VideoBytes.Box("trak", VideoBytes.Box("mdia", VideoBytes.Header("mdhd", 1, 100, 100))));

        var patches = VideoBytes.Plan(file, Taken)!;
        var after = VideoBytes.Apply(file, patches);

        Assert.Equal(2, patches.Count);
        Assert.All(patches, p => Assert.Equal(16, p.New.Length));
        Assert.Equal((ulong)TakenSeconds, BinaryPrimitives.ReadUInt64BigEndian(after.AsSpan(IndexOf(after, "mvhd") + 4 + 4)));
    }

    [Fact]
    public void ABoxThatIsSixteenBytesInTheHeader_Works()
    {
        // A box with a 64-bit size (size field 1): the movie box itself, as a file of more than 4 GB has
        var inner = VideoBytes.Box("mvhd", VideoBytes.Header("mvhd", 0, 5, 5)[8..]);
        var size = 16 + inner.Length;
        var moov = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(moov, 1);
        Encoding.ASCII.GetBytes("moov").CopyTo(moov, 4);
        BinaryPrimitives.WriteUInt64BigEndian(moov.AsSpan(8), (ulong)size);
        var file = (byte[])[.. VideoBytes.Box("ftyp", new byte[8]), .. moov, .. inner];

        var patches = VideoBytes.Plan(file, Taken);

        Assert.Single(patches!);
    }

    [Fact]
    public void ADateThatIsAlreadyThere_NeedsNoPatch_AndATruncatedFileIsNotGuessedAt()
    {
        var file = VideoBytes.Mp4(TakenSeconds, TakenSeconds, TakenSeconds, TakenSeconds);

        Assert.Empty(VideoBytes.Plan(file, Taken)!);
        Assert.Null(VideoBytes.Plan(file[..30], Taken)); // the movie box is cut off: no place found
    }

    [Fact]
    public void ADateBefore1904_OrOverTheEndOfA32BitTime_IsRefused()
    {
        var file = VideoBytes.Mp4(1, 1, 1);

        Assert.Throws<InvalidDataException>(() => VideoBytes.Plan(file, new DateTime(1890, 1, 1)));
        Assert.Throws<InvalidDataException>(() => VideoBytes.Plan(file, new DateTime(2050, 1, 1))); // 32 bits of seconds since 1904 end in 2040
    }

    [Fact]
    public void AnAviWithTheCameraDate_GetsTheDateInTheFormItHad()
    {
        var file = VideoBytes.Avi("Sat Jun 05 13:10:45 2010");

        var patches = VideoBytes.Plan(file, Taken)!;
        var after = VideoBytes.Apply(file, patches);

        Assert.Single(patches);
        Assert.Equal(file.Length, after.Length);
        Assert.Equal("Mon May 23 03:25:04 2011", Encoding.ASCII.GetString(after, IndexOf(after, "IDIT") + 8, 24));
        Assert.Equal((byte)'\n', after[IndexOf(after, "IDIT") + 8 + 24]); // what follows the date is the camera's
        Assert.Empty(VideoBytes.Plan(after, Taken)!);
    }

    [Fact]
    public void AnAviWithTheSlashForm_KeepsTheSlashForm()
    {
        var file = VideoBytes.Avi("2005/04/03 15:04:43");

        var after = VideoBytes.Apply(file, VideoBytes.Plan(file, Taken)!);

        Assert.Equal("2011/05/23 03:25:04", Encoding.ASCII.GetString(after, IndexOf(after, "IDIT") + 8, 19));
    }

    [Theory]
    [InlineData(null)]                       // no date chunk at all
    [InlineData("created yesterday, maybe")] // a form that is not known: not guessed at
    public void AnAviThatHasNoKnownPlaceForADate_HasNone(string? date)
    {
        Assert.Null(VideoBytes.Plan(VideoBytes.Avi(date), Taken));
    }

    [Fact]
    public void ADateChunkAfterThePictures_IsNotLookedFor()
    {
        Assert.Null(VideoBytes.Plan(VideoBytes.Avi("Sat Jun 05 13:10:45 2010", dateAfterPictures: true), Taken));
    }

    [Fact]
    public void SomethingThatIsNotAVideoOfThoseKinds_HasNoPlace()
    {
        Assert.Null(VideoBytes.Plan(ExifJpeg.Build(original: "2009:09:01 14:30:00"), Taken));
        Assert.Null(VideoBytes.Plan([], Taken));
        Assert.Null(VideoBytes.Plan(Encoding.ASCII.GetBytes("Not a video at all, just text that is long enough"), Taken));
    }

    [Theory]
    [InlineData("a.mp4", true)]
    [InlineData("a.MOV", true)]
    [InlineData("a.avi", true)]
    [InlineData("a.3gp", true)]
    [InlineData("a.mkv", false)]
    [InlineData("a.mts", false)]
    [InlineData("a.jpg", false)]
    public void TheKindsThatMayHaveAPlace_AreKnownByTheirExtension(string name, bool can)
    {
        Assert.Equal(can, VideoContainerDates.CanWrite(name));
    }

    private static int IndexOf(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text));
}

public class VideoDateRunTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0);
    private static readonly DateTime FileDate = new(2011, 5, 23, 0, 25, 4, DateTimeKind.Utc);

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Video(string name, byte[] bytes)
    {
        var path = _folder.File(name);
        File.WriteAllBytes(path, bytes);
        File.SetCreationTimeUtc(path, FileDate);
        File.SetLastWriteTimeUtc(path, FileDate);
        return path;
    }

    private DateAlignmentEntry Planned(string path, DateTime given, bool write = true)
    {
        // What a scan finds of a video: no EXIF, the date it says it was recorded (here believed), the times of the file
        var facts = new PhotoDateFacts(path, ExifDateSlots.None, new DateTime(2003, 1, 31, 18, 6, 0), File.GetCreationTimeUtc(path), File.GetLastWriteTimeUtc(path));
        return DateAlignment.Plan(facts, DateAlignmentOptions.Default, given, write);
    }

    private DateAlignmentReport Run(params DateAlignmentEntry[] entries) =>
        DateAligner.Apply(entries, _folder.Path, DateAligner.NewLogFile(_folder.Path, Now), null, null, CancellationToken.None);

    [Fact]
    public void AGivenDate_IsWrittenIntoTheVideoInPlace_AndAnUndoPutsEveryByteBack()
    {
        var old = VideoBytes.Seconds(new DateTime(2003, 1, 31, 15, 6, 0, DateTimeKind.Utc));
        var original = VideoBytes.Mp4(old, old, old);
        var path = Video("SDV_0667.mp4", original);
        var given = FileDate.ToLocalTime(); // the date of the file, as local time (whatever the zone of the machine: the file dates then are that date already)

        var entry = Planned(path, given);
        Assert.Equal(DateAlignmentStatus.Change, entry.Status);
        Assert.True(entry.SetExif.HasFlag(ExifDateFields.Original));
        Assert.False(entry.SetCreated || entry.SetModified); // the file dates are the date already: only the video's own date changes

        var report = Run(entry);

        Assert.Empty(report.Failures);
        var photo = Assert.Single(report.Photos);
        Assert.True(photo.Written.HasFlag(ExifDateFields.Original));
        var written = File.ReadAllBytes(path);
        Assert.Equal(original.Length, written.Length);
        Assert.NotEqual(original, written);
        Assert.Empty(VideoContainerDates.Plan(new MemoryStream(written), given)!); // it says the given date now
        Assert.Equal(FileDate, File.GetLastWriteTimeUtc(path).AddTicks(-(File.GetLastWriteTimeUtc(path).Ticks % TimeSpan.TicksPerSecond)));

        var undone = DateAligner.Undo(report.LogFile, null, CancellationToken.None);

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(0, undone.Skipped);
        Assert.Empty(undone.Failures);
        Assert.True(Assert.Single(undone.Photos).Written.HasFlag(ExifDateFields.Original)); // the catalog is told to read the date again
        Assert.Equal(FileDate, File.GetCreationTimeUtc(path));
    }

    [Fact]
    public void TheFileDatesAreSetToo_WhenTheyDifferFromTheGivenDate()
    {
        var original = VideoBytes.Avi("Sat Jun 05 13:10:45 2010");
        var path = Video("MVI_1.avi", original);
        var given = new DateTime(2012, 7, 14, 9, 30, 0);

        var report = Run(Planned(path, given));

        Assert.Empty(report.Failures);
        Assert.Equal(DateAlignment.ToUtc(given), File.GetCreationTimeUtc(path));
        Assert.Equal(DateAlignment.ToUtc(given), File.GetLastWriteTimeUtc(path));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("Sat Jul 14 09:30:00 2012", Encoding.ASCII.GetString(bytes, IndexOf(bytes, "IDIT") + 8, 24));
    }

    [Fact]
    public void AnUndo_LeavesAVideoAloneThatWasChangedSinceTheRun()
    {
        var old = VideoBytes.Seconds(new DateTime(2003, 1, 31, 15, 6, 0, DateTimeKind.Utc));
        var path = Video("a.mp4", VideoBytes.Mp4(old, old, old));
        var report = Run(Planned(path, FileDate.ToLocalTime()));

        // Someone wrote another date there after the run
        var bytes = File.ReadAllBytes(path);
        bytes[IndexOf(bytes, "mvhd") + 8 + 7] ^= 0x55;
        File.WriteAllBytes(path, bytes);
        File.SetCreationTimeUtc(path, FileDate);
        File.SetLastWriteTimeUtc(path, FileDate);

        var undone = DateAligner.Undo(report.LogFile, null, CancellationToken.None);

        Assert.True(undone.Skipped > 0);
        Assert.Equal(bytes[IndexOf(bytes, "mvhd") + 8 + 7], File.ReadAllBytes(path)[IndexOf(bytes, "mvhd") + 8 + 7]); // that byte is theirs: it stays
    }

    [Fact]
    public void AVideoWithNoPlaceForADate_GetsItsFileDates_AndSaysSo()
    {
        var path = Video("a.avi", VideoBytes.Avi(null));
        var given = new DateTime(2012, 7, 14, 9, 30, 0);

        var report = Run(Planned(path, given));

        Assert.Equal(DateAlignment.ToUtc(given), File.GetCreationTimeUtc(path));
        var failure = Assert.Single(report.Failures);
        Assert.Equal(path, failure.Path);
        Assert.Single(report.Photos);
        Assert.False(report.Photos[0].Written.HasFlag(ExifDateFields.Original));
    }

    [Fact]
    public void WithoutTheTick_AVideoIsNotWrittenInto()
    {
        var old = VideoBytes.Seconds(new DateTime(2003, 1, 31, 15, 6, 0, DateTimeKind.Utc));
        var original = VideoBytes.Mp4(old, old, old);
        var path = Video("a.mp4", original);

        var entry = Planned(path, new DateTime(2012, 7, 14, 9, 30, 0), write: false);
        Run(entry);

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    private static int IndexOf(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text));
}
