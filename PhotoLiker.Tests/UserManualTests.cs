using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public sealed class UserManualTests : IDisposable
{
    private readonly string _programFolder = Path.Combine(Path.GetTempPath(), "PhotoLikerManual_" + Guid.NewGuid().ToString("N"));

    public UserManualTests() => Directory.CreateDirectory(Path.Combine(_programFolder, UserManual.FolderName));

    public void Dispose() => Directory.Delete(_programFolder, recursive: true);

    [Theory]
    [InlineData(UserManual.English)]
    [InlineData(UserManual.Ukrainian)]
    public void Find_ThePageIsInTheManualFolder_ReturnsItsPath(string language)
    {
        var page = Path.Combine(_programFolder, UserManual.FolderName, $"manual.{language}.html");
        File.WriteAllText(page, "<html></html>");

        Assert.Equal(page, UserManual.Find(_programFolder, language));
    }

    [Fact]
    public void Find_OnlyOneLanguageIsThere_TheOtherIsNull()
    {
        File.WriteAllText(Path.Combine(_programFolder, UserManual.FolderName, "manual.en.html"), "<html></html>");

        Assert.NotNull(UserManual.Find(_programFolder, UserManual.English));
        Assert.Null(UserManual.Find(_programFolder, UserManual.Ukrainian));
    }

    [Fact]
    public void Find_NoManualFolder_IsNull()
    {
        Assert.Null(UserManual.Find(Path.Combine(_programFolder, "nowhere"), UserManual.English));
    }
}
