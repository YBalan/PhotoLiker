namespace PhotoLiker.Search.Tests;

/// <summary>
/// The old Visual C++ runtime that a shell extension (TortoiseGit) loads into a program makes ONNX Runtime crash the whole
/// process. What is decided here is whether the one that would be used is old.
/// </summary>
public class NativeRuntimeTests
{
    private const string Windows = @"C:\WINDOWS\SYSTEM32\MSVCP140.dll";
    private const string Tortoise = @"C:\Program Files\TortoiseGit\bin\MSVCP140.dll";

    private static (string Name, string Path, Version? Version) Module(string name, string path, string? version) =>
        (name, path, version is null ? null : Version.Parse(version));

    [Fact]
    public void AnOldRuntimeThatWasLoadedFirst_IsFound()
    {
        var old = NativeRuntime.FindOldRuntime([Module("MSVCP140.dll", Tortoise, "14.30.30708.0"), Module("MSVCP140.dll", Windows, "14.50.35719.0")]);

        Assert.Equal((Tortoise, new Version(14, 30, 30708, 0)), old);
    }

    [Fact]
    public void TheCurrentRuntimeLoadedFirst_ProtectsFromAnOldOneLoadedLater()
    {
        // Imports are bound by name to the first copy, so the old one that comes after is not the one ONNX Runtime gets
        Assert.Null(NativeRuntime.FindOldRuntime([Module("MSVCP140.dll", Windows, "14.50.35719.0"), Module("MSVCP140.dll", Tortoise, "14.30.30708.0")]));
    }

    [Theory]
    [InlineData("14.29.30133.0", true)]
    [InlineData("14.30.30708.0", true)]
    [InlineData("14.39.33519.0", true)]
    [InlineData("14.40.33810.0", false)]
    [InlineData("14.50.35719.0", false)]
    public void WhichRuntimesAreTooOld(string version, bool tooOld)
    {
        var found = NativeRuntime.FindOldRuntime([Module("MSVCP140.dll", Tortoise, version)]);

        Assert.Equal(tooOld, found is not null);
    }

    [Fact]
    public void NoRuntimeLoaded_OrAVersionThatIsNotKnown_IsNotBlocked()
    {
        Assert.Null(NativeRuntime.FindOldRuntime([]));
        Assert.Null(NativeRuntime.FindOldRuntime([Module("MSVCP140.dll", Tortoise, null)]));
    }

    [Fact]
    public void OtherLibraries_AreNotTheRuntime()
    {
        Assert.Null(NativeRuntime.FindOldRuntime([Module("VCRUNTIME140.dll", Tortoise, "14.30.30708.0"), Module("kernel32.dll", @"C:\Windows\System32\kernel32.dll", "10.0.0.0")]));
    }

    [Fact]
    public void TheNameIsMatchedWithoutCaringAboutCase()
    {
        Assert.NotNull(NativeRuntime.FindOldRuntime([Module("msvcp140.DLL", Tortoise, "14.30.30708.0")]));
    }

    [Fact]
    public void PreferringTheRuntimeOfWindows_CanBeDoneAnyNumberOfTimes()
    {
        NativeRuntime.PreferWindowsRuntime();
        NativeRuntime.PreferWindowsRuntime();
    }
}
