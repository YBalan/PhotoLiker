using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>3D models (STL files) in the list of a folder: which files are models, the setting that lists them, the count of them, and that a photo tool can tell a picture from them.</summary>
[Collection("Video extensions")] // MediaKinds is one list for the process
public class ModelSupportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLModelSupportTests_" + Guid.NewGuid());

    public ModelSupportTests()
    {
        Directory.CreateDirectory(_root);
        MediaKinds.UseVideoExtensions(null);
        MediaKinds.UseModelExtensions(null);
    }

    public void Dispose()
    {
        MediaKinds.UseVideoExtensions(null);
        MediaKinds.UseModelExtensions(null);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    // ── which files are models ─────────────────────────────────────────

    [Theory]
    [InlineData("bracket.stl", true)]
    [InlineData("BRACKET.STL", true)]
    [InlineData(@"C:\prints\a b\gear v2.Stl", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("clip.mp4", false)]
    [InlineData("model.stl.zip", false)]
    [InlineData("stl", false)]          // a name, not an extension
    [InlineData("notes.step", false)]   // another 3D format: not read
    [InlineData("", false)]
    public void TheExtension_TellsAModel(string path, bool model) => Assert.Equal(model, MediaKinds.IsModel(path));

    [Fact]
    public void ANullPath_IsNoModel() => Assert.False(MediaKinds.IsModel((string?)null));

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.PNG", true)]
    [InlineData("clip.mp4", false)]
    [InlineData("bracket.stl", false)]
    public void ANeitherVideoNorModel_IsAnImage(string path, bool image) => Assert.Equal(image, MediaKinds.IsImage(path));

    [Fact]
    public void WithVideosAndModelsOff_EverythingIsAnImage()
    {
        MediaKinds.UseVideoExtensions([]);
        MediaKinds.UseModelExtensions([]);

        Assert.True(MediaKinds.IsImage("clip.mp4")); // by the lists that are on: what is not named a video or a model is a picture
        Assert.True(MediaKinds.IsImage("bracket.stl"));
    }

    [Fact]
    public void TheListOfExtensions_CanBeChanged_WhateverTheCaseAndTheDot()
    {
        MediaKinds.UseModelExtensions([".XYZ", "abc", " .Def "]);

        Assert.True(MediaKinds.IsModel("a.xyz"));
        Assert.True(MediaKinds.IsModel("a.ABC"));
        Assert.True(MediaKinds.IsModel("a.def"));
        Assert.False(MediaKinds.IsModel("a.stl")); // not in the list any more
    }

    [Fact]
    public void NothingGiven_IsTheDefaultList()
    {
        MediaKinds.UseModelExtensions([".xyz"]);
        MediaKinds.UseModelExtensions(null);

        Assert.True(MediaKinds.IsModel("a.stl"));
        Assert.False(MediaKinds.IsModel("a.xyz"));
    }

    [Fact]
    public void TheModelsAndTheVideos_AreTwoLists_ThatDoNotStandInEachOthersWay()
    {
        MediaKinds.UseVideoExtensions([".xyz"]);
        Assert.True(MediaKinds.IsModel("a.stl"));
        Assert.False(MediaKinds.IsVideo("a.stl"));

        MediaKinds.UseModelExtensions([]);
        Assert.True(MediaKinds.IsVideo("a.xyz"));
        Assert.False(MediaKinds.IsModel("a.stl"));
    }

    [Fact]
    public void APhotoFile_KnowsWhatItIs()
    {
        Assert.True(new PhotoFile(@"C:\p\gear.STL", string.Empty).IsModel);
        Assert.False(new PhotoFile(@"C:\p\gear.STL", string.Empty).IsVideo);
        Assert.False(new PhotoFile(@"C:\p\gear.STL", string.Empty).IsImage);
        Assert.True(new PhotoFile(@"C:\p\IMG_1.JPG", string.Empty).IsImage);
        Assert.False(new PhotoFile(@"C:\p\IMG_1.JPG", string.Empty).IsModel);
        Assert.False(new PhotoFile(@"C:\p\MVI_1.AVI", string.Empty).IsImage);
    }

    [Fact]
    public void TheDefaultList_IsStl_AndIsNotAPhotoExtension()
    {
        Assert.Equal([".stl"], MediaKinds.DefaultModelExtensions);
        Assert.DoesNotContain(MediaKinds.DefaultModelExtensions, e => new Settings().Extensions.Contains(e));
        Assert.DoesNotContain(MediaKinds.DefaultModelExtensions, e => MediaKinds.DefaultVideoExtensions.Contains(e));
    }

    // ── the setting ────────────────────────────────────────────────────

    [Fact]
    public void Models_AreOnByDefault_AndLeftOutWhenTurnedOff()
    {
        var settings = new Settings { SupportVideo = false };

        Assert.True(settings.SupportModels);
        Assert.Contains(".stl", settings.ListedExtensions);

        settings.SupportModels = false;
        Assert.Equal(settings.Extensions, settings.ListedExtensions);
        Assert.DoesNotContain(".stl", settings.ListedExtensions);
    }

    [Fact]
    public void TheListedExtensions_AreThePhotos_ThenTheVideosIfOn_ThenTheModelsIfOn()
    {
        var settings = new Settings { VideoExtensions = [".avi"], ModelExtensions = [".stl", ".obj"] };

        Assert.Equal([.. settings.Extensions, ".avi", ".stl", ".obj"], settings.ListedExtensions);

        settings.SupportVideo = false;
        Assert.Equal([.. settings.Extensions, ".stl", ".obj"], settings.ListedExtensions);

        settings.SupportVideo = true;
        settings.SupportModels = false;
        Assert.Equal([.. settings.Extensions, ".avi"], settings.ListedExtensions);

        Assert.DoesNotContain(".stl", settings.Extensions); // indexing and the tools keep working on the photos only
    }

    [Fact]
    public void TheSetting_IsKeptWithTheFolder()
    {
        new Settings { CurrentFolder = _root, SupportModels = false, ModelExtensions = [".stl", ".xyz"] }.Save(_root);

        var loaded = Settings.Load(_root);

        Assert.False(loaded.SupportModels);
        Assert.Equal([".stl", ".xyz"], loaded.ModelExtensions);
    }

    [Fact]
    public void AFolderSavedBeforeModels_HasThemOn_WithTheDefaultList()
    {
        File.WriteAllText(Settings.GetFilePath(_root), """{ "CacheSize": 33, "SupportVideo": false, "Extensions": [".jpg"] }""");

        var loaded = Settings.Load(_root);

        Assert.Equal(33, loaded.CacheSize);
        Assert.True(loaded.SupportModels); // the file does not say: the default
        Assert.Equal(MediaKinds.DefaultModelExtensions, loaded.ModelExtensions);
        Assert.Equal([".jpg", ".stl"], loaded.ListedExtensions);
    }

    [Fact]
    public void AFolderThatHasModelsTurnedOff_KeepsThemOff()
    {
        new Settings { CurrentFolder = _root, SupportModels = false }.Save(_root);

        Assert.False(Settings.Load(_root).SupportModels);
    }

    // ── the count ──────────────────────────────────────────────────────

    [Fact]
    public void ThePhotosTheVideosAndTheModels_AreCountedApart_AndMakeUpTheTotal()
    {
        var settings = new Settings
        {
            Files = [new(@"C:\a\1.jpg", ""), new(@"C:\a\2.stl", ""), new(@"C:\a\3.mp4", ""), new(@"C:\a\4.png", ""), new(@"C:\a\5.STL", "")],
        };

        Assert.Equal(5, settings.TotalFiles);
        Assert.Equal(2, settings.PhotoCount);
        Assert.Equal(1, settings.VideoCount);
        Assert.Equal(2, settings.ModelCount);
        Assert.Equal(settings.TotalFiles, settings.PhotoCount + settings.VideoCount + settings.ModelCount);
    }

    [Fact]
    public void TheCounts_FollowTheListThatReplacesThePrevious()
    {
        var settings = new Settings { Files = [new(@"C:\a\1.stl", "")] };
        Assert.Equal(1, settings.ModelCount);

        settings.Files = [new(@"C:\a\1.jpg", ""), new(@"C:\a\2.jpg", "")];

        Assert.Equal(0, settings.ModelCount);
        Assert.Equal(2, settings.PhotoCount);
    }

    [Fact]
    public void TheCounts_FollowWhatCountsAsAModel()
    {
        var settings = new Settings { Files = [new(@"C:\a\1.stl", ""), new(@"C:\a\2.xyz", "")] };
        Assert.Equal(1, settings.ModelCount);

        MediaKinds.UseModelExtensions([".xyz"]);

        Assert.Equal(1, settings.ModelCount);
        Assert.Equal(1, settings.PhotoCount); // the .stl is a photo to the lists now: the count was made again
    }

    // ── the listing ────────────────────────────────────────────────────

    [Fact]
    public void AFolderScan_ListsTheModelsWithThePhotos_WhenTheyAreOn_AndNotWhenTheyAreOff()
    {
        Touch("a.jpg");
        Touch("sub/gear.stl");
        Touch("sub/gear.STL.txt");
        var settings = new Settings();

        var listed = PhotoScanner.Scan(_root, recursive: true, settings.ListedExtensions, excludedFolder: null);
        Assert.Equal(["a.jpg", "gear.stl"], listed.Select(file => Path.GetFileName(file.OriginalFilePath)).Order());

        settings.SupportModels = false;
        var photosOnly = PhotoScanner.Scan(_root, recursive: true, settings.ListedExtensions, excludedFolder: null);
        Assert.Equal(["a.jpg"], photosOnly.Select(file => Path.GetFileName(file.OriginalFilePath)));
    }

    [Fact]
    public void TheSavedListOfAFolder_IsAnotherOne_WhenTheModelsAreTurnedOnOrOff()
    {
        var on = new ListingKey(_root, true, new Settings().ListedExtensions, null);
        var off = new ListingKey(_root, true, new Settings { SupportModels = false }.ListedExtensions, null);

        Assert.NotEqual(on.CacheFile, off.CacheFile); // a list saved without the models is not shown as if it had them
    }

    [Fact]
    public void TheModelCategory_IsItsOwn_AboveTheVideoOne()
    {
        var category = ExifTagMap.GetCategory(MetadataEntry.ModelTagBase + 3);

        Assert.Equal(ImageHelperStrings.CategoryModel, category.Name);
        Assert.True(category.IsBrowsable);
        Assert.Equal(ImageHelperStrings.CategoryVideo, ExifTagMap.GetCategory(MetadataEntry.VideoTagBase + 3).Name);
        Assert.True(MetadataEntry.ModelTagBase > MetadataEntry.VideoTagBase + 0xFFFF); // the video rows never reach the models' ids
    }
}
