using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

/// <summary>The models that match words with photos: the catalog, how photos and words are prepared for each, and what the index remembers.</summary>
public class ClipModelCatalogTests
{
    [Fact]
    public void TheDefault_IsTheModelThatEarlierVersionsUsed_SoTheirIndexesAndDownloadsStillFit()
    {
        var model = ClipModels.Default;

        Assert.Equal("clip-vit-b32", model.Id);
        Assert.Equal(Settings.DefaultSearchModel, model.Id);
        Assert.Equal(new Settings().SearchModel, model.Id);
        Assert.Equal(512, model.Dimensions);
        Assert.Equal("clip-vit-b32", model.FolderName); // %LocalAppData%\PhotoLiker\models\clip-vit-b32 is where the first version put the files
        Assert.Equal(["vision_model.onnx", "text_model.onnx", "vocab.json", "merges.txt"], model.Files.Select(f => f.Name));
        Assert.Same(model, ClipModels.All[0]);
    }

    [Fact]
    public void TheDefault_NeedsNoScoreMapping_TheCutoffsAreTunedForIt()
    {
        var model = ClipModels.Default;

        Assert.Equal(0.31f, model.TextScore(0.31f));
        Assert.Equal(0.65f, model.SimilarScore(0.65f));
    }

    [Fact]
    public void EveryModel_IsComplete_AndHasAnIdAndFolderOfItsOwn()
    {
        Assert.Equal(ClipModels.All.Count, ClipModels.All.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ClipModels.All.Count, ClipModels.All.Select(m => m.FolderName).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var model in ClipModels.All)
        {
            Assert.Matches("^[a-z0-9-]+$", model.Id);
            Assert.False(string.IsNullOrWhiteSpace(model.Vendor), model.Id);
            Assert.False(string.IsNullOrWhiteSpace(model.Languages), model.Id); // the list of models says what each one understands
            Assert.True(model.Dimensions > 0, model.Id);
            Assert.Equal(3, model.Mean.Length);
            Assert.Equal(3, model.Std.Length);
            Assert.Equal(model.Tokens == TextTokens.Clip ? 2 : 1, model.Tokenizer.Count);
            Assert.Equal(model.Files.Count(), model.Files.Select(f => f.Name).Distinct().Count()); // no two files of a model share a name
            Assert.All(model.Files, f => Assert.StartsWith("https://", f.Url));
            Assert.True(model.Megabytes > 100, model.Id);
        }
    }

    [Fact]
    public void TheModelsThatAreNotTheDefault_AreDownloadedFromAFixedRevision_SoALaterChangeOfARepositoryCanNotBreakThem()
    {
        foreach (var model in ClipModels.All.Skip(1))
            Assert.All(model.Files, f => Assert.Matches(new Regex("/resolve/[0-9a-f]{40}/"), f.Url));
    }

    [Theory]
    [InlineData("clip-vit-b32", "clip-vit-b32")]
    [InlineData("SIGLIP2-B16", "siglip2-b16")]
    [InlineData("xlmr-vit-b32", "xlmr-vit-b32")]
    [InlineData("a-model-of-a-newer-version", "clip-vit-b32")]
    [InlineData("", "clip-vit-b32")]
    [InlineData(null, "clip-vit-b32")]
    public void Find_GivesTheModelOfAnId_AndTheDefaultForAnythingElse(string? id, string expected)
    {
        Assert.Equal(expected, ClipModels.Find(id).Id);
    }

    [Theory]
    [InlineData("siglip2-b16", 0.157f, 0.311f)]  // the median of the matching pairs of the sample lands on CLIP's
    [InlineData("siglip2-b16", -0.036f, 0.161f)] // and so does the median of the other pairs
    [InlineData("xlmr-vit-b32", 0.249f, 0.311f)]
    [InlineData("xlmr-vit-b32", 0.030f, 0.161f)]
    public void TheOtherModels_ScoreOnTheScaleOfTheCutoffs(string id, float cosine, float expected)
    {
        Assert.Equal(expected, ClipModels.Find(id).TextScore(cosine), 2);
    }

    [Theory]
    [InlineData("siglip2-b16", 0.5325f, 0.501f)] // random photos
    [InlineData("siglip2-b16", 0.9418f, 0.941f)] // near-duplicates
    [InlineData("xlmr-vit-b32", 0.4563f, 0.501f)]
    [InlineData("xlmr-vit-b32", 0.9256f, 0.941f)]
    public void TheOtherModels_ScoreSimilarPhotosOnTheScaleOfMoreLikeThis(string id, float cosine, float expected)
    {
        Assert.Equal(expected, ClipModels.Find(id).SimilarScore(cosine), 2);
    }

    [Fact]
    public void Download_ListsOnlyWhatIsMissing()
    {
        using var folder = new TempFolder();
        var model = ClipModels.SigLip2;

        Assert.Equal(model.Megabytes + 281, ModelFiles.MissingMegabytes(folder.Path, model, faces: true));
        Assert.Equal(model.Megabytes, ModelFiles.MissingMegabytes(folder.Path, model, faces: false));
        Assert.Equal(281, ModelFiles.MissingMegabytes(folder.Path, null, faces: true));

        foreach (var file in model.Files)
            File.WriteAllText(folder.File(Path.Combine(model.FolderName, file.Name)), "x");

        Assert.True(ModelFiles.ClipPresent(folder.Path, model));
        Assert.False(ModelFiles.ClipPresent(folder.Path, ClipModels.Default));
        Assert.Equal(0, ModelFiles.MissingMegabytes(folder.Path, model, faces: false));
        Assert.Equal(281, ModelFiles.MissingMegabytes(folder.Path, model, faces: true));

        File.WriteAllText(folder.File(Path.Combine("insightface-buffalo_l", "det_10g.onnx")), "x");
        File.WriteAllText(folder.File(Path.Combine("insightface-buffalo_l", "w600k_r50.onnx")), "x");

        Assert.Equal(0, ModelFiles.MissingMegabytes(folder.Path, model, faces: true));
        Assert.Equal(ClipModels.Default.Megabytes, ModelFiles.MissingMegabytes(folder.Path, ClipModels.Default, faces: true));
    }
}

