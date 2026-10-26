using Microsoft.Data.Sqlite;

namespace PhotoLiker.Search.Tests;

/// <summary>Exporting an index and importing it as the index of another folder (another machine).</summary>
public class IndexTransferTests : IDisposable
{
    private readonly TempFolder _source = new();       // the photo folder where the index was made
    private readonly TempFolder _target = new();       // the same photos as seen from the other machine
    private readonly TempFolder _transfer = new();     // where the exported file travels

    public void Dispose()
    {
        foreach (var folder in new[] { _source, _target })
        {
            try { File.Delete(SearchPaths.IndexFile(folder.Path)); } catch { /* best effort */ }
        }
        _source.Dispose();
        _target.Dispose();
        _transfer.Dispose();
    }

    private string ExportedFile => Path.Combine(_transfer.Path, "folder.plindex");

    private static IndexedPhoto Photo(string path, float[] clip, params float[][] faces)
    {
        var photo = new IndexedPhoto(path, length: 1000, modifiedTicks: 5000) { Clip = clip, ClipVersion = 1, FaceVersion = 1 };
        photo.Faces = [.. faces.Select((v, i) => new IndexedFace(photo, 0.1f * (i + 1), 0.2f, 0.1f, 0.1f, 0.9f, manual: i == 1, v))];
        return photo;
    }

    private PhotoIndex MakeSourceIndex()
    {
        var index = PhotoIndex.Open(_source.Path);
        index.Save(
        [
            Photo("2009/one.jpg", Vectors.Unit(1, 2), Vectors.Unit(3), Vectors.Unit(4)),
            Photo("2009/two.jpg", Vectors.Unit(2, 1)),
            Photo("2010/three.jpg", Vectors.Unit(5)),
        ]);
        return index;
    }

    [Fact]
    public void AnExportedIndex_BecomesTheIndexOfAnotherFolder_WithPhotosFacesAndHandMarkedFaces()
    {
        using (var index = MakeSourceIndex())
            index.ExportTo(ExportedFile);

        PhotoIndex.Import(_target.Path, ExportedFile);

        using var imported = PhotoIndex.Open(_target.Path);
        Assert.Equal(3, imported.PhotoCount);
        Assert.Equal(2, imported.FaceCount);
        var one = imported.Find("2009/one.jpg")!;
        Assert.Equal(Vectors.Unit(1, 2), one.Clip);
        Assert.Equal([false, true], one.Faces.Select(f => f.Manual));
        Assert.Equal(Path.Combine(_target.Path, "2009", "one.jpg"), imported.ToFullPath(one.RelativePath)); // the paths follow the folder
    }

    [Fact]
    public void TheExport_IsOneSelfContainedFile()
    {
        using (var index = MakeSourceIndex())
            index.ExportTo(ExportedFile);

        Assert.Equal([ExportedFile], Directory.GetFiles(_transfer.Path)); // no temp file, no write-ahead log
        var info = PhotoIndex.Inspect(ExportedFile);
        Assert.Equal(3, info.Photos);
        Assert.Equal(2, info.Faces);
        Assert.Equal(_source.Path, info.Root, ignoreCase: true);
        Assert.InRange(info.ExportedAt!.Value, DateTime.Now.AddMinutes(-2), DateTime.Now.AddMinutes(1));
    }

    [Fact]
    public async Task ExportingWhileTheIndexIsBeingChanged_GivesAConsistentCopy()
    {
        using var index = MakeSourceIndex();

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
                index.Save([Photo($"more/{i}.jpg", Vectors.Unit(i + 1), Vectors.Unit(i + 2))]);
        });
        index.ExportTo(ExportedFile);
        await writer;

        var info = PhotoIndex.Inspect(ExportedFile); // opens and reads it: it is a whole, valid index
        Assert.InRange(info.Photos, 3, 203);
    }

    [Fact]
    public void Importing_ReplacesTheIndexTheFolderHad()
    {
        using (var index = MakeSourceIndex())
            index.ExportTo(ExportedFile);
        using (var existing = PhotoIndex.Open(_target.Path))
            existing.Save([Photo("old.jpg", Vectors.Unit(9))]);

        PhotoIndex.Import(_target.Path, ExportedFile);

        using var imported = PhotoIndex.Open(_target.Path);
        Assert.Null(imported.Find("old.jpg"));
        Assert.Equal(3, imported.PhotoCount);
    }

    [Fact]
    public void Importing_LeavesTheExportedFileAlone()
    {
        using (var index = MakeSourceIndex())
            index.ExportTo(ExportedFile);

        PhotoIndex.Import(_target.Path, ExportedFile);

        Assert.True(File.Exists(ExportedFile));
        Assert.Equal(3, PhotoIndex.Inspect(ExportedFile).Photos);
    }

    [Fact]
    public void ANonIndexFile_IsRefused_AndNothingIsReplaced()
    {
        using (var existing = PhotoIndex.Open(_target.Path))
            existing.Save([Photo("keep.jpg", Vectors.Unit(1))]);
        var notAnIndex = Path.Combine(_transfer.Path, "photo.jpg");
        File.WriteAllBytes(notAnIndex, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        Assert.Throws<InvalidDataException>(() => PhotoIndex.Import(_target.Path, notAnIndex));

        using var untouched = PhotoIndex.Open(_target.Path);
        Assert.NotNull(untouched.Find("keep.jpg"));
    }

    [Fact]
    public void ADatabaseThatIsNotAnIndex_IsRefused()
    {
        var other = Path.Combine(_transfer.Path, "other.db");
        using (var db = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE something (id INTEGER)";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => PhotoIndex.Inspect(other));
    }

    [Fact]
    public void AnIndexOfAnotherVersion_IsRefused()
    {
        using (var index = MakeSourceIndex())
            index.ExportTo(ExportedFile);
        using (var db = new SqliteConnection($"Data Source={ExportedFile};Pooling=False"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE meta SET value = '999' WHERE key = 'schema'";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidDataException>(() => PhotoIndex.Inspect(ExportedFile));
        Assert.Contains("another version", ex.Message);
    }

    [Fact]
    public void AMissingFile_IsReportedAsMissing()
    {
        Assert.Throws<FileNotFoundException>(() => PhotoIndex.Inspect(Path.Combine(_transfer.Path, "nothing.plindex")));
    }
}
