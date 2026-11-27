using Microsoft.Win32;
using PhotoLiker.Core;
using PhotoLiker.VectorFiles;
using PhotoLiker.VectorFiles.Tests;

namespace PhotoLikerUI.Tests;

/// <summary>What the main window rests on for a 3D model, without opening a window: the rows of the grid, the picture and the thumbnail, the start from a file, the slideshow's list.</summary>
public sealed class ModelBasicsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string MakeStl(string name, float[]? triangles = null, string header = "bracket")
    {
        var path = _folder.File(name);
        File.WriteAllBytes(path, TestMeshes.BinaryFile(triangles ?? TestMeshes.Box(0, 0, 0, 40, 20, 10), header));
        return path;
    }

    // ── the rows of the grid ───────────────────────────────────────────

    [Fact]
    public void TheRows_SayTheFactsOfTheModel_InTheModelCategory()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, 40, 20, 10), "bracket");

        var rows = ModelMetadata.Entries(mesh).ToDictionary(row => row.Name, row => row.Value);

        Assert.Equal("12", rows[ModelMetadata.TrianglesName]);
        Assert.Equal("40 × 20 × 10 mm", rows[ModelMetadata.SizeName]);
        Assert.Equal("8 cm³ (8,000 mm³)", rows[ModelMetadata.VolumeName]);
        Assert.Equal("28 cm² (2,800 mm²)", rows[ModelMetadata.AreaName]);
        Assert.Equal(ModelStrings.FormatBinary, rows[ModelMetadata.FormatName]);
        Assert.Equal("bracket", rows[ModelMetadata.ModelNameName]);
        Assert.DoesNotContain(ModelMetadata.SkippedName, rows.Keys);
        Assert.DoesNotContain(ModelMetadata.FileSizeName, rows.Keys); // no file was given
    }

    [Fact]
    public void EveryRow_HasADescription_AndIsInTheModelCategory()
    {
        var path = MakeStl("a.stl");
        var mesh = StlReader.Read(path);

        var rows = ModelMetadata.Entries(mesh, new FileInfo(path));

        Assert.Contains(rows, row => row.Name == ModelMetadata.FileSizeName);
        Assert.Contains(rows, row => row.Name == ModelMetadata.FileDatesName);
        Assert.All(rows, row =>
        {
            Assert.True(row.TagId >= MetadataEntry.ModelTagBase);
            Assert.False(string.IsNullOrWhiteSpace(MetadataDescriptions.For(row)), row.Name);
            Assert.Equal(ImageHelperStrings.CategoryModel, ExifTagMap.GetCategory(row).Name);
        });
        Assert.Equal(rows.Count, rows.Select(row => row.TagId).Distinct().Count());
    }

    [Fact]
    public void ATextFile_IsSaidToBeText_AndTrianglesLeftOutAreCounted()
    {
        var text = StlReader.Read(System.Text.Encoding.ASCII.GetBytes(TestMeshes.AsciiFile(TestMeshes.Box(0, 0, 0, 1, 1, 1), "")));
        var broken = StlReader.Read(TestMeshes.BinaryFile(TestMeshes.Join(TestMeshes.Box(0, 0, 0, 1, 1, 1), TestMeshes.Triangle(float.NaN, 0, 0, 1, 1, 1, 2, 2, 2))));

        Assert.Equal(ModelStrings.FormatText, ModelMetadata.Entries(text).Single(row => row.Name == ModelMetadata.FormatName).Value);
        Assert.Equal("1", ModelMetadata.Entries(broken).Single(row => row.Name == ModelMetadata.SkippedName).Value);
    }

    [Theory]
    [InlineData(0.5, "0.5 mm³")]
    [InlineData(999, "999 mm³")]
    [InlineData(1000, "1 cm³ (1,000 mm³)")]
    [InlineData(12_345.678, "12.35 cm³ (12,346 mm³)")]
    public void TheVolume_IsInCubicCentimetresFromAThousandCubicMillimetres(double cubicMillimetres, string text) =>
        Assert.Equal(text, ModelMetadata.VolumeText(cubicMillimetres));

    [Theory]
    [InlineData(2.5, "2.5 mm²")]
    [InlineData(100, "1 cm² (100 mm²)")]
    [InlineData(2800, "28 cm² (2,800 mm²)")]
    public void TheArea_IsInSquareCentimetresFromAHundredSquareMillimetres(double squareMillimetres, string text) =>
        Assert.Equal(text, ModelMetadata.AreaText(squareMillimetres));

    [Fact]
    public void TheSize_HasNoDecimalsThatAreZeros_AndAtMostTwo() =>
        Assert.Equal("12.5 × 0.07 × 30 mm", ModelMetadata.SizeText(new MeshBounds(0, 0, 0, 12.5f, 0.07f, 30)));

    [Fact]
    public void TheNumbers_AreWrittenTheSameInEveryLanguage()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("uk-UA");
            var rows = ModelMetadata.Entries(TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, 40.5f, 20, 10))).ToDictionary(row => row.Name, row => row.Value);

            Assert.Equal("40.5 × 20 × 10 mm", rows[ModelMetadata.SizeName]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    // ── the picture of a model ─────────────────────────────────────────

    [Fact]
    public void ALoadedModel_HasItsTrianglesAndAPictureAndAThumbnail()
    {
        var path = MakeStl("part.stl");

        using var entry = ImageHelper.LoadPhoto(path, thumbSize: 110);

        Assert.NotNull(entry.Model);
        Assert.Equal(12, entry.Model!.TriangleCount);
        Assert.Equal(800, entry.Full.Width);
        Assert.Equal(600, entry.Full.Height);
        Assert.Equal(110, entry.Thumb!.Width);
        Assert.Contains(entry.Metadata, row => row.Name == ModelMetadata.TrianglesName);
        Assert.Equal(800L * 600 * 4 + entry.Model.Bytes, entry.Bytes); // what the cache counts: the picture and the triangles

        var background = ModelViewControllerTests.Opaque(ImageHelper.ModelStyle.Background);
        var bitmap = (Bitmap)entry.Full;
        Assert.Equal(background, bitmap.GetPixel(2, 2).ToArgb());       // the stage in the corner
        Assert.NotEqual(background, bitmap.GetPixel(400, 300).ToArgb()); // the model in the middle
    }

    [Fact]
    public void ThePhotoOfAPhoto_HasNoModel()
    {
        using var bitmap = new Bitmap(40, 30);
        var path = _folder.File("p.png");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        using var entry = ImageHelper.LoadPhoto(path, 64);

        Assert.Null(entry.Model);
    }

    [Fact]
    public void TheThumbnailAlone_IsTheSizeOfTheStrip_AndShowsTheModel()
    {
        var path = MakeStl("part.stl");

        using var thumbnail = ImageHelper.LoadThumbnail(path, 96);

        Assert.Equal(96, thumbnail.Width);
        Assert.Equal(96, thumbnail.Height);
        Assert.NotEqual(ModelViewControllerTests.Opaque(ImageHelper.ModelStyle.Background), thumbnail.GetPixel(48, 48).ToArgb());
    }

    [Fact]
    public void AFileThatIsNoModel_FailsToLoad_WithAnError_NotAnEmptyPicture()
    {
        var path = _folder.File("fake.stl");
        File.WriteAllText(path, "this is not an STL file at all, only a sentence of some length");

        Assert.Throws<InvalidDataException>(() => ImageHelper.LoadPhoto(path, 64));
        Assert.Throws<InvalidDataException>(() => ImageHelper.LoadThumbnail(path, 64));
    }

    // ── the start from a file ──────────────────────────────────────────

    [Fact]
    public void AFile_OnTheCommandLine_IsTheFolderItIsIn_AtThatFile()
    {
        var path = MakeStl("part.stl");

        var (folder, file) = StartTarget.Of([path]);

        Assert.Equal(_folder.Path, folder);
        Assert.Equal(path, file);
    }

    [Fact]
    public void AFolder_OnTheCommandLine_IsOpenedAsItIs()
    {
        var (folder, file) = StartTarget.Of([_folder.Path]);

        Assert.Equal(_folder.Path, folder);
        Assert.Null(file);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\this\is\not\there.stl")]
    public void NothingThatIsThere_IsNoTarget(string? argument)
    {
        var (folder, file) = StartTarget.Of(argument is null ? [] : [argument]);

        Assert.Null(folder);
        Assert.Null(file);
    }

    [Fact]
    public void AFileNamedWithADotsAndASlash_IsMadeFull()
    {
        var path = MakeStl("part.stl");

        var (folder, file) = StartTarget.Of([Path.Combine(_folder.Path, ".", "part.stl")]);

        Assert.Equal(path, file);
        Assert.Equal(_folder.Path, folder);
    }

    // ── the slideshow ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSlideshow_NeverHasAModel(bool withVideo)
    {
        MediaKinds.UseVideoExtensions(null);
        MediaKinds.UseModelExtensions(null);
        PhotoFile[] list = [new(@"C:\p\a.jpg", ""), new(@"C:\p\b.stl", ""), new(@"C:\p\c.mp4", ""), new(@"C:\p\d.png", "")];

        var shown = MainForm.SlideshowFiles(list, withVideo).Select(file => Path.GetFileName(file.OriginalFilePath));

        string[] expected = withVideo ? ["a.jpg", "c.mp4", "d.png"] : ["a.jpg", "d.png"];
        Assert.Equal(expected, shown);
    }
}

/// <summary>Photo Liker in Windows' list of programs for .stl files: what is written to the registry (to a place of the test's own, never to the real one), and what takes it out again.</summary>
public sealed class StlAssociationTests : IDisposable
{
    private const string Exe = @"C:\Programs\Photo Liker\PhotoLikerUI.exe";
    private readonly string _root = @"Software\PhotoLikerAssociationTests_" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    private StlAssociation Make() => new(Exe, _root);

    private static string? Value(string key, string? name = null)
    {
        using var opened = Registry.CurrentUser.OpenSubKey(key);
        return opened?.GetValue(name) as string;
    }

    [Fact]
    public void Nothing_IsRegisteredAtFirst()
    {
        Assert.False(Make().IsRegistered());
        Assert.False(Make().IsDefault());
    }

    [Fact]
    public void Registering_WritesWhatWindowsReads()
    {
        Make().Register("STL 3D model", "Looks at things");

        Assert.True(Make().IsRegistered());
        var progId = $@"{_root}\Classes\PhotoLiker.StlFile";
        Assert.Equal("STL 3D model", Value(progId));
        Assert.Equal($"\"{Exe}\" \"%1\"", Value(progId + @"\shell\open\command")); // the file is the first argument, in quotes: a path may have spaces
        Assert.Equal($"\"{Exe}\",0", Value(progId + @"\DefaultIcon"));

        using (var openWith = Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\.stl\OpenWithProgids"))
            Assert.Contains("PhotoLiker.StlFile", openWith!.GetValueNames());

        var capabilities = $@"{_root}\PhotoLiker\Capabilities";
        Assert.Equal("Photo Liker", Value(capabilities, "ApplicationName"));
        Assert.Equal("Looks at things", Value(capabilities, "ApplicationDescription"));
        Assert.Equal("PhotoLiker.StlFile", Value(capabilities + @"\FileAssociations", ".stl"));
        Assert.Equal($@"{_root}\PhotoLiker\Capabilities", Value($@"{_root}\RegisteredApplications", "PhotoLiker")); // what Settings, Default apps finds the program by

        var byName = $@"{_root}\Classes\Applications\PhotoLikerUI.exe";
        Assert.Equal($"\"{Exe}\" \"%1\"", Value(byName + @"\shell\open\command"));
        using (var supported = Registry.CurrentUser.OpenSubKey(byName + @"\SupportedTypes"))
            Assert.Contains(".stl", supported!.GetValueNames());
    }

    [Fact]
    public void Registering_Twice_IsTheSameAsOnce_AndTakesTheNewName()
    {
        Make().Register("old name", "old");
        Make().Register("new name", "new");

        Assert.Equal("new name", Value($@"{_root}\Classes\PhotoLiker.StlFile"));
        using var openWith = Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\.stl\OpenWithProgids");
        Assert.Single(openWith!.GetValueNames());
    }

    [Fact]
    public void Unregistering_TakesEverythingAway_AndLeavesWhatWasThereBefore()
    {
        using (var other = Registry.CurrentUser.CreateSubKey($@"{_root}\Classes\.stl\OpenWithProgids"))
            other.SetValue("SomeOtherProgram.stl", string.Empty);
        using (var apps = Registry.CurrentUser.CreateSubKey($@"{_root}\RegisteredApplications"))
            apps.SetValue("AnotherApp", @"Software\Another\Capabilities");

        Make().Register("STL 3D model", "x");
        Make().Unregister();

        Assert.False(Make().IsRegistered());
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\PhotoLiker.StlFile"));
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\Applications\PhotoLikerUI.exe"));
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_root}\PhotoLiker"));
        using (var openWith = Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\.stl\OpenWithProgids"))
            Assert.Equal(["SomeOtherProgram.stl"], openWith!.GetValueNames());
        using (var apps = Registry.CurrentUser.OpenSubKey($@"{_root}\RegisteredApplications"))
            Assert.Equal(["AnotherApp"], apps!.GetValueNames());
    }

    [Fact]
    public void Unregistering_WhatIsNotRegistered_DoesNothingAndDoesNotFail() => Make().Unregister();

    [Fact]
    public void ItIsTheDefault_WhenWindowsChoiceIsPhotoLiker()
    {
        Make().Register("STL 3D model", "x");
        var choice = $@"{_root}\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.stl\UserChoice";

        Assert.False(Make().IsDefault());

        using (var key = Registry.CurrentUser.CreateSubKey(choice))
            key.SetValue("ProgId", "photoliker.STLFILE"); // Windows does not care for the case
        Assert.True(Make().IsDefault());

        using (var key = Registry.CurrentUser.CreateSubKey(choice))
            key.SetValue("ProgId", "Applications\\other.exe");
        Assert.False(Make().IsDefault());
    }

    [Fact]
    public void ItIsNotRegistered_WithOnlyHalfOfWhatItNeeds()
    {
        Make().Register("STL 3D model", "x");
        using (var openWith = Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\.stl\OpenWithProgids", writable: true))
            openWith!.DeleteValue("PhotoLiker.StlFile");

        Assert.False(Make().IsRegistered());
    }
}