public class ClipTokenizerKindsTests
{
    [Theory]
    [InlineData("Beach, September 2009!", "beach september 2009")]
    [InlineData("snow-covered mountains", "snowcovered mountains")]
    [InlineData("IMG_0042   holiday", "img 0042 holiday")]
    [InlineData("  ПАРК восени  ", "парк восени")]
    [InlineData("Tom's party", "toms party")]
    [InlineData("wedding 💍", "wedding 💍")]            // only ASCII punctuation goes (OpenCLIP's string.punctuation)
    [InlineData("«Київ» — місто", "«київ» — місто")]
    public void SigLip_CleansTheTextTheWayOpenClipDid(string text, string expected)
    {
        Assert.Equal(expected, SigLipTokenizer.Clean(text));
    }

    /// <summary>Ids from the Hugging Face tokenizers of the two models (Python), for the same texts.</summary>
    public static TheoryData<string, int[], int[]> Reference => new()
    {
        { "a cat on a sofa", [235250, 4401, 611, 476, 25062], [10, 7515, 98, 10, 60228] },
        { "Beach, September 2009", [54901, 31952, 235248, 235284, 235276, 235276, 235315], [18919, 4, 6088, 1877] },
        { "кіт на дивані", [12170, 235332, 1416, 9675, 213287], [34628, 222, 29, 159577, 260] },
        { "Діти грають на пляжі", [16887, 1652, 16246, 25588, 1416, 84611, 69986], [208934, 10099, 14632, 29, 120098, 260] },
        { "Мама", [2176, 2176], [112613] },
        { "sunset over the sea", [78231, 1163, 573, 4176], [4262, 3509, 645, 70, 15520] },
        { "snow-covered mountains", [35514, 25044, 17200], [108203, 9, 63127, 297, 101120, 7] },
        { "wedding 💍", [46682, 235248, 244130], [81141, 6, 247745] },
        { "ґанок і їжак", [241071, 235326, 21243, 7415, 33972, 4979, 235347], [6, 85991, 59, 25898, 189, 6, 2401, 105645] },
        { "x", [235297], [1022] },
    };

    private static string? TokenizerFile(ClipModelInfo model)
    {
        var path = ModelFiles.PathOf(IndexingJobTests.RealModelsFolder(), model, model.Tokenizer[0]);
        return File.Exists(path) ? path : null; // the file is there once the model has been downloaded; without it the test has nothing to check
    }

    [Theory]
    [MemberData(nameof(Reference))]
    public void SigLip_GivesTheIdsOfTheReferenceTokenizer_EndMarkAndZeros(string text, int[] siglip, int[] _)
    {
        if (TokenizerFile(ClipModels.SigLip2) is not { } file) return;

        var ids = new SigLipTokenizer(file).Encode(text);

        Assert.Equal(SigLipTokenizer.ContextLength, ids.Length);
        Assert.Equal(siglip.Select(i => (long)i), ids.Take(siglip.Length));
        Assert.Equal(SigLipTokenizer.EndOfText, ids[siglip.Length]);
        Assert.All(ids.Skip(siglip.Length + 1), id => Assert.Equal(SigLipTokenizer.Padding, id));
    }

    [Theory]
    [MemberData(nameof(Reference))]
    public void XlmRoberta_GivesTheIdsOfTheReferenceTokenizer_StartEndAndPadding(string text, int[] _, int[] xlmr)
    {
        if (TokenizerFile(ClipModels.XlmRoberta) is not { } file) return;

        var ids = new XlmRobertaTokenizer(file).Encode(text);

        Assert.Equal(XlmRobertaTokenizer.ContextLength, ids.Length);
        Assert.Equal(XlmRobertaTokenizer.StartOfText, ids[0]);
        Assert.Equal(xlmr.Select(i => (long)i), ids.Skip(1).Take(xlmr.Length));
        Assert.Equal(XlmRobertaTokenizer.EndOfText, ids[xlmr.Length + 1]);
        Assert.All(ids.Skip(xlmr.Length + 2), id => Assert.Equal(XlmRobertaTokenizer.Padding, id));
    }

    [Fact]
    public void AnEmptyText_StillGivesAFullLengthInput()
    {
        if (TokenizerFile(ClipModels.SigLip2) is { } siglip)
        {
            var ids = new SigLipTokenizer(siglip).Encode("   ");
            Assert.Equal(SigLipTokenizer.ContextLength, ids.Length);
            Assert.Equal(SigLipTokenizer.EndOfText, ids[0]);
        }
        if (TokenizerFile(ClipModels.XlmRoberta) is { } xlmr)
        {
            var ids = new XlmRobertaTokenizer(xlmr).Encode("   ");
            Assert.Equal(XlmRobertaTokenizer.ContextLength, ids.Length);
            Assert.Equal([0L, 2L], ids.Take(2));
        }
    }

