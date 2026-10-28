using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class DuplicateFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLDuplicates_" + Guid.NewGuid());

    public DuplicateFinderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private PhotoFile Photo(string relative, byte[] content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return new PhotoFile(path, string.Empty) { Length = content.Length };
    }

    private static byte[] Bytes(int length, int seed, params (int Offset, byte Value)[] changes)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        foreach (var (offset, value) in changes)
            bytes[offset] = (byte)(bytes[offset] ^ value | 1); // really another value
        return bytes;
    }

    private static DuplicateFindResult Find(IReadOnlyList<PhotoFile> files, bool whole = false) =>
        DuplicateFinder.Find(files, whole, null, CancellationToken.None);

    private IReadOnlyList<string> Relative(DuplicateGroup group) => [.. group.Paths.Select(p => Path.GetRelativePath(_root, p))];

    [Fact]
    public void TheSamePhoto_InTwoFolders_IsFound_WhateverItIsCalled()
    {
        var content = Bytes(500_000, 1);
        var files = new[] { Photo("107_2311/IMG_0001.jpg", content), Photo("2014-11-23/renamed beach.jpg", content), Photo("other/x.jpg", Bytes(500_000, 2)) };

        var result = Find(files);

        var group = Assert.Single(result.Groups);
        Assert.Equal(["107_2311\\IMG_0001.jpg", "2014-11-23\\renamed beach.jpg"], Relative(group));
        Assert.Equal([500_000L, 500_000L], group.Lengths);
        Assert.False(group.MetadataDiffers);
    }

    /// <summary>A JPEG: the start of image, one APP1 segment with this payload (EXIF, XMP...), the start of scan, this picture data, the end of image.</summary>
    private static byte[] Jpeg(byte[] metadata, byte[] picture)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        var length = 2 + metadata.Length;
        stream.Write([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
        stream.Write(metadata);
        stream.Write([0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0]);
        stream.Write(picture);
        stream.Write([0xFF, 0xD9]);
        return stream.ToArray();
    }

    [Fact]
    public void ThePictureDataOfAJpeg_StartsAfterItsHeader()
    {
        var jpeg = Jpeg(Bytes(3996, 1), Bytes(1000, 2));

        Assert.Equal(3996 + 16, DuplicateFinder.JpegScanStart(new MemoryStream(jpeg)));
        Assert.Equal(-1, DuplicateFinder.JpegScanStart(new MemoryStream(Bytes(1000, 3)))); // not a JPEG
        Assert.Equal(-1, DuplicateFinder.JpegScanStart(new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9, 0, 0, 0, 0]))); // the end before any picture
        Assert.Equal(-1, DuplicateFinder.JpegScanStart(new MemoryStream(jpeg[..100]))); // cut off in the header
    }

    [Fact]
    public void TheSamePictureWithAnotherXmpBlock_IsTheSamePhoto_WhichIsHowTwoCopiesOfOneCameraPhotoDiffer()
    {
        // The case of IMG_2700.JPG in two folders: the same size to the byte, the same picture, and an XMP packet of 3,996 bytes
        // that a program rewrote in place (the padding takes up the change)
        var picture = Bytes(600_000, 1);
        var xmp = Bytes(3996, 2);
        var edited = Bytes(3996, 2, (417, 5), (1200, 5), (3000, 5));
        var files = new[] { Photo("2015-05-01/IMG_2700.JPG", Jpeg(xmp, picture)), Photo("150_0105/IMG_2700.JPG", Jpeg(edited, picture)) };

        var result = Find(files);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Paths.Count);
        Assert.True(group.MetadataDiffers);
    }

    [Fact]
    public void AHeaderLongerThanTheFirstRead_IsFoundAnyway_AndGivesTheSameFingerprintAsAShortOne()
    {
        // 200 KB of metadata: more than the one read at the start of the file gets, so the segments are followed with more reads
        var picture = Bytes(600_000, 1);
        var different = new[] { Photo("a/1.jpg", LongHeader(picture, 7)), Photo("b/1.jpg", LongHeader(picture, 8)) };
        var same = new[] { Photo("c/1.jpg", LongHeader(picture, 7)), Photo("d/1.jpg", LongHeader(picture, 7)) };
        var otherPicture = new[] { Photo("e/1.jpg", LongHeader(picture, 7)), Photo("f/1.jpg", LongHeader(Bytes(600_000, 2), 7)) };

        var group = Assert.Single(Find(different).Groups);
        Assert.Equal(2, group.Paths.Count);
        Assert.True(group.MetadataDiffers);
        Assert.False(Assert.Single(Find(same).Groups).MetadataDiffers);
        Assert.Empty(Find(otherPicture).Groups);
    }

    /// <summary>A JPEG with four APP1 segments of 60 KB: a header of 240 KB.</summary>
    private static byte[] LongHeader(byte[] picture, int seed)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        for (var i = 0; i < 4; i++)
        {
            var metadata = Bytes(60_000, seed + i);
            var length = 2 + metadata.Length;
            stream.Write([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
            stream.Write(metadata);
        }
        stream.Write([0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0]);
        stream.Write(picture);
        stream.Write([0xFF, 0xD9]);
        return stream.ToArray();
    }

    [Fact]
    public void ACopyWhoseMetadataGrew_IsOnlyFoundWhenPhotosThatShareTheirNameAreLookedAtToo()
    {
        // "301CANON\IMG_4859.JPG" and "2015-05-02\IMG_4859.JPG": the same picture, an XMP block of 4 KB more in one: another size
        var picture = Bytes(600_000, 1);
        var files = new[]
        {
            Photo("301CANON/IMG_4859.JPG", Jpeg(Bytes(1000, 2), picture)),
            Photo("2015-05-02/IMG_4859.JPG", Jpeg(Bytes(5400, 3), picture)),
            Photo("2015-05-02/renamed.JPG", Jpeg(Bytes(5000, 4), picture)), // the same picture again, but another size and another name
        };

        Assert.Empty(Find(files).Groups);

        var result = DuplicateFinder.Find(files, false, null, CancellationToken.None, includeSameName: true);

        var group = Assert.Single(result.Groups);
        Assert.All(group.Paths, p => Assert.EndsWith("IMG_4859.JPG", p));
        Assert.Equal(2, group.Lengths.Distinct().Count()); // the sizes differ, each copy has its own
        Assert.True(group.MetadataDiffers);
        Assert.Equal(2, result.Candidates); // the renamed one shares neither its size nor its name with another
    }

    [Fact]
    public void ThePictureOfOneJpegInTwoFolders_WithTheSameMetadata_IsNotMarkedAsDifferent()
    {
        var jpeg = Jpeg(Bytes(3996, 2), Bytes(600_000, 1));
        var files = new[] { Photo("a/1.jpg", jpeg), Photo("b/1.jpg", jpeg) };

        Assert.False(Assert.Single(Find(files).Groups).MetadataDiffers);
    }

    [Fact]
    public void TwoJpegsWithTheSameHeaderAndAnotherPicture_AreNotDuplicates()
    {
        var header = Bytes(3996, 2);
        foreach (var offset in new[] { 10, 300_000, 599_990 })
        {
            var files = new[] { Photo($"a{offset}.jpg", Jpeg(header, Bytes(600_000, 1))), Photo($"b{offset}.jpg", Jpeg(header, Bytes(600_000, 1, (offset, 9)))) };

            Assert.Empty(Find(files).Groups);
        }
    }

    [Fact]
    public void ADifferenceInThePictureBetweenTheSamples_IsOnlySeenWhenAllOfThePictureIsCompared()
    {
        var header = Bytes(3996, 2);
        var files = new[] { Photo("a.jpg", Jpeg(header, Bytes(600_000, 1))), Photo("b.jpg", Jpeg(header, Bytes(600_000, 1, (120_000, 9)))) };

        Assert.Single(Find(files, whole: false).Groups);
        Assert.Empty(Find(files, whole: true).Groups);
    }

    [Fact]
    public void AJpegThatIsNotUnderstood_IsComparedAsAFile()
    {
        // no start of scan at all: only the whole file can tell
        var broken = new byte[400_000];
        new Random(4).NextBytes(broken);
        broken[0] = 0xFF;
        broken[1] = 0xD8;
        broken[2] = 0x00; // not a marker
        var other = (byte[])broken.Clone();
        other[200_000] ^= 0x55; // in the middle block

        Assert.Empty(Find([Photo("a.jpg", broken), Photo("b.jpg", other)]).Groups);
        Assert.Single(Find([Photo("c.jpg", broken), Photo("d.jpg", broken)]).Groups);
    }

    [Fact]
    public void TheSameNameAndSize_ButAnotherPhoto_IsNotADuplicate()
    {
        var files = new[] { Photo("a/IMG_0001.jpg", Bytes(500_000, 1)), Photo("b/IMG_0001.jpg", Bytes(500_000, 2)) };

        var result = Find(files);

        Assert.Empty(result.Groups);
        Assert.Equal(2, result.Candidates); // they were looked at, and told apart
    }

    [Fact]
    public void OnlyFilesOfTheSameSize_AreLookedAt()
    {
        var files = new[] { Photo("a.jpg", Bytes(1000, 1)), Photo("b.jpg", Bytes(2000, 1)), Photo("c.jpg", Bytes(3000, 1)) };

        var result = Find(files);

        Assert.Equal(0, result.Candidates);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public void AnEmptyFile_IsNeverTakenForACopyOfAnotherEmptyFile()
    {
        var files = new[] { Photo("a.jpg", []), Photo("b.jpg", []) };

        Assert.Empty(Find(files).Groups);
    }

    [Fact]
    public void ASmallFile_IsComparedWholeEvenWithoutAskingForIt()
    {
        var files = new[] { Photo("a.jpg", Bytes(100_000, 1)), Photo("b.jpg", Bytes(100_000, 1, (50_000, 7))) };

        Assert.Empty(Find(files).Groups);
    }

    [Fact]
    public void ABigFilesDifferenceAtTheStartTheMiddleOrTheEnd_IsSeenByTheSamples()
    {
        var length = 600_000;
        var original = Bytes(length, 1);
        foreach (var offset in new[] { 10, length / 2, length - 10 })
        {
            var files = new[] { Photo($"a{offset}.jpg", original), Photo($"b{offset}.jpg", Bytes(length, 1, (offset, 9))) };

            Assert.Empty(Find(files).Groups);
        }
    }

    [Fact]
    public void ADifferenceBetweenTheSamples_IsOnlySeenWhenTheWholeFilesAreCompared()
    {
        var length = 600_000;
        var files = new[] { Photo("a.jpg", Bytes(length, 1)), Photo("b.jpg", Bytes(length, 1, (120_000, 9))) }; // after the first block, before the middle one

        Assert.Single(Find(files, whole: false).Groups); // the price of the fast way: this is what the whole-file comparison is for
        Assert.Empty(Find(files, whole: true).Groups);
    }

    [Fact]
    public void ThreeCopies_AreOneGroup_AndTwoPairsAreTwoGroups()
    {
        var one = Bytes(400_000, 1);
        var two = Bytes(400_000, 2);
        var files = new[] { Photo("a/1.jpg", one), Photo("b/1.jpg", one), Photo("c/1.jpg", one), Photo("a/2.jpg", two), Photo("b/2.jpg", two) };

        var result = Find(files);

        Assert.Equal([3, 2], result.Groups.Select(g => g.Paths.Count).OrderDescending());
    }

    [Fact]
    public void AFileThatCanNotBeRead_IsCounted_AndTheOthersAreStillFound()
    {
        var content = Bytes(400_000, 1);
        var files = new[] { Photo("a.jpg", content), Photo("b.jpg", content), Photo("locked.jpg", content) };

        using (new FileStream(files[2].OriginalFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = Find(files);

            Assert.Equal(1, result.Unreadable);
            Assert.Equal(2, Assert.Single(result.Groups).Paths.Count);
        }
    }

    [Fact]
    public void AFileThatChangedSizeSinceTheListing_IsNotTrusted()
    {
        var content = Bytes(400_000, 1);
        var a = Photo("a.jpg", content);
        var b = Photo("b.jpg", content);
        File.WriteAllBytes(b.OriginalFilePath, Bytes(300_000, 5)); // saved again after the folder was listed

        var result = Find([a, b]);

        Assert.Empty(result.Groups);
        Assert.Equal(1, result.Unreadable);
    }

    [Fact]
    public void ProgressIsReported_AndAStopStops()
    {
        var content = Bytes(300_000, 1);
        var files = Enumerable.Range(0, 60).Select(i => Photo($"f{i}/a.jpg", i % 2 == 0 ? content : Bytes(300_000, 100 + i))).ToList();
        var reports = new List<DuplicateProgress>();

        DuplicateFinder.Find(files, false, new Immediate(reports.Add), CancellationToken.None);

        Assert.Equal(new DuplicateProgress(60, 60, reports[^1].BytesRead), reports[^1]);
        Assert.True(reports[^1].BytesRead > 0);

        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DuplicateFinder.Find(files, false, null, stop.Token));
    }

    private sealed class Immediate(Action<DuplicateProgress> handler) : IProgress<DuplicateProgress>
    {
        public void Report(DuplicateProgress value) => handler(value);
    }
}

public class DuplicateRulesTests
{
    private const string Root = @"C:\Library";

    private static DuplicateGroup Group(params string[] relativePaths) =>
        new(1000, [.. relativePaths.Select(p => Path.Combine(Root, p))]);

    private static IReadOnlyList<DuplicatePlan> Plan(DuplicateOptions? options, params DuplicateGroup[] groups) =>
        DuplicateRules.Plan(groups, Root, options ?? DuplicateOptions.Default);

    private static string? KeptIn(DuplicatePlan plan) => plan.Keeper is { } k ? Path.GetRelativePath(Root, plan.Copies[k].Path) : null;

    [Theory]
    [InlineData("2014-11-23", true)]
    [InlineData("2015-04-18", true)]
    [InlineData("20141123", true)]
    [InlineData("2014_11_23", true)]
    [InlineData("23.11.2014", true)]
    [InlineData("2014", true)]
    [InlineData("2014-11", true)]
    [InlineData("2014-11-23 Anna's birthday", true)]
    [InlineData("Crete 2012-07", true)]
    [InlineData("107_2311", false)]
    [InlineData("115_2012", false)]
    [InlineData("110_2911", false)]
    [InlineData("147_1804", false)]
    [InlineData("135___07", false)]
    [InlineData("314CANON", false)]
    [InlineData("Holidays", false)]
    public void ADateInTheNameOfAFolder_IsToldFromACameraCounter(string name, bool date)
    {
        Assert.Equal(date, DuplicateRules.IsDateName(name));
    }

    [Theory]
    [InlineData("107_2311", true)]
    [InlineData("115_2012", true)]
    [InlineData("135___07", true)]
    [InlineData("147_1804", true)]
    [InlineData("314CANON", true)]
    [InlineData("100NIKON", true)]
    [InlineData("100MSDCF", true)]
    [InlineData("102", true)]
    [InlineData("IMG_1234", true)]
    [InlineData("Fotik1 Leftovers", false)]
    [InlineData("\u041d\u0430\u0442\u0430\u0448\u0438\u043d \u0444\u043e\u0442\u0438\u043a", false)]
    public void ACameraCounter_IsRecognized(string name, bool counter)
    {
        Assert.Equal(counter, DuplicateRules.IsCounterName(name));
    }

    [Theory]
    [InlineData("\u041d\u0430\u0442\u0430\u0448\u0438\u043d \u0444\u043e\u0442\u0438\u043a", true)]
    [InlineData("Fotik1 Leftovers", true)]
    [InlineData("Crete trip", true)]
    [InlineData("Photo", false)]
    [InlineData("DCIM", false)]
    [InlineData("New folder", false)]
    [InlineData("135___07", false)]
    [InlineData("2014-11-23", false)]
    [InlineData("314CANON", false)]
    [InlineData("12345678901", false)]
    public void AFolderNameThatMeansSomething_IsToldFromTheOthers(string name, bool meaningful)
    {
        Assert.Equal(meaningful, DuplicateRules.IsMeaningful(name));
    }

    [Fact]
    public void ADatedFolder_BeatsACameraFolder()
    {
        var plans = Plan(null, Group(@"Photo\107_2311\IMG_1.jpg", @"Photo\2014-11-23\IMG_1.jpg"));

        Assert.Equal(@"Photo\2014-11-23\IMG_1.jpg", KeptIn(plans[0]));
        Assert.Contains("date", plans[0].Copies[1].Reason);
        Assert.Contains("107_2311", plans[0].Copies[0].Reason);
    }

    [Fact]
    public void AnythingInsideADeletedFolder_Loses_AlsoToACameraFolder()
    {
        var dated = Plan(null, Group(@"Photo\2015-04-18\a.jpg", @"Photo\Deleted\147_1804\a.jpg"));
        var camera = Plan(null, Group(@"Photo\147_1804\a.jpg", @"Photo\Deleted\147_1804\a.jpg"));

        Assert.Equal(@"Photo\2015-04-18\a.jpg", KeptIn(dated[0]));
        Assert.Equal(@"Photo\147_1804\a.jpg", KeptIn(camera[0]));
        Assert.Equal(FolderRank.Avoid, dated[0].Copies[1].Rank);
    }

    [Theory]
    [InlineData("IMG_7359 - копия", true)]
    [InlineData("IMG_7359 - Копия (2)", true)]
    [InlineData("IMG_1234 - Copy", true)]
    [InlineData("IMG_1234 (2)", true)]
    [InlineData("Copy of IMG_1234", true)]
    [InlineData("Копия IMG_1234", true)]
    [InlineData("IMG_1234_copy", true)]
    [InlineData("IMG_7359", false)]
    [InlineData("Beach, renamed", false)]
    [InlineData("Copyright notes", false)]
    [InlineData("Photocopy 3", false)]
    [InlineData("IMG_(2)x", false)]
    public void AFileNamedLikeACopy_IsToldFromOneThatIsNot(string name, bool copy)
    {
        Assert.Equal(copy, DuplicateRules.IsCopyName(name));
    }

    [Fact]
    public void InTheSameFolder_TheFileNamedLikeACopyMovesAway_AndAFolderStillBeatsAName()
    {
        var plans = Plan(null,
            Group(@"Photo\468CANON\IMG_5407 - копия.JPG".Replace(@"копия", "копия"), @"Photo\468CANON\IMG_5407.JPG"),
            Group(@"Photo\2015-04-18\IMG_1 (2).jpg", @"Photo\107_2311\IMG_1.jpg"),
            Group(@"Photo\Alpha\a.jpg", @"Photo\Beta\a.jpg"));

        Assert.Equal(@"Photo\468CANON\IMG_5407.JPG", KeptIn(plans[0]));
        Assert.Contains("named like a copy", plans[0].Copies.Single(c => c.Path.Contains("копия")).Reason);
        Assert.Equal(@"Photo\2015-04-18\IMG_1 (2).jpg", KeptIn(plans[1])); // the folder is what counts first
        Assert.False(plans[2].Decided);
    }

    [Fact]
    public void WhenAllTheCopiesAreInADeletedFolder_TheBetterNamedFolderStillWins_AndOneCopyAlwaysStays()
    {
        var plans = Plan(null,
            Group(@"Photo\Deleted\147_1804\a.jpg", @"Photo\Deleted\2015-04-18\a.jpg"),
            Group(@"Photo\Deleted\400CANON\b.jpg", @"Photo\Deleted\401CANON\b.jpg"),
            Group(@"Photo\Alpha\c.jpg", @"Photo\Beta\c.jpg")); // a group elsewhere: what every copy shares says nothing, and Photo\Deleted is not shared by all

        Assert.Equal(@"Photo\Deleted\2015-04-18\a.jpg", KeptIn(plans[0]));
        Assert.Contains("Deleted", plans[0].Copies[0].Reason);
        Assert.False(plans[1].Decided); // two cards: nothing says which
        Assert.All(plans, plan => Assert.True(plan.ToMove.Count() < plan.Copies.Count));
    }

    [Theory]
    [InlineData(@"Photo\Liked\a.jpg", true)]
    [InlineData(@"Photo\liked\sub\a.jpg", true)]
    [InlineData("Photo\\Liked\\D - Group Златка\\a.jpg", true)]
    [InlineData(@"Photo\A - Group\a.jpg", true)]
    [InlineData(@"Photo\2015-04-18\a.jpg", false)]
    [InlineData(@"Photo\Group photos\a.jpg", false)]
    [InlineData(@"a.jpg", false)]
    public void PhotosInTheLikedAndGroupFolders_AreCopiesOnPurpose(string relative, bool deliberate)
    {
        Assert.Equal(deliberate, DuplicateRules.IsDeliberateCopy(Root, Path.Combine(Root, relative)));
    }

    [Fact]
    public void AFolderWithANameOfItsOwn_BeatsACameraFolder_EvenWhenTheCameraFolderIsInIt()
    {
        // Y:\OldFotik\Photo\135___07 and Y:\OldFotik\Photo\<Natasha's camera>\135___07
        var plans = Plan(null, Group(@"Photo\135___07\x.jpg", "Photo\\\u041d\u0430\u0442\u0430\u0448\u0438\u043d \u0444\u043e\u0442\u0438\u043a\\135___07\\x.jpg"));

        Assert.Equal("Photo\\\u041d\u0430\u0442\u0430\u0448\u0438\u043d \u0444\u043e\u0442\u0438\u043a\\135___07\\x.jpg", KeptIn(plans[0]));
        Assert.Contains("name of its own", plans[0].Copies[1].Reason);
    }

    [Fact]
    public void TwoGoodFolders_AreLeftToAPerson_AndSoAreTwoCameraFolders()
    {
        var plans = Plan(null,
            Group(@"Photo\2015-04-18\a.jpg", @"Photo\2015-04-19\a.jpg"),
            Group(@"Photo\Crete\a.jpg", @"Photo\2015-04-18\b.jpg"),
            Group(@"Photo\107_2311\a.jpg", @"Photo\108_2411\a.jpg"));

        Assert.All(plans, plan => Assert.False(plan.Decided));
        Assert.All(plans, plan => Assert.Empty(plan.ToMove));
    }

    [Fact]
    public void WithThreeCopies_AGoodFolderAmongBadOnes_Decides()
    {
        var plans = Plan(null, Group(@"Photo\107_2311\a.jpg", @"Photo\2014-11-23\a.jpg", @"Photo\Deleted\107_2311\a.jpg"));

        Assert.Equal(@"Photo\2014-11-23\a.jpg", KeptIn(plans[0]));
        Assert.Equal(2, plans[0].ToMove.Count());
    }

    [Fact]
    public void ThePartOfThePathAllTheCopiesShare_SaysNothing()
    {
        // every photo is in "Holiday"; it must not make the copy in a camera folder a "named" one
        var plans = Plan(null, Group(@"Holiday\107_2311\a.jpg", @"Holiday\2014-11-23\a.jpg"), Group(@"Holiday\Deleted\b.jpg", @"Holiday\115_2012\b.jpg"));

        Assert.Equal(@"Holiday\2014-11-23\a.jpg", KeptIn(plans[0]));
        Assert.Equal(@"Holiday\115_2012\b.jpg", KeptIn(plans[1]));
    }

    [Fact]
    public void APhotoDirectlyInTheFolder_HasNoFolderOfItsOwn_AndLosesToANamedOne()
    {
        var plans = Plan(null, Group(@"a.jpg", @"2014-11-23\a.jpg"));

        Assert.Equal(@"2014-11-23\a.jpg", KeptIn(plans[0]));
    }

    [Fact]
    public void AFolderToKeep_BeatsEverything_AndAFolderToMoveFromLosesToEverything()
    {
        var keep = DuplicateOptions.Default with { KeepPatterns = DuplicateOptions.Split("107_*") };
        var move = DuplicateOptions.Default with { MovePatterns = DuplicateOptions.Split("2014-11; Crete") };
        var group = Group(@"Photo\107_2311\a.jpg", @"Photo\2014-11-23\a.jpg");

        Assert.Equal(@"Photo\107_2311\a.jpg", KeptIn(Plan(keep, group)[0]));
        Assert.Equal(@"Photo\107_2311\a.jpg", KeptIn(Plan(move, group)[0]));
        Assert.Equal(FolderRank.Keep, Plan(keep, group)[0].Copies[0].Rank);
        Assert.Equal(FolderRank.Avoid, Plan(move, group)[0].Copies[1].Rank);
    }

    [Fact]
    public void Patterns_AreTextToFind_WithWildcards_AndCaseDoesNotMatter()
    {
        var plain = DuplicateRules.Patterns(["deleted"]);
        var wild = DuplicateRules.Patterns([@"photo\20??-*"]);

        Assert.Matches(plain[0], @"Photo\Deleted\147_1804");
        Assert.Matches(wild[0], @"Photo\2015-04-18");
        Assert.DoesNotMatch(wild[0], @"Photo\107_2311");
        Assert.Equal(["a", "b c", "d"], DuplicateOptions.Split(" a ; b c ;;d\r\n"));
        Assert.Empty(DuplicateOptions.Split(null));
    }

    [Fact]
    public void APersonsChoice_MakesThatCopyTheOneThatStays()
    {
        var plan = Plan(null, Group(@"Photo\Alpha\a.jpg", @"Photo\Beta\a.jpg"))[0];

        var chosen = plan.KeepingCopy(Path.Combine(Root, @"Photo\Beta\A.JPG"));

        Assert.True(chosen.Decided);
        Assert.Equal(Path.Combine(Root, @"Photo\Alpha\a.jpg"), Assert.Single(chosen.ToMove).Path);
        Assert.Same(plan, plan.KeepingCopy(Path.Combine(Root, "nothing.jpg")));
    }
}

public class DuplicateMoverTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLDuplicateMover_" + Guid.NewGuid());

    public DuplicateMoverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Photo(string relative, string content = "the picture")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private DuplicatePlan PlanOf(string keeper, params string[] others)
    {
        static long Size(string path) => new FileInfo(path).Length;
        return new(new DuplicateGroup([keeper, .. others], [Size(keeper), .. others.Select(Size)]),
            [new DuplicateCopy(keeper, FolderRank.Named, "k", FolderRank.Named, Size(keeper)),
             .. others.Select(o => new DuplicateCopy(o, FolderRank.Counter, "o", FolderRank.Counter, Size(o)))], 0);
    }

    private DuplicateMoveReport Move(params DuplicatePlan[] plans) =>
        DuplicateMover.Move(plans, _root, DuplicateMover.NewLogFile(_root, Now), null, CancellationToken.None);

    private string Duplicated(string relative) => Path.Combine(_root, "Deleted", "Duplicated", relative);

    [Fact]
    public void TheCopies_AreMovedToDeletedDuplicated_WithTheirPath_AndTheOneThatStaysIsNotTouched()
    {
        var keeper = Photo(@"2014-11-23\IMG_1.jpg");
        var copy = Photo(@"107_2311\IMG_1.jpg");

        var report = Move(PlanOf(keeper, copy));

        Assert.Equal([new DuplicateMove(copy, Duplicated(@"107_2311\IMG_1.jpg"))], report.Moved);
        Assert.False(File.Exists(copy));
        Assert.Equal("the picture", File.ReadAllText(Duplicated(@"107_2311\IMG_1.jpg")));
        Assert.True(File.Exists(keeper));
        Assert.Empty(report.Failures);
        Assert.Equal("the picture".Length, report.Bytes);
    }

    [Fact]
    public void AnUndecidedGroup_IsNotTouched()
    {
        var a = Photo(@"a\1.jpg");
        var b = Photo(@"b\1.jpg");
        var plan = new DuplicatePlan(new DuplicateGroup(11, [a, b]), [new(a, FolderRank.Named, "x"), new(b, FolderRank.Named, "x")], null);

        var report = Move(plan);

        Assert.Empty(report.Moved);
        Assert.True(File.Exists(a) && File.Exists(b));
    }

    [Fact]
    public void ACopyIsOnlyMoved_IfThePhotoThatStaysIsStillThere()
    {
        var keeper = Photo(@"good\1.jpg");
        var copy = Photo(@"camera\1.jpg");
        var plan = PlanOf(keeper, copy);
        File.Delete(keeper);

        var report = Move(plan);

        Assert.Empty(report.Moved);
        Assert.Contains("gone", Assert.Single(report.Failures).Message);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void ACopyThatWasChangedSinceTheListing_IsNotMoved()
    {
        var keeper = Photo(@"good\1.jpg");
        var copy = Photo(@"camera\1.jpg");
        var plan = PlanOf(keeper, copy);
        File.WriteAllText(copy, "saved again, and longer");

        var report = Move(plan);

        Assert.Empty(report.Moved);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void ACopyWithTheNameOfOneThatWasMovedBefore_GetsANumber_NotOverwritten()
    {
        var first = Photo(@"camera\1.jpg", "one");
        var keeper1 = Photo(@"good\1.jpg", "one");
        Move(PlanOf(keeper1, first));
        var second = Photo(@"camera\1.jpg", "two");
        var keeper2 = Photo(@"better\1.jpg", "two");

        var report = Move(PlanOf(keeper2, second));

        Assert.Equal(Duplicated(@"camera\1 (2).jpg"), report.Moved.Single().To);
        Assert.Equal("one", File.ReadAllText(Duplicated(@"camera\1.jpg")));
    }

    [Fact]
    public void TheMovesAreLoggedAhead_AndTheLogIsNextToThePhotos()
    {
        var keeper = Photo(@"good\1.jpg");
        var copy = Photo(@"camera\1.jpg");

        var report = Move(PlanOf(keeper, copy));

        Assert.Equal(_root, Path.GetDirectoryName(report.LogFile));
        Assert.Equal(report.LogFile, DuplicateMover.LatestLog(_root));
        var lines = File.ReadAllLines(report.LogFile);
        Assert.Equal(2, lines.Length);
        Assert.Contains("camera", lines[1]);
    }

    [Fact]
    public void AnUndo_PutsTheCopiesBack_RemovesTheFoldersItEmptied_AndTheLogIsDone()
    {
        var keeper = Photo(@"good\1.jpg");
        var copy = Photo(@"camera\sub\1.jpg");
        var report = Move(PlanOf(keeper, copy));

        var undone = DuplicateMover.Undo(report.LogFile, _root, null, CancellationToken.None);

        Assert.Equal("the picture", File.ReadAllText(copy));
        Assert.Single(undone.Moved);
        Assert.Equal(0, undone.Skipped);
        Assert.False(Directory.Exists(Path.Combine(_root, "Deleted", "Duplicated", "camera")));
        Assert.Null(DuplicateMover.LatestLog(_root));
        Assert.True(File.Exists(report.LogFile[..^".jsonl".Length] + ".undone.jsonl"));
    }

    [Fact]
    public void AnUndo_LeavesACopyWhereItIs_IfItsOldPlaceIsTaken()
    {
        var keeper = Photo(@"good\1.jpg", "old");
        var copy = Photo(@"camera\1.jpg", "old");
        var report = Move(PlanOf(keeper, copy));
        Photo(@"camera\1.jpg", "somebody put another photo here");

        var undone = DuplicateMover.Undo(report.LogFile, _root, null, CancellationToken.None);

        Assert.Equal(1, undone.Skipped);
        Assert.Equal("somebody put another photo here", File.ReadAllText(copy));
        Assert.True(File.Exists(Duplicated(@"camera\1.jpg")));
    }

    [Fact]
    public void ALogThatIsNotOne_IsRefused()
    {
        var file = Photo("photo-liker-duplicates-x.jsonl", "this is not json");

        Assert.Throws<InvalidDataException>(() => DuplicateMover.Undo(file, _root, null, CancellationToken.None));
    }

    [Fact]
    public void ARunThatIsStoppedBeforeItStarts_MovesNothing()
    {
        var keeper = Photo(@"good\1.jpg");
        var copy = Photo(@"camera\1.jpg");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var report = DuplicateMover.Move([PlanOf(keeper, copy)], _root, DuplicateMover.NewLogFile(_root, Now), null, stop.Token);

        Assert.True(report.Cancelled);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void ManyCopies_AreMovedInChunks_AndReported()
    {
        var plans = Enumerable.Range(0, 250).Select(i => PlanOf(Photo($"good\\{i}.jpg"), Photo($"camera\\{i}.jpg"))).ToArray();
        var reports = new List<int>();

        var report = DuplicateMover.Move(plans, _root, DuplicateMover.NewLogFile(_root, Now), new Immediate(reports.Add), CancellationToken.None);

        Assert.Equal(250, report.Moved.Count);
        Assert.Equal([100, 200, 250], reports);
        Assert.Equal(251, File.ReadAllLines(report.LogFile).Length);
    }

    [Fact]
    public void TheDestinationOfADuplicate_IsInsideDeletedDuplicated()
    {
        Assert.Equal(Path.Combine(_root, "Deleted", "Duplicated"), DeletedPhotos.DuplicatesFolderOf(_root));
        Assert.Equal(Duplicated(@"a\b.jpg"), DeletedPhotos.DestinationOf(_root, Path.Combine(_root, "a", "b.jpg"), DeletedPhotos.DuplicatesFolderName));
        Assert.Equal(Path.Combine(_root, "Deleted", "a", "b.jpg"), DeletedPhotos.DestinationOf(_root, Path.Combine(_root, "a", "b.jpg")));
    }

    private sealed class Immediate(Action<int> handler) : IProgress<int>
    {
        public void Report(int value) => handler(value);
    }
}
