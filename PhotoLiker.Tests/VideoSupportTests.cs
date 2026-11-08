using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Videos in the list of a folder: which files are videos, the setting that lists them, and the rows that describe them in the metadata grid.</summary>
[Collection("Video extensions")] // MediaKinds is one list for the process
public class VideoSupportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLVideoSupportTests_" + Guid.NewGuid());

    public VideoSupportTests()
    {
        Directory.CreateDirectory(_root);
        MediaKinds.UseVideoExtensions(null);
    }

    public void Dispose()
    {
        MediaKinds.UseVideoExtensions(null);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    // ── which files are videos ─────────────────────────────────────────

    [Theory]
    [InlineData("MVI_4069.AVI", true)]    // a Canon camera
    [InlineData("VID_20231202.mp4", true)] // a phone
    [InlineData("clip.MOV", true)]
    [InlineData("show.mkv", true)]
    [InlineData("camcorder.MTS", true)]
    [InlineData(@"C:\a b\sub\x.3gp", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("photo.png", false)]
    [InlineData("notes.txt", false)]
    [InlineData("noextension", false)]
    [InlineData("mp4", false)]       // a name, not an extension
    [InlineData("archive.mp4.zip", false)]
    [InlineData("", false)]
    public void TheExtension_TellsAVideoFromAPhoto(string path, bool video) => Assert.Equal(video, MediaKinds.IsVideo(path));

    [Fact]
    public void ANullPath_IsNoVideo() => Assert.False(MediaKinds.IsVideo((string?)null));

    [Fact]
    public void TheListOfExtensions_CanBeChanged_WhateverTheCaseAndTheDot()
    {
        MediaKinds.UseVideoExtensions([".XYZ", "abc", " .Def "]);

        Assert.True(MediaKinds.IsVideo("a.xyz"));
        Assert.True(MediaKinds.IsVideo("a.ABC"));
        Assert.True(MediaKinds.IsVideo("a.def"));
        Assert.False(MediaKinds.IsVideo("a.mp4")); // not in the list any more
    }

    [Fact]
    public void AnEmptyList_MeansNothingIsAVideo()
    {
        MediaKinds.UseVideoExtensions([]);

        Assert.False(MediaKinds.IsVideo("a.mp4"));
    }

    [Fact]
    public void NothingGiven_IsTheDefaultList()
    {
        MediaKinds.UseVideoExtensions([".xyz"]);
        MediaKinds.UseVideoExtensions(null);

        Assert.True(MediaKinds.IsVideo("a.mp4"));
        Assert.False(MediaKinds.IsVideo("a.xyz"));
    }

    [Fact]
    public void APhotoFile_KnowsItIsAVideo()
    {
        Assert.True(new PhotoFile(@"C:\v\MVI_1.AVI", string.Empty).IsVideo);
        Assert.False(new PhotoFile(@"C:\v\IMG_1.JPG", string.Empty).IsVideo);
    }

    [Fact]
    public void TheDefaultList_HasTheFormatsOfCamerasAndPhones()
    {
        foreach (var extension in new[] { ".avi", ".mov", ".mp4", ".3gp", ".mts", ".mkv", ".wmv" })
            Assert.Contains(extension, MediaKinds.DefaultVideoExtensions);

        Assert.DoesNotContain(MediaKinds.DefaultVideoExtensions, e => new Settings().Extensions.Contains(e)); // no extension is both
    }

    // ── the setting ────────────────────────────────────────────────────

    [Fact]
    public void Videos_AreOffUnlessTurnedOn()
    {
        var settings = new Settings();

        Assert.False(settings.SupportVideo);
        Assert.Equal(settings.Extensions, settings.ListedExtensions);
        Assert.DoesNotContain(".mp4", settings.ListedExtensions);
    }

    [Fact]
    public void WithVideosOn_TheListedExtensions_AreThePhotosAndTheVideos()
    {
        var settings = new Settings { SupportVideo = true, VideoExtensions = [".avi", ".mp4"] };

        Assert.Equal([.. settings.Extensions, ".avi", ".mp4"], settings.ListedExtensions);
        Assert.Equal(settings.Extensions, settings.Extensions); // indexing and the tools keep working on the photos only
        Assert.DoesNotContain(".avi", settings.Extensions);
    }

    [Fact]
    public void TheSetting_IsKeptWithTheFolder()
    {
        new Settings { CurrentFolder = _root, SupportVideo = true, VideoExtensions = [".avi", ".xyz"] }.Save(_root);

        var loaded = Settings.Load(_root);

        Assert.True(loaded.SupportVideo);
        Assert.Equal([".avi", ".xyz"], loaded.VideoExtensions);
    }

    [Fact]
    public void AFolderSavedBeforeVideos_HasThemOff_WithTheDefaultList()
    {
        File.WriteAllText(Settings.GetFilePath(_root), """{ "CacheSize": 33, "Extensions": [".jpg"] }""");

        var loaded = Settings.Load(_root);

        Assert.Equal(33, loaded.CacheSize);
        Assert.False(loaded.SupportVideo);
        Assert.Equal(MediaKinds.DefaultVideoExtensions, loaded.VideoExtensions);
    }

    [Fact]
    public void SpeechIsOffUntilAskedFor_WithTheModelThatWorks_AndTheLanguageHeardByTheModel()
    {
        var settings = new Settings();

        Assert.False(settings.VideoSpeech);
        Assert.Equal("small", settings.SpeechModel);
        Assert.Equal(Settings.DefaultSpeechModel, settings.SpeechModel);
        Assert.Equal("auto", settings.SpeechLanguage);
    }

    [Fact]
    public void TheSpeechSettings_AreKeptWithTheFolder_AndAnOldFileHasTheDefaults()
    {
        new Settings { CurrentFolder = _root, VideoSpeech = true, SpeechModel = "base", SpeechLanguage = "uk" }.Save(_root);
        var loaded = Settings.Load(_root);

        Assert.True(loaded.VideoSpeech);
        Assert.Equal(("base", "uk"), (loaded.SpeechModel, loaded.SpeechLanguage));

        File.WriteAllText(Settings.GetFilePath(_root), """{ "SupportVideo": true }""");
        var old = Settings.Load(_root);
        Assert.True(old.SupportVideo);
        Assert.False(old.VideoSpeech);
        Assert.Equal("small", old.SpeechModel);
    }

    // ── the cores of the speech recognition ────────────────────────────

    [Fact]
    public void TheCores_AreAllOfThisComputers_ByDefault_AndAnOldFileHasThat()
    {
        Assert.Equal(Settings.MaxCpuCores, new Settings().CpuCores);
        Assert.True(Settings.MaxCpuCores >= 1);

        File.WriteAllText(Settings.GetFilePath(_root), """{ "SupportVideo": true }""");
        Assert.Equal(Settings.MaxCpuCores, Settings.Load(_root).CpuCores);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(100_000, -1)] // a file from a computer with more cores: the most this one has (-1 stands for it)
    public void TheCores_StayBetweenOneAndWhatTheComputerHas(int set, int expected)
    {
        var settings = new Settings { CpuCores = set };

        Assert.Equal(expected == -1 ? Settings.MaxCpuCores : expected, settings.CpuCores);
    }

    [Fact]
    public void TheCores_AreKeptWithTheFolder()
    {
        new Settings { CurrentFolder = _root, CpuCores = 1 }.Save(_root);

        Assert.Equal(1, Settings.Load(_root).CpuCores);
    }

    // ── the progress rows of the settings grid ─────────────────────────

    [Fact]
    public void ThePhotosAndTheVideosOfTheList_AreCountedApart_AndMakeUpTheTotal()
    {
        var settings = new Settings
        {
            Files =
            [
                new PhotoFile(@"C:\a\1.jpg", string.Empty), new PhotoFile(@"C:\a\2.PNG", string.Empty), new PhotoFile(@"C:\a\3.jpg", string.Empty),
                new PhotoFile(@"C:\a\clip.mp4", string.Empty), new PhotoFile(@"C:\a\MVI_1.AVI", string.Empty),
            ],
        };

        Assert.Equal(3, settings.PhotoCount);
        Assert.Equal(2, settings.VideoCount);
        Assert.Equal(settings.TotalFiles, settings.PhotoCount + settings.VideoCount);
    }

    [Fact]
    public void AnEmptyList_HasNoPhotosAndNoVideos() =>
        Assert.Equal((0, 0), (new Settings().PhotoCount, new Settings().VideoCount));

    [Fact]
    public void TheCounts_FollowTheVideoExtensionsOfTheFolder()
    {
        var settings = new Settings { Files = [new PhotoFile(@"C:\a\1.jpg", string.Empty), new PhotoFile(@"C:\a\x.xyz", string.Empty)] };
        Assert.Equal(0, settings.VideoCount);

        MediaKinds.UseVideoExtensions([".xyz"]);

        Assert.Equal((1, 1), (settings.PhotoCount, settings.VideoCount));
    }

    [Fact]
    public void TheCounts_AreMadeAnew_WhenTheListIsReplaced_EvenByOneOfTheSameLength()
    {
        var settings = new Settings { Files = [new PhotoFile(@"C:\a\1.jpg", string.Empty), new PhotoFile(@"C:\a\clip.mp4", string.Empty)] };
        Assert.Equal((1, 1), (settings.PhotoCount, settings.VideoCount)); // read once, so that the count is remembered

        settings.Files = [new PhotoFile(@"C:\b\1.jpg", string.Empty), new PhotoFile(@"C:\b\2.jpg", string.Empty)];

        Assert.Equal((2, 0), (settings.PhotoCount, settings.VideoCount));
    }

    [Fact]
    public void TheCounts_AreMadeAnew_WhenAFileIsTakenOutOfTheListItself()
    {
        var video = new PhotoFile(@"C:\a\clip.mp4", string.Empty);
        var settings = new Settings { Files = [new PhotoFile(@"C:\a\1.jpg", string.Empty), video, new PhotoFile(@"C:\a\2.jpg", string.Empty)] };
        Assert.Equal((2, 1), (settings.PhotoCount, settings.VideoCount));

        settings.Files.Remove(video); // as deleting the current file does

        Assert.Equal((2, 0), (settings.PhotoCount, settings.VideoCount));
        Assert.Equal(settings.TotalFiles, settings.PhotoCount + settings.VideoCount);
    }

    [Fact]
    public void TheCounts_AreNotWrittenToTheSettingsFile()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Settings { Files = [new PhotoFile(@"C:\a\1.jpg", string.Empty)] });

        Assert.DoesNotContain("PhotoCount", json);
        Assert.DoesNotContain("VideoCount", json);
        Assert.Contains("CpuCores", json);
    }

    [Fact]
    public void ASearch_LooksAtBothKinds_UnlessOneIsSwitchedOff()
    {
        var settings = new Settings { SupportVideo = true };
        Assert.Equal((true, true), settings.SearchKinds());

        settings.SearchVideos = false;
        Assert.Equal((true, false), settings.SearchKinds());

        settings.SearchPhotos = false;
        settings.SearchVideos = true;
        Assert.Equal((false, true), settings.SearchKinds());
    }

    [Fact]
    public void BothKindsOff_CountsAsBothOn_SomethingHasToBeSearched()
    {
        var settings = new Settings { SupportVideo = true, SearchPhotos = false, SearchVideos = false };

        Assert.Equal((true, true), settings.SearchKinds());
    }

    [Fact]
    public void WithVideosNotListed_OnlyThePhotosAreSearched_WhateverTheTogglesSay()
    {
        var settings = new Settings { SupportVideo = false, SearchPhotos = false, SearchVideos = true };

        Assert.Equal((true, false), settings.SearchKinds());
    }

    [Fact]
    public void TheKindsOfASearch_AreKeptWithTheFolder_AndAnOldFileSearchesBoth()
    {
        new Settings { CurrentFolder = _root, SupportVideo = true, SearchPhotos = false }.Save(_root);
        var loaded = Settings.Load(_root);
        Assert.Equal((false, true), (loaded.SearchPhotos, loaded.SearchVideos));
        Assert.Equal((false, true), loaded.SearchKinds());

        File.WriteAllText(Settings.GetFilePath(_root), """{ "SupportVideo": true }""");
        Assert.Equal((true, true), Settings.Load(_root).SearchKinds());
    }

    [Fact]
    public void TheListedExtensions_AreNotWrittenToTheFile()
    {
        new Settings { CurrentFolder = _root, SupportVideo = true }.Save(_root);

        Assert.DoesNotContain("ListedExtensions", File.ReadAllText(Settings.GetFilePath(_root)));
    }

    // ── the list of a folder ───────────────────────────────────────────

    [Fact]
    public void TheListing_HoldsVideosOnlyWhenTheyAreOn()
    {
        Touch("a.jpg");
        Touch("clip.mp4");
        Touch(@"sub\MVI_0001.AVI");
        Touch(@"sub\b.png");
        var off = new Settings();
        var on = new Settings { SupportVideo = true };

        var photosOnly = PhotoScanner.Scan(_root, recursive: true, off.ListedExtensions, null).Select(p => Path.GetFileName(p.OriginalFilePath)).Order();
        var withVideos = PhotoScanner.Scan(_root, recursive: true, on.ListedExtensions, null).ToList();

        Assert.Equal(["a.jpg", "b.png"], photosOnly);
        Assert.Equal(["MVI_0001.AVI", "a.jpg", "b.png", "clip.mp4"], withVideos.Select(p => Path.GetFileName(p.OriginalFilePath)).Order(StringComparer.Ordinal));
        Assert.Equal(["MVI_0001.AVI", "clip.mp4"], withVideos.Where(p => p.IsVideo).Select(p => Path.GetFileName(p.OriginalFilePath)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheSavedListing_IsAnotherOne_WhenVideosAreTurnedOn()
    {
        var off = new ListingKey(_root, true, new Settings().ListedExtensions, null);
        var on = new ListingKey(_root, true, new Settings { SupportVideo = true }.ListedExtensions, null);

        Assert.NotEqual(off.CacheFile, on.CacheFile); // the list made without videos is not shown with them on
    }

    [Fact]
    public void ALikedCopyOfAVideo_IsFoundLikeThatOfAPhoto()
    {
        var video = Touch("clip.mp4");
        var photo = new PhotoFile(video, string.Empty) { Length = new FileInfo(video).Length, LastWriteTimeUtc = File.GetLastWriteTimeUtc(video) };
        var liked = Path.Combine(_root, "Liked");
        Directory.CreateDirectory(liked);

        var library = LikedLibrary.Scan(liked);
        var copy = library.GetNewCopyPath(photo);
        LikedLibrary.CopyPhoto(video, copy);

        Assert.Equal(copy, LikedLibrary.Scan(liked).Find(photo));
    }

    // ── the rows of a video in the metadata grid ───────────────────────

    [Fact]
    public void TheRowsOfAVideo_AreInTheirOwnCategory_FirstOfAll()
    {
        var row = new MetadataEntry(MetadataEntry.VideoTagBase + 3, "Video codec", "mjpeg");
        var exif = new MetadataEntry(0x010F, "Camera Make", "Canon");

        var category = ExifTagMap.GetCategory(row);

        Assert.Equal(ImageHelperStrings.CategoryVideo, category.Name);
        Assert.True(category.IsBrowsable);
        Assert.True(string.CompareOrdinal(ImageHelperStrings.CategoryVideo, ExifTagMap.GetCategory(exif).Name) < 0); // sorts before Image, EXIF ...
    }

    [Fact]
    public void ARowThatBringsItsOwnDescription_IsDescribedByIt()
    {
        var row = new MetadataEntry(MetadataEntry.VideoTagBase + 1, "Duration", "0:47", Description: "How long it plays.");

        Assert.Equal("How long it plays.", MetadataDescriptions.For(row));
    }

    [Fact]
    public void ARowWithNoDescription_IsDescribedAsBefore()
    {
        var row = new MetadataEntry(0x010F, "Camera Make", "Canon");

        Assert.False(string.IsNullOrWhiteSpace(MetadataDescriptions.For(row)));
    }
}

[CollectionDefinition("Video extensions", DisableParallelization = true)]
public sealed class VideoExtensionsCollection;