    [Fact]
    public void ALongText_IsCutShort_AndStillEndsWithTheEndMark()
    {
        var text = string.Join(' ', Enumerable.Range(0, 200).Select(i => "photo" + i));

        if (TokenizerFile(ClipModels.SigLip2) is { } siglip)
        {
            var ids = new SigLipTokenizer(siglip).Encode(text);
            Assert.Equal(SigLipTokenizer.ContextLength, ids.Length);
            Assert.Equal(SigLipTokenizer.EndOfText, ids[^1]); // SigLIP reads the vector at the last position
        }
        if (TokenizerFile(ClipModels.XlmRoberta) is { } xlmr)
        {
            var ids = new XlmRobertaTokenizer(xlmr).Encode(text);
            Assert.Equal(XlmRobertaTokenizer.ContextLength, ids.Length);
            Assert.Equal(XlmRobertaTokenizer.EndOfText, ids[^1]);
        }
    }
}

public class ClipImageFitTests
{
    /// <summary>600 x 200: the left third red, the middle green, the right third blue.</summary>
    private static DecodedPhoto ThreeBands()
    {
        var pixels = new byte[600 * 200 * 3];
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 600; x++)
            {
                var p = (y * 600 + x) * 3; // B, G, R
                if (x < 200) pixels[p + 2] = 255;
                else if (x < 400) pixels[p + 1] = 255;
                else pixels[p] = 255;
            }
        }
        return new DecodedPhoto(600, 200, pixels);
    }

    private static (float R, float G, float B) PixelAt(float[] input, ClipModelInfo model, int x, int y)
    {
        var plane = model.ImageSize * model.ImageSize;
        var i = y * model.ImageSize + x;
        return (input[i], input[plane + i], input[2 * plane + i]);
    }

    [Fact]
    public void CentreCrop_LosesTheEdgesOfAWidePhoto()
    {
        var model = ClipModels.Clip;

        var input = ClipImageEncoder.Prepare(ThreeBands(), model);

        // After scaling to 224 high the photo is 672 wide and only the middle band, the green one, is left of it
        var (red, green, blue) = PixelAt(input, model, 5, 100);
        Assert.Equal((0f - model.Mean[0]) / model.Std[0], red, 2);
        Assert.Equal((1f - model.Mean[1]) / model.Std[1], green, 2);
        Assert.Equal((0f - model.Mean[2]) / model.Std[2], blue, 2);
    }

    [Fact]
    public void Squash_KeepsTheWholePhoto_AndNormalizesToMinusOneAndOne()
    {
        var model = ClipModels.SigLip2;

        var input = ClipImageEncoder.Prepare(ThreeBands(), model);

        Assert.Equal(model.InputLength, input.Length);
        Assert.Equal((1f, -1f, -1f), PixelAt(input, model, 10, 100));                     // red at the left edge
        Assert.Equal((-1f, 1f, -1f), PixelAt(input, model, model.ImageSize / 2, 100));    // green in the middle
        Assert.Equal((-1f, -1f, 1f), PixelAt(input, model, model.ImageSize - 10, 100));   // blue at the right edge
    }
}

