using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class PhotoFileTests
{
    // ── Constructor ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_StoresOriginalAndLikedPaths()
    {
        var pf = new PhotoFile(@"C:\Photos\a.jpg", @"C:\Liked\a.jpg");

        Assert.Equal(@"C:\Photos\a.jpg", pf.OriginalFilePath);
        Assert.Equal(@"C:\Liked\a.jpg", pf.LikedFilePath);
    }

    [Fact]
    public void LikedFilePath_IsMutable()
    {
        var pf = new PhotoFile(@"C:\Photos\a.jpg", string.Empty);

        pf.LikedFilePath = @"C:\Liked\a.jpg";

        Assert.Equal(@"C:\Liked\a.jpg", pf.LikedFilePath);
    }

    [Fact]
    public void ToString_ContainsBothPaths()
    {
        var pf = new PhotoFile(@"C:\Photos\a.jpg", @"C:\Liked\a.jpg");

        var str = pf.ToString();

        Assert.Contains(@"C:\Photos\a.jpg", str);
        Assert.Contains(@"C:\Liked\a.jpg", str);
    }
}

public class PhotoFileListExtensionsTests
{
    // ── IndexOf ─────────────────────────────────────────────────────────────

    [Fact]
    public void IndexOf_ExistingPath_ReturnsCorrectIndex()
    {
        var list = new List<PhotoFile>
        {
            new(@"C:\a.jpg", string.Empty),
            new(@"C:\b.jpg", string.Empty),
            new(@"C:\c.jpg", string.Empty),
        };

        Assert.Equal(0, list.IndexOf(@"C:\a.jpg"));
        Assert.Equal(1, list.IndexOf(@"C:\b.jpg"));
        Assert.Equal(2, list.IndexOf(@"C:\c.jpg"));
    }

    [Fact]
    public void IndexOf_MissingPath_ReturnsMinusOne()
    {
        var list = new List<PhotoFile> { new(@"C:\a.jpg", string.Empty) };

        Assert.Equal(-1, list.IndexOf(@"C:\missing.jpg"));
    }

    [Fact]
    public void IndexOf_EmptyList_ReturnsMinusOne()
    {
        var list = new List<PhotoFile>();

        Assert.Equal(-1, list.IndexOf(@"C:\a.jpg"));
    }

    [Fact]
    public void IndexOf_CaseSensitive_DoesNotMatchDifferentCase()
    {
        var list = new List<PhotoFile> { new(@"C:\Photo.jpg", string.Empty) };

        // Ordinal comparison — must NOT find lowercase variant
        Assert.Equal(-1, list.IndexOf(@"C:\photo.jpg"));
    }
}
