using System.Buffers.Binary;
using System.Text;

namespace PhotoLiker.Search.Tests;

/// <summary>The "Recorded" row of the metadata grid: one video is given a date by hand.</summary>
public class VideoDateSaveTests : IDisposable
{
    private static readonly DateTime Typed = new(2012, 7, 14, 9, 30, 15);
    private static readonly DateTime OldFileDate = new(2020, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Video(string name, byte[] bytes)
    {
        var path = _folder.File(name);
        File.WriteAllBytes(path, bytes);
        File.SetCreationTimeUtc(path, OldFileDate);
        File.SetLastWriteTimeUtc(path, OldFileDate);
        return path;
    }

    private static byte[] Mp4WithOldDate()
    {
        var old = VideoBytes.Seconds(new DateTime(2003, 1, 31, 15, 6, 0, DateTimeKind.Utc));
        return VideoBytes.Mp4(old, old, old);
    }

    private static int IndexOf(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void AnMp4_SaysItWasRecordedAtTheTypedDate_AndItsFileDatesAreThatMomentToo()
    {
        var original = Mp4WithOldDate();
        var path = Video("a.mp4", original);

        var saved = VideoDateSave.Save(path, Typed);

        Assert.True(saved.WrittenInside);
        Assert.False(saved.NoPlaceInside);
        Assert.True(saved.DatesKept);
        var written = File.ReadAllBytes(path);
        Assert.Equal(original.Length, written.Length); // not one byte moves
        Assert.Equal(original.Length, saved.Length);
        var mvhd = IndexOf(written, "mvhd") + 4;
        Assert.Equal((uint)VideoBytes.Seconds(DateAlignment.ToUtc(Typed)), BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(mvhd + 4)));
        Assert.Empty(VideoContainerDates.Plan(new MemoryStream(written), Typed)!); // it says the typed date now

        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetCreationTimeUtc(path));
        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetLastWriteTimeUtc(path));
        Assert.Equal(saved.CreatedUtc, File.GetCreationTimeUtc(path));
        Assert.Equal(saved.LastWriteUtc, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AnAvi_GetsTheDateInItsDateChunk_InTheFormItHad()
    {
        var path = Video("MVI_1.avi", VideoBytes.Avi("Sat Jun 05 13:10:45 2010"));

        var saved = VideoDateSave.Save(path, Typed);

        Assert.True(saved.WrittenInside);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("Sat Jul 14 09:30:15 2012", Encoding.ASCII.GetString(bytes, IndexOf(bytes, "IDIT") + 8, 24));
        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AVideoWithNoPlaceForADate_GetsItsFileDatesOnly_AndIsNotTouchedInside()
    {
        var original = VideoBytes.Avi(null); // an AVI with no date chunk
        var path = Video("a.avi", original);

        var saved = VideoDateSave.Save(path, Typed);

        Assert.True(saved.NoPlaceInside);
        Assert.False(saved.WrittenInside);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetCreationTimeUtc(path));
        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AKindOfVideoThatCannotHoldADate_IsNeverReadAsOne_AndGetsItsFileDatesOnly()
    {
        var bytes = new byte[2000];
        new Random(1).NextBytes(bytes); // not a video at all, but with a name that no writer knows
        var path = Video("a.mkv", bytes);

        var saved = VideoDateSave.Save(path, Typed);

        Assert.True(saved.NoPlaceInside);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ADateThatTheVideoAlreadySays_StillSetsTheFileDates_AndWritesNothing()
    {
        var path = Video("a.mp4", Mp4WithOldDate());
        VideoDateSave.Save(path, Typed);
        var once = File.ReadAllBytes(path);
        File.SetCreationTimeUtc(path, OldFileDate);
        File.SetLastWriteTimeUtc(path, OldFileDate);

        var saved = VideoDateSave.Save(path, Typed);

        Assert.True(saved.WrittenInside);
        Assert.Equal(once, File.ReadAllBytes(path));
        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetCreationTimeUtc(path));
        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AReadOnlyVideo_IsWritten_AndStaysReadOnly()
    {
        var path = Video("a.mp4", Mp4WithOldDate());
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var saved = VideoDateSave.Save(path, Typed);

            Assert.True(saved.WrittenInside);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
            Assert.Equal(DateAlignment.ToUtc(Typed), File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void ADateThatTheVideoCannotHold_IsRefused_AndNothingIsChanged()
    {
        var original = Mp4WithOldDate();
        var path = Video("a.mp4", original);
        var avi = Video("b.avi", VideoBytes.Avi("Sat Jun 05 13:10:45 2010"));

        // before 1904 an MP4 has no way to say it; before 1601 Windows has no file date, whatever the kind of video
        Assert.Throws<InvalidDataException>(() => VideoDateSave.Save(path, new DateTime(1900, 1, 1, 12, 0, 0)));
        Assert.Throws<InvalidDataException>(() => VideoDateSave.Save(avi, new DateTime(1500, 1, 1, 12, 0, 0)));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(OldFileDate, File.GetCreationTimeUtc(path));
        Assert.Equal(OldFileDate, File.GetLastWriteTimeUtc(path));
        Assert.Equal(OldFileDate, File.GetLastWriteTimeUtc(avi));
        Assert.Equal(VideoBytes.Avi("Sat Jun 05 13:10:45 2010"), File.ReadAllBytes(avi));
    }

    [Fact]
    public void AVideoThatIsOpenInAPlayer_IsLeftAsItWas_AndTheCallerIsToldWhy()
    {
        var original = Mp4WithOldDate();
        var path = Video("a.mp4", original);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // a player holds it: nobody else may write
            Assert.ThrowsAny<IOException>(() => VideoDateSave.Save(path, Typed));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(OldFileDate, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AVideoThatIsGone_IsReported()
    {
        Assert.Throws<FileNotFoundException>(() => VideoDateSave.Save(_folder.File("gone.mp4"), Typed));
    }

    [Fact]
    public void TheSecondsAreKept_AndTheRestOfTheTimeIsDropped()
    {
        var path = Video("a.mp4", Mp4WithOldDate());

        VideoDateSave.Save(path, Typed.AddMilliseconds(987));

        Assert.Equal(DateAlignment.ToUtc(Typed), File.GetLastWriteTimeUtc(path));
    }
}
