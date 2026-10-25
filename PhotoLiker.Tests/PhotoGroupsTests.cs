using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class PhotoGroupsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PLGroupTests_" + Guid.NewGuid());
    private readonly string _likedDir;

    public PhotoGroupsTests()
    {
        _likedDir = Path.Combine(_tempDir, "Liked");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string MakeLikedSubfolder(string name)
    {
        var path = Path.Combine(_likedDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // ── IsGroupKey ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData('1')]
    [InlineData('9')]
    [InlineData('A')]
    [InlineData('Z')]
    [InlineData('a')]
    [InlineData('z')]
    public void IsGroupKey_DigitsOneToNineAndLetters_ReturnsTrue(char key)
    {
        Assert.True(PhotoGroups.IsGroupKey(key));
    }

    [Theory]
    [InlineData('0')]
    [InlineData(' ')]
    [InlineData('-')]
    [InlineData('\0')]
    [InlineData('é')]
    [InlineData('Ж')]
    [InlineData('ſ')] // long s: upper-cases to 'S'
    [InlineData('ı')] // dotless i: upper-cases to 'I'
    [InlineData('K')] // Kelvin sign: lower-cases to 'k'
    public void IsGroupKey_AnythingElse_ReturnsFalse(char key)
    {
        Assert.False(PhotoGroups.IsGroupKey(key));
    }

    [Theory]
    [InlineData('a', 'A')]
    [InlineData('Q', 'Q')]
    [InlineData('5', '5')]
    public void NormalizeKey_UpperCasesLetters(char key, char expected)
    {
        Assert.Equal(expected, PhotoGroups.NormalizeKey(key));
    }

    [Fact]
    public void NormalizeKey_InvalidKey_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoGroups.NormalizeKey('0'));
    }

    [Fact]
    public void IsGroupKey_CoversExactly35Keys()
    {
        var keys = Enumerable.Range(0, 128).Select(i => (char)i).Where(PhotoGroups.IsGroupKey)
            .Select(char.ToUpperInvariant).Distinct();

        Assert.Equal(35, keys.Count());
    }

    // ── GetFolderName ───────────────────────────────────────────────────────

    [Theory]
    [InlineData('A', "A - Group")]
    [InlineData('a', "A - Group")]
    [InlineData('1', "1 - Group")]
    [InlineData('Z', "Z - Group")]
    public void GetFolderName_ReturnsKeyAndSuffix(char key, string expected)
    {
        Assert.Equal(expected, PhotoGroups.GetFolderName(key));
    }

    [Theory]
    [InlineData('0')]
    [InlineData('-')]
    public void GetFolderName_InvalidKey_Throws(char key)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoGroups.GetFolderName(key));
    }

    // ── TryGetKey ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("A - Group", 'A')]
    [InlineData("3 - Group", '3')]
    [InlineData("a - group", 'A')]
    [InlineData("A - Group Family", 'A')]
    [InlineData("B - Group (2024)", 'B')]
    [InlineData("A - Groups", 'A')]
    public void TryGetKey_NameStartingWithKeyAndSuffix_ReturnsKey(string name, char expected)
    {
        Assert.True(PhotoGroups.TryGetKey(name, out var key));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Group")]
    [InlineData("A")]
    [InlineData("A - Grou")]
    [InlineData("A- Group")]
    [InlineData("A -Group")]
    [InlineData("0 - Group")]
    [InlineData("AB - Group")]
    [InlineData(" A - Group")]
    [InlineData("My A - Group")]
    [InlineData("Liked")]
    public void TryGetKey_OtherNames_ReturnsFalse(string? name)
    {
        Assert.False(PhotoGroups.TryGetKey(name, out _));
    }

    // ── FindFolders / FindFolder ────────────────────────────────────────────

    [Fact]
    public void FindFolders_LikedFolderMissing_ReturnsEmpty()
    {
        Assert.Empty(PhotoGroups.FindFolders(_likedDir));
    }

    [Fact]
    public void FindFolders_BlankLikedFolder_ReturnsEmpty()
    {
        Assert.Empty(PhotoGroups.FindFolders(""));
        Assert.Empty(PhotoGroups.FindFolders("   "));
    }

    [Fact]
    public void FindFolders_ReturnsOnlyGroupFoldersOrderedByKey()
    {
        var b = MakeLikedSubfolder("B - Group");
        var one = MakeLikedSubfolder("1 - Group");
        var a = MakeLikedSubfolder("A - Group Family");
        MakeLikedSubfolder("Holidays");
        MakeLikedSubfolder("0 - Group");
        File.WriteAllText(Path.Combine(_likedDir, "C - Group"), "a file, not a folder");

        var folders = PhotoGroups.FindFolders(_likedDir);

        Assert.Equal(
            [new GroupFolder('1', one), new GroupFolder('A', a), new GroupFolder('B', b)],
            folders);
    }

    [Fact]
    public void FindFolders_IgnoresNestedFolders()
    {
        Directory.CreateDirectory(Path.Combine(_likedDir, "Holidays", "A - Group"));

        Assert.Empty(PhotoGroups.FindFolders(_likedDir));
    }

    [Fact]
    public void FindFolder_NoFolderForKey_ReturnsNull()
    {
        MakeLikedSubfolder("B - Group");

        Assert.Null(PhotoGroups.FindFolder(_likedDir, 'A'));
    }

    [Fact]
    public void FindFolder_InvalidKey_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoGroups.FindFolder(_likedDir, '0'));
    }

    [Fact]
    public void FindFolder_DefaultName_ReturnsIt()
    {
        var a = MakeLikedSubfolder("A - Group");

        Assert.Equal(a, PhotoGroups.FindFolder(_likedDir, 'A'));
    }

    [Fact]
    public void FindFolder_RenamedFolder_ReturnsIt()
    {
        var renamed = MakeLikedSubfolder("A - Group Family");

        Assert.Equal(renamed, PhotoGroups.FindFolder(_likedDir, 'A'));
    }

    [Fact]
    public void FindFolder_LowercaseKey_FindsTheSameFolder()
    {
        var a = MakeLikedSubfolder("A - Group");

        Assert.Equal(a, PhotoGroups.FindFolder(_likedDir, 'a'));
    }

    [Fact]
    public void FindFolder_DefaultNameAndRenamedFolder_PrefersDefaultName()
    {
        MakeLikedSubfolder("A - Group Family");
        var original = MakeLikedSubfolder("A - Group");

        Assert.Equal(original, PhotoGroups.FindFolder(_likedDir, 'A'));
    }

    [Fact]
    public void FindFolder_SeveralRenamedFolders_ReturnsFirstByName()
    {
        MakeLikedSubfolder("A - Group Work");
        var family = MakeLikedSubfolder("A - Group Family");

        Assert.Equal(family, PhotoGroups.FindFolder(_likedDir, 'A'));
    }

    // ── EnsureFolder ────────────────────────────────────────────────────────

    [Fact]
    public void EnsureFolder_NothingExists_CreatesLikedAndGroupFolder()
    {
        var folder = PhotoGroups.EnsureFolder(_likedDir, 'A');

        Assert.Equal(Path.Combine(_likedDir, "A - Group"), folder);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void EnsureFolder_LowercaseKey_CreatesUppercaseName()
    {
        var folder = PhotoGroups.EnsureFolder(_likedDir, 'b');

        Assert.Equal("B - Group", Path.GetFileName(folder));
    }

    [Fact]
    public void EnsureFolder_DigitKey_CreatesDigitFolder()
    {
        var folder = PhotoGroups.EnsureFolder(_likedDir, '7');

        Assert.Equal("7 - Group", Path.GetFileName(folder));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void EnsureFolder_RenamedFolderExists_ReturnsItWithoutCreatingAnother()
    {
        var renamed = MakeLikedSubfolder("A - Group Family");

        var folder = PhotoGroups.EnsureFolder(_likedDir, 'A');

        Assert.Equal(renamed, folder);
        Assert.False(Directory.Exists(Path.Combine(_likedDir, "A - Group")));
    }

    [Fact]
    public void EnsureFolder_CalledTwice_ReturnsSameFolder()
    {
        var first = PhotoGroups.EnsureFolder(_likedDir, 'A');
        var second = PhotoGroups.EnsureFolder(_likedDir, 'A');

        Assert.Equal(first, second);
        Assert.Single(PhotoGroups.FindFolders(_likedDir));
    }

    [Fact]
    public void EnsureFolder_OtherGroupsFolderExists_StillCreatesNewOne()
    {
        MakeLikedSubfolder("B - Group");

        PhotoGroups.EnsureFolder(_likedDir, 'A');

        Assert.Equal(['A', 'B'], PhotoGroups.FindFolders(_likedDir).Select(f => f.Key));
    }

    [Fact]
    public void EnsureFolder_BlankLikedFolder_Throws()
    {
        Assert.Throws<ArgumentException>(() => PhotoGroups.EnsureFolder("", 'A'));
    }

    [Fact]
    public void EnsureFolder_InvalidKey_ThrowsAndCreatesNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoGroups.EnsureFolder(_likedDir, '0'));

        Assert.False(Directory.Exists(_likedDir) && Directory.EnumerateDirectories(_likedDir).Any());
    }
}