/// <summary>The index keeps a vector for every model that has been through a photo.</summary>
public class ClipModelIndexTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private string IndexFile => SearchPaths.IndexFile(_photos.Path);

    public void Dispose()
    {
        _photos.Dispose();
        try { File.Delete(IndexFile); } catch { /* best effort */ }
    }

    private static IReadOnlyDictionary<string, ClipVector> From(params ClipModelInfo[] models) =>
        models.ToDictionary(m => m.Id, m => new ClipVector(Vectors.Unit(m, 1), IndexingJob.ClipVersion));

    private static IndexedPhoto Photo(string path, long length, params ClipModelInfo[] models) =>
        new(path, length, 1) { Clips = From(models), FaceVersion = 1 };

    private long Scalar(string sql)
    {
        using var db = new SqliteConnection($"Data Source={IndexFile};Mode=ReadOnly;Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private IReadOnlyList<string> PhotoColumns()
    {
        using var db = new SqliteConnection($"Data Source={IndexFile};Mode=ReadOnly;Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA table_info(photos)";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(1));
        return columns;
    }

    /// <summary>An index file as the versions that kept one vector per photo wrote it: the vector in the photos table (and its model, in the later ones).</summary>
    private void WriteOldIndex(string file, bool withModelColumn, params (string Path, ClipModelInfo? Model)[] photos)
    {
        using var db = new SqliteConnection($"Data Source={file};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO meta (key, value) VALUES ('schema', '1');
            CREATE TABLE photos (
                id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE COLLATE NOCASE, length INTEGER NOT NULL, modified INTEGER NOT NULL,
                clip BLOB, clip_version INTEGER NOT NULL, face_version INTEGER NOT NULL, failure TEXT,
                taken INTEGER, taken_source INTEGER NOT NULL DEFAULT 0, date_version INTEGER NOT NULL DEFAULT 0
                {(withModelColumn ? ", clip_model TEXT NOT NULL DEFAULT 'clip-vit-b32'" : string.Empty)});
            CREATE TABLE faces (
                id INTEGER PRIMARY KEY, photo_id INTEGER NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
                x REAL NOT NULL, y REAL NOT NULL, w REAL NOT NULL, h REAL NOT NULL, score REAL NOT NULL, manual INTEGER NOT NULL, embedding BLOB NOT NULL);
            """;
        command.ExecuteNonQuery();

        var length = 1000;
        foreach (var (path, model) in photos)
        {
            using var insert = db.CreateCommand();
            insert.CommandText = withModelColumn
                ? "INSERT INTO photos (path, length, modified, clip, clip_version, face_version, clip_model) VALUES ($path, $length, 5000, $clip, 1, 1, $model)"
                : "INSERT INTO photos (path, length, modified, clip, clip_version, face_version) VALUES ($path, $length, 5000, $clip, 1, 1)";
            insert.Parameters.AddWithValue("$path", path);
            insert.Parameters.AddWithValue("$length", length++);
            insert.Parameters.AddWithValue("$clip", model is null ? DBNull.Value : VectorMath.ToBytes(Vectors.Unit(model, 1, 2)));
            insert.Parameters.AddWithValue("$model", (model ?? ClipModels.Default).Id);
            insert.ExecuteNonQuery();

            if (model is null)
                continue;
            using var face = db.CreateCommand(); // a photo that was analyzed has a face
            face.CommandText = "INSERT INTO faces (photo_id, x, y, w, h, score, manual, embedding) VALUES (last_insert_rowid(), 0.1, 0.2, 0.3, 0.4, 0.9, 0, $embedding)";
            face.Parameters.AddWithValue("$embedding", VectorMath.ToBytes(Vectors.Unit(ClipModels.Default, 0, 0, 1)));
            face.ExecuteNonQuery();
        }
    }

    [Fact]
    public void APhoto_KeepsAVectorForEveryModelThatHasBeenThroughIt_AndTheCountsTellApart()
    {
        using (var index = PhotoIndex.Open(_photos.Path, IndexFile))
        {
            index.Save([
                Photo("a.jpg", 1, ClipModels.Default),
                Photo("b.jpg", 2, ClipModels.SigLip2),
                Photo("c.jpg", 3, ClipModels.Default, ClipModels.SigLip2),
                new IndexedPhoto("broken.jpg", 4, 1) { Failure = "not a picture", FaceVersion = 1 },
            ]);
        }

        using var reopened = PhotoIndex.Open(_photos.Path, IndexFile);

        var both = reopened.Find("c.jpg")!;
        Assert.Equal([ClipModels.Default.Id, ClipModels.SigLip2.Id], both.Clips.Keys.Order());
        Assert.Equal(512, both.VectorOf(ClipModels.Default.Id)!.Length);
        Assert.Equal(768, both.VectorOf(ClipModels.SigLip2.Id)!.Length);
        Assert.Equal(Vectors.Unit(ClipModels.SigLip2, 1), both.VectorOf(ClipModels.SigLip2.Id));
        Assert.Null(both.VectorOf(ClipModels.XlmRoberta.Id));
        Assert.False(reopened.Find("broken.jpg")!.HasVector);

        Assert.Equal(3, reopened.PhotoCount);                                       // the photos that have been through any model
        Assert.Equal(2, reopened.CountWithModel(ClipModels.Default.Id));
        Assert.Equal(2, reopened.CountWithModel(ClipModels.SigLip2.Id));
        Assert.Equal(0, reopened.CountWithModel(ClipModels.XlmRoberta.Id));
    }

    [Fact]
    public void SavingAPhotoAgain_ReplacesItsVectors_NeverAddsToThem()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);
        index.Save([Photo("a.jpg", 1, ClipModels.Default, ClipModels.SigLip2)]);
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM clips"));

        index.Save([Photo("a.jpg", 1, ClipModels.Default)]);

        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM clips"));
        Assert.Equal([ClipModels.Default.Id], index.Find("a.jpg")!.Clips.Keys);
    }

    [Fact]
    public void ForgettingAPhoto_ForgetsItsVectors()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);
        index.Save([Photo("a.jpg", 1, ClipModels.Default, ClipModels.SigLip2), Photo("b.jpg", 2, ClipModels.Default)]);

        index.Remove(["a.jpg"]);

        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM clips"));
        Assert.Equal(1, Scalar("SELECT COUNT(DISTINCT photo_id) FROM clips"));
    }

    [Fact]
    public void AnIndexOfTheVersionsBeforeTheChoiceOfModels_KeepsItsVectors_AsVectorsOfTheFirstModel()
    {
        WriteOldIndex(IndexFile, withModelColumn: false, ("old.jpg", ClipModels.Default), ("unreadable.jpg", null));

        using (var index = PhotoIndex.Open(_photos.Path, IndexFile))
        {
            var photo = index.Find("old.jpg")!;
            Assert.Equal([ClipModels.Default.Id], photo.Clips.Keys);
            Assert.Equal(Vectors.Unit(ClipModels.Default, 1, 2), photo.VectorOf(ClipModels.Default.Id)); // the work of hours is still there
            Assert.Equal(1, photo.Clips[ClipModels.Default.Id].Version);
            Assert.False(index.Find("unreadable.jpg")!.HasVector);
            Assert.Equal(1, index.PhotoCount);

            // The faces still belong to their photos, and the link works as before: forgetting a photo forgets its faces
            Assert.Equal(Vectors.Unit(ClipModels.Default, 0, 0, 1), Assert.Single(photo.Faces).Embedding);
            Assert.Equal(1, index.FaceCount);
            index.Remove(["old.jpg"]);
            Assert.Equal(0, index.FaceCount);
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM faces"));
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM clips"));
        }

        Assert.DoesNotContain("clip", PhotoColumns()); // the vectors moved out of the photos table
        Assert.DoesNotContain("clip_version", PhotoColumns());
        Assert.Contains("taken", PhotoColumns());      // and the rest of the table is as it was
    }

    [Fact]
    public void AnIndexOfTheVersionsWithOneVectorAndItsModel_KeepsEachVectorWithItsModel()
    {
        WriteOldIndex(IndexFile, withModelColumn: true, ("clip.jpg", ClipModels.Default), ("siglip.jpg", ClipModels.SigLip2));

        using var index = PhotoIndex.Open(_photos.Path, IndexFile);

        Assert.Equal([ClipModels.Default.Id], index.Find("clip.jpg")!.Clips.Keys);
        Assert.Equal([ClipModels.SigLip2.Id], index.Find("siglip.jpg")!.Clips.Keys);
        Assert.Equal(768, index.Find("siglip.jpg")!.VectorOf(ClipModels.SigLip2.Id)!.Length);
        Assert.Single(index.Find("clip.jpg")!.Faces);
        Assert.Single(index.Find("siglip.jpg")!.Faces);
        Assert.Equal(2, index.FaceCount);
        Assert.DoesNotContain("clip_model", PhotoColumns());
    }

    [Fact]
    public void AMigratedIndex_IsTheSameWhenOpenedAgain_AndTakesNewVectors()
    {
        WriteOldIndex(IndexFile, withModelColumn: true, ("a.jpg", ClipModels.Default));
        using (var first = PhotoIndex.Open(_photos.Path, IndexFile))
            first.Save([Photo("b.jpg", 2000, ClipModels.SigLip2)]);

        using var again = PhotoIndex.Open(_photos.Path, IndexFile);

        Assert.Equal(Vectors.Unit(ClipModels.Default, 1, 2), again.Find("a.jpg")!.VectorOf(ClipModels.Default.Id));
        Assert.Equal(Vectors.Unit(ClipModels.SigLip2, 1), again.Find("b.jpg")!.VectorOf(ClipModels.SigLip2.Id));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM clips"));
    }

    [Fact]
    public void TheCountOfAFile_IsTheSameInTheOldLayoutAndTheNew()
    {
        var old = _photos.File("old.plindex");
        WriteOldIndex(old, withModelColumn: false, ("a.jpg", ClipModels.Default), ("b.jpg", ClipModels.Default), ("broken.jpg", null));
        Assert.Equal(2, PhotoIndex.Inspect(old).Photos);

        using (var index = PhotoIndex.Open(_photos.Path, IndexFile))
        {
            index.Save([Photo("a.jpg", 1, ClipModels.Default, ClipModels.SigLip2), Photo("b.jpg", 2, ClipModels.SigLip2), Photo("c.jpg", 3)]);
            var exported = _photos.File("new.plindex");
            index.ExportTo(exported);

            Assert.Equal(2, PhotoIndex.Inspect(exported).Photos); // photos, not vectors: a is counted once
        }
    }
}

/// <summary>What the next indexing run does for the folder's model: only the photos that lack its vector, and only that vector.</summary>
public class ClipModelWorkPlanTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly PhotoIndex _index;

    public ClipModelWorkPlanTests() => _index = PhotoIndex.Open(_photos.Path, SearchPaths.IndexFile(_photos.Path));

    public void Dispose()
    {
        _index.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private PhotoFile OnDisk(string name, string content = "photo")
    {
        File.WriteAllText(_photos.File(name), content);
        return new PhotoFile(Path.Combine(_photos.Path, name), string.Empty)
        {
            Length = new FileInfo(Path.Combine(_photos.Path, name)).Length,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(Path.Combine(_photos.Path, name)),
        };
    }

    private void Indexed(PhotoFile file, string? failure = null, bool withFace = false, int version = IndexingJob.ClipVersion, params ClipModelInfo[] models)
    {
        _index.TryGetRelativePath(file.OriginalFilePath, out var relative);
        var photo = new IndexedPhoto(relative, file.Length, file.LastWriteTimeUtc.Ticks)
        {
            Clips = failure is null ? models.ToDictionary(m => m.Id, m => new ClipVector(Vectors.Unit(m, 1), version)) : IndexedPhoto.NoClips,
            FaceVersion = IndexingJob.FaceVersion,
            Failure = failure,
            Date = new PhotoDate(new DateTime(2009, 9, 1), DateSource.Exif, PhotoDates.Version),
        };
        if (withFace)
            photo.Faces = [new IndexedFace(photo, 0.1f, 0.1f, 0.2f, 0.2f, 0.9f, manual: true, Vectors.Unit(ClipModels.Default, 7))];
        _index.Save([photo]);
    }

    private IReadOnlyList<(string RelativePath, bool Clip, bool Faces)> Plan(ClipModelInfo model, params PhotoFile[] files) =>
        IndexingJob.PlanFor(_index, files, model);

    [Fact]
    public void ANewPhoto_NeedsEverything()
    {
        var plan = Plan(ClipModels.SigLip2, OnDisk("new.jpg"));

        Assert.Equal([("new.jpg", true, true)], plan);
    }

    [Fact]
    public void APhotoThatHasTheVectorOfTheModel_IsLeftAlone()
    {
        var file = OnDisk("a.jpg");
        Indexed(file, models: [ClipModels.SigLip2]);

        Assert.Empty(Plan(ClipModels.SigLip2, file));
    }

    [Fact]
    public void APhotoThatLacksTheVectorOfTheModel_GetsThatVector_ButItsFacesAreKept()
    {
        var file = OnDisk("a.jpg");
        Indexed(file, withFace: true, models: [ClipModels.Default]);

        Assert.Equal([("a.jpg", true, false)], Plan(ClipModels.SigLip2, file));
        Assert.Equal([("a.jpg", true, false)], Plan(ClipModels.XlmRoberta, file));
        Assert.Empty(Plan(ClipModels.Default, file));
    }

    [Fact]
    public void GoingBackToAModelThePhotosHaveBeenThrough_NeedsNoWork()
    {
        var file = OnDisk("a.jpg");
        Indexed(file, withFace: true, models: [ClipModels.Default, ClipModels.SigLip2]);

        Assert.Empty(Plan(ClipModels.Default, file));
        Assert.Empty(Plan(ClipModels.SigLip2, file));
        Assert.Equal([("a.jpg", true, false)], Plan(ClipModels.XlmRoberta, file)); // only a model it has not been through
    }

    [Fact]
    public void AVectorMadeByAnOlderWayOfPreparingPhotos_IsMadeAgain_AndOnlyThatOne()
    {
        var file = OnDisk("a.jpg");
        Indexed(file, version: IndexingJob.ClipVersion - 1, models: [ClipModels.Default, ClipModels.SigLip2]);

        Assert.Equal([("a.jpg", true, false)], Plan(ClipModels.Default, file));
    }

    [Fact]
    public void APhotoThatCanNotBeRead_IsNotTriedAgainForAnotherModel()
    {
        var file = OnDisk("broken.jpg");
        Indexed(file, failure: "not a picture");

        Assert.Empty(Plan(ClipModels.SigLip2, file));
    }

    [Fact]
    public void AChangedPhoto_NeedsEverythingAgain_WhateverTheModel()
    {
        var file = OnDisk("a.jpg", "photo");
        Indexed(file, models: [ClipModels.Default, ClipModels.SigLip2]);
        var changed = OnDisk("a.jpg", "another, longer photo");

        Assert.Equal([("a.jpg", true, true)], Plan(ClipModels.SigLip2, changed));
    }
}

/// <summary>Searching with the model of the folder: only the photos that have a vector of that model are looked through.</summary>
public class ClipModelSearchTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly ModelHub _models = new(modelsFolder: Path.Combine(Path.GetTempPath(), "PLNoModels_" + Guid.NewGuid()));
    private readonly FolderIndex _search;
    private int _added;

    public ClipModelSearchTests() => _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);

    public void Dispose()
    {
        _search.Dispose();
        _models.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative);

    private static readonly PhotoDate Taken = new(new DateTime(2009, 9, 1), DateSource.Exif, PhotoDates.Version);

    /// <summary>A photo that has been through these models, each making the vector that starts with <paramref name="leading"/>.</summary>
    private IndexedPhoto Add(string path, ClipModelInfo[] models, float[]? leading = null)
    {
        var photo = new IndexedPhoto(path, 1000 + _added++, 1000 * TimeSpan.TicksPerSecond)
        {
            Clips = models.ToDictionary(m => m.Id, m => new ClipVector(Vectors.Unit(m, leading ?? [1, 0]), 1)),
            FaceVersion = 1,
            Date = Taken,
        };
        _search.Index.Save([photo]);
        return photo;
    }

    private void Add(string path, ClipModelInfo model, float[]? leading = null) => Add(path, [model], leading);

    [Fact]
    public void ATextSearch_OnlySeesThePhotosThatHaveAVectorOfTheFoldersModel()
    {
        Add("old1.jpg", ClipModels.Default);
        Add("old2.jpg", ClipModels.Default);
        Add("new.jpg", ClipModels.SigLip2);
        _search.ClipModel = ClipModels.SigLip2;

        var result = _search.RankText("beach", Vectors.Unit(ClipModels.SigLip2, 1, 0), dates: null, SearchTuning.Default);

        Assert.Equal([FullPath("new.jpg")], result.Hits.Select(h => h.FullPath));
        Assert.Equal(1, result.SearchedPhotos);
        Assert.Equal(2, result.OtherModelPhotos); // the two that only the other model has been through
    }

    [Fact]
    public void APhotoThatHasBeenThroughBothModels_IsFoundWithEither_AndIsNotCountedAsLeftOut()
    {
        Add("both.jpg", [ClipModels.Default, ClipModels.SigLip2]);

        _search.ClipModel = ClipModels.Default;
        var withDefault = _search.RankText("beach", Vectors.Unit(ClipModels.Default, 1, 0), dates: null, SearchTuning.Default);
        _search.ClipModel = ClipModels.SigLip2;
        var withSigLip = _search.RankText("beach", Vectors.Unit(ClipModels.SigLip2, 1, 0), dates: null, SearchTuning.Default);

        Assert.Equal([FullPath("both.jpg")], withDefault.Hits.Select(h => h.FullPath));
        Assert.Equal([FullPath("both.jpg")], withSigLip.Hits.Select(h => h.FullPath));
        Assert.Equal(0, withDefault.OtherModelPhotos);
        Assert.Equal(0, withSigLip.OtherModelPhotos);
    }

    [Fact]
    public void TheScoresOfTheOtherModels_AreMappedToTheScaleOfTheCutoffs()
    {
        Add("new.jpg", ClipModels.SigLip2);
        _search.ClipModel = ClipModels.SigLip2;

        // A SigLIP 2 cosine of 0.157 (the median of the matching pairs) is a score of about 0.31 for the cutoffs: a match
        var query = Vectors.Unit(ClipModels.SigLip2, 0.157f, MathF.Sqrt(1 - 0.157f * 0.157f));
        var result = _search.RankText("beach", query, dates: null, SearchTuning.Default);

        Assert.False(result.Weak);
        Assert.Equal(0.311f, Assert.Single(result.Hits).Score, 2);
    }

    [Fact]
    public void WithNothingOfTheFoldersModelInTheIndex_ATextSearchFindsNothing_ButDatesAndPeopleStillWork()
    {
        Add("old.jpg", ClipModels.Default);
        _search.ClipModel = ClipModels.XlmRoberta;

        var text = _search.RankText("beach", Vectors.Unit(ClipModels.XlmRoberta, 1, 0), dates: null, SearchTuning.Default);
        var dates = _search.Search(_models, "September 2009");

        Assert.Empty(text.Hits);
        Assert.Equal(0, text.SearchedPhotos);
        Assert.Equal([FullPath("old.jpg")], dates.Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void MoreLikeThis_NeedsThePhotoToHaveAVectorOfTheFoldersModel()
    {
        Add("old.jpg", ClipModels.Default);
        Add("new.jpg", ClipModels.SigLip2);
        Add("new-too.jpg", ClipModels.SigLip2);
        _search.ClipModel = ClipModels.SigLip2;

        Assert.False(_search.CanSearchSimilar(FullPath("old.jpg")));
        Assert.Empty(_search.SearchSimilar(FullPath("old.jpg")).Hits);

        Assert.True(_search.CanSearchSimilar(FullPath("new.jpg")));
        var similar = _search.SearchSimilar(FullPath("new.jpg"));
        Assert.Equal(2, similar.Hits.Count);           // itself and its twin, not the photo only the other model has been through
        Assert.Equal(2, similar.SearchedPhotos);
    }

    private IndexedPhoto WithFace(IndexedPhoto photo, float[] face)
    {
        photo.Faces = [new IndexedFace(photo, 0.1f, 0.1f, 0.2f, 0.2f, 0.9f, manual: false, face)];
        _search.Index.Save([photo]);
        return photo;
    }

    [Fact]
    public void CopiesOfOnePhoto_AreStillOne_WhenNoModelHasBeenThroughBoth()
    {
        // Same size, same second: one photo saved twice. In the middle of a change of model one copy has the old vector and one the new,
        // vectors that can not be compared (these two would look different, and differently long)
        var face = Vectors.Unit(ClipModels.Default, 0, 0, 1);
        var old = new IndexedPhoto("a.jpg", 1234, 5 * TimeSpan.TicksPerSecond) { Clips = Vectors.Clips(Vectors.Unit(ClipModels.Default, 1)), FaceVersion = 1 };
        var changed = new IndexedPhoto("copy/a.jpg", 1234, 5 * TimeSpan.TicksPerSecond) { Clips = Vectors.Clips(ClipModels.SigLip2, 0, 1), FaceVersion = 1 };
        WithFace(old, face);
        WithFace(changed, face);

        var result = _search.SearchPerson(old.Faces[0], "mom");

        Assert.Equal([FullPath("a.jpg")], result.Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void CopiesOfOnePhoto_AreComparedByAModelBothHaveBeenThrough()
    {
        var face = Vectors.Unit(ClipModels.Default, 0, 0, 1);
        // Both have been through both models; they show different pictures to the model they share, so they are two photos
        var first = new IndexedPhoto("a.jpg", 1234, 5 * TimeSpan.TicksPerSecond)
        {
            Clips = From(Vectors.Unit(ClipModels.Default, 1), Vectors.Unit(ClipModels.SigLip2, 1)),
            FaceVersion = 1,
        };
        var second = new IndexedPhoto("b.jpg", 1234, 5 * TimeSpan.TicksPerSecond)
        {
            Clips = From(Vectors.Unit(ClipModels.Default, 0, 1), Vectors.Unit(ClipModels.SigLip2, 0, 1)),
            FaceVersion = 1,
        };
        WithFace(first, face);
        WithFace(second, face);

        var result = _search.SearchPerson(first.Faces[0], "mom");

        Assert.Equal(2, result.Hits.Count);
    }

    private static IReadOnlyDictionary<string, ClipVector> From(float[] ofDefault, float[] ofSigLip2) =>
        new Dictionary<string, ClipVector>
        {
            [ClipModels.Default.Id] = new(ofDefault, 1),
            [ClipModels.SigLip2.Id] = new(ofSigLip2, 1),
        };
}

/// <summary>The real models: they run only on a machine that has the files (see <see cref="IndexingJobTests.RealModelsFolder"/>).</summary>
public class ClipModelPipelineTests : IDisposable
{
    private static readonly string[] Extensions = [".jpg"];

    private readonly TempFolder _photos = new();
    private readonly ModelHub _models = new(IndexingJobTests.RealModelsFolder());
    private readonly FolderIndex _search;

    public ClipModelPipelineTests() => _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);

    public void Dispose()
    {
        _search.Dispose();
        _models.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    public static TheoryData<string> EveryModel
    {
        get
        {
            var ids = new TheoryData<string>();
            foreach (var model in ClipModels.All)
                ids.Add(model.Id);
            return ids;
        }
    }

    private void Write(string relativePath, byte[] content) => File.WriteAllBytes(_photos.File(relativePath), content);

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative);

    private Task<IndexSummary> IndexAsync() => _search.IndexAsync(_models, excludedFolder: null, Extensions, progress: null, CancellationToken.None);

    private void WriteThreeColours()
    {
        Write("red.jpg", IndexingJobTests.SolidJpeg(0, 0, 230));
        Write("blue.jpg", IndexingJobTests.SolidJpeg(230, 20, 0));
        Write("green.jpg", IndexingJobTests.SolidJpeg(0, 200, 20));
    }

    [Theory]
    [MemberData(nameof(EveryModel))]
    public async Task EveryModel_IndexesPhotosWithItsOwnVectors_AndFindsThemByDescription(string id)
    {
        var model = ClipModels.Find(id);
        if (!_models.IsDownloaded(model)) return;
        WriteThreeColours();
        _search.ClipModel = model;

        var summary = await IndexAsync();

        Assert.Equal(3, summary.Indexed);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(3, _search.Index.CountWithModel(model.Id));
        Assert.All(_search.Index.Snapshot(), p => Assert.Equal(model.Dimensions, p.VectorOf(model.Id)!.Length));
        Assert.All(_search.Index.Snapshot(), p => Assert.InRange(VectorMath.Dot(p.VectorOf(model.Id)!, p.VectorOf(model.Id)!), 0.999f, 1.001f)); // unit vectors

        var result = _search.SearchText(_models, "a plain red picture");
        Assert.Equal(FullPath("red.jpg"), result.Hits[0].FullPath);
    }

    [Fact]
    public async Task TheMultilingualModel_UnderstandsUkrainianAndRussian()
    {
        var model = ClipModels.XlmRoberta;
        if (!_models.IsDownloaded(model)) return;
        WriteThreeColours();
        _search.ClipModel = model;
        await IndexAsync();

        Assert.Equal(FullPath("red.jpg"), _search.SearchText(_models, "червоний").Hits[0].FullPath);
        Assert.Equal(FullPath("green.jpg"), _search.SearchText(_models, "зелёный").Hits[0].FullPath);
    }

    [Fact]
    public async Task ChangingTheModel_AddsItsVectors_KeepsTheOldOnes_AndTheFaces_AndNeedsNoFaceModels_AndGoingBackNeedsNothing()
    {
        var target = ClipModels.SigLip2;
        if (!_models.ClipDownloaded(target)) return;

        // A folder that was indexed with the first model, one face on a photo (named by hand, so it must survive)
        WriteThreeColours();
        var embedding = Vectors.Unit(ClipModels.Default, 3, 4);
        var firstVector = Vectors.Unit(ClipModels.Default, 1);
        foreach (var name in new[] { "red.jpg", "blue.jpg", "green.jpg" })
        {
            var info = new FileInfo(FullPath(name));
            var photo = new IndexedPhoto(name, info.Length, info.LastWriteTimeUtc.Ticks)
            {
                Clips = Vectors.Clips(firstVector), FaceVersion = IndexingJob.FaceVersion,
                Date = new PhotoDate(new DateTime(2009, 9, 1), DateSource.Exif, PhotoDates.Version),
            };
            if (name == "red.jpg")
                photo.Faces = [new IndexedFace(photo, 0.1f, 0.2f, 0.3f, 0.4f, 0.9f, manual: true, embedding)];
            _search.Index.Save([photo]);
        }

        // A models folder that has the new model but not the face models: if the run wanted faces it would have to download them
        using var withoutFaces = new TempFolder();
        try
        {
            foreach (var file in target.Files)
            {
                var link = withoutFaces.File(Path.Combine(target.FolderName, file.Name));
                File.CreateSymbolicLink(link, ModelFiles.PathOf(_models.ModelsFolder, target, file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // links can not be made on this machine
        }
        using var hub = new ModelHub(withoutFaces.Path);
        Assert.False(hub.FacesDownloaded);

        _search.ClipModel = target;
        var summary = await _search.IndexAsync(hub, excludedFolder: null, Extensions, progress: null, CancellationToken.None);

        Assert.Equal(3, summary.Indexed);
        Assert.Equal(0, summary.Failed);
        Assert.False(hub.FacesDownloaded);                       // nothing of the faces was needed, or downloaded
        Assert.Equal(3, _search.Index.CountWithModel(target.Id));
        Assert.Equal(3, _search.Index.CountWithModel(ClipModels.Default.Id));                // the first model's vectors are still there...
        Assert.Equal(firstVector, _search.Index.Find("red.jpg")!.VectorOf(ClipModels.Default.Id)); // ...untouched
        var face = Assert.Single(_search.Index.Find("red.jpg")!.Faces);
        Assert.True(face.Manual);
        Assert.Equal(embedding, face.Embedding);

        // Going back to the first model: nothing to do, so not a file of it is needed either
        _search.ClipModel = ClipModels.Default;
        var back = await _search.IndexAsync(hub, excludedFolder: null, Extensions, progress: null, CancellationToken.None);
        Assert.Equal(0, back.Indexed + back.Failed);

        // Both are in the index file
        _search.Dispose();
        using var reopened = PhotoIndex.Open(_photos.Path);
        Assert.Equal(3, reopened.CountWithModel(target.Id));
        Assert.Equal(3, reopened.CountWithModel(ClipModels.Default.Id));
        Assert.Equal(3, reopened.PhotoCount);
        Assert.Single(reopened.Find("red.jpg")!.Faces);
    }
}
