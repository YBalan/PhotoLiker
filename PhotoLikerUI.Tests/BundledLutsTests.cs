using PhotoLiker.Effects.Looks;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The colour lookup tables that come with the program (the installer packs the folder Luts beside it): they are there, every one can be read by the slideshow's own reader, every one does something
/// sensible to a colour, and the file dialog of the LookFile row starts where they are.
/// </summary>
public sealed class BundledLutsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static IReadOnlyList<string> Files() => Directory.Exists(BundledLuts.Folder) ? [.. Directory.GetFiles(BundledLuts.Folder, "*.cube").OrderBy(file => file)] : [];

    // ── the files ───────────────────────────────────────────────────────────

    [Fact]
    public void TheTables_AreBesideTheProgram_Twelve_WithAReadme()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Luts"), BundledLuts.Folder);
        Assert.True(Directory.Exists(BundledLuts.Folder), "the folder Luts is beside the program");
        Assert.Equal(12, Files().Count);
        Assert.True(File.Exists(Path.Combine(BundledLuts.Folder, "README.txt")));
        Assert.Contains("MIT", File.ReadAllText(Path.Combine(BundledLuts.Folder, "README.txt")));
    }

    [Fact]
    public void EveryTable_IsReadByTheSlideshowsReader_AsA33PointCube_WithATitle()
    {
        foreach (var file in Files())
        {
            var lut = CubeLut.Load(file);

            Assert.True(lut.IsCube, file);
            Assert.Equal(33, lut.Size);
            Assert.Equal(Path.GetFileNameWithoutExtension(file), lut.Title);
        }
    }

    [Fact]
    public void EveryTable_KeepsBlackDarkAndWhiteLight_AndMakesGreysRiseSteadily() // a dark look (Cold Night) takes a little off the white: 0.8 is the least
    {
        foreach (var file in Files())
        {
            var lut = CubeLut.Load(file);

            var (r0, g0, b0) = lut.Map(0, 0, 0);
            var (r1, g1, b1) = lut.Map(1, 1, 1);
            Assert.True(r0 <= 0.15f && g0 <= 0.15f && b0 <= 0.15f, $"{Path.GetFileName(file)}: black becomes {r0:0.00} {g0:0.00} {b0:0.00}");
            Assert.True(r1 >= 0.80f && g1 >= 0.80f && b1 >= 0.80f, $"{Path.GetFileName(file)}: white becomes {r1:0.00} {g1:0.00} {b1:0.00}");

            var last = -1f;
            for (var step = 0; step <= 20; step++)
            {
                var t = step / 20f;
                var (r, g, b) = lut.Map(t, t, t);
                var light = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                Assert.True(light >= last - 0.002f, $"{Path.GetFileName(file)}: the greys do not rise at {t:0.00}");
                last = light;
            }
        }
    }

    [Fact]
    public void EveryTable_ChangesColours_NoneIsAnIdentityInDisguise_AndNoTwoAreTheSame()
    {
        var samples = new (float R, float G, float B)[] { (0.8f, 0.4f, 0.2f), (0.2f, 0.5f, 0.8f), (0.3f, 0.7f, 0.3f), (0.9f, 0.85f, 0.7f), (0.15f, 0.15f, 0.2f) };
        var results = new List<(float, float, float)[]>();
        foreach (var file in Files())
        {
            var lut = CubeLut.Load(file);
            var mapped = samples.Select(c => lut.Map(c.R, c.G, c.B)).ToArray();
            var change = mapped.Zip(samples, (m, c) => Math.Abs(m.R - c.R) + Math.Abs(m.G - c.G) + Math.Abs(m.B - c.B)).Max();
            Assert.True(change > 0.05f, $"{Path.GetFileName(file)} changes colours by {change:0.000}");
            results.Add(mapped);
        }

        for (var a = 0; a < results.Count; a++)
        for (var b = a + 1; b < results.Count; b++)
        {
            var apart = results[a].Zip(results[b], (x, y) => Math.Abs(x.Item1 - y.Item1) + Math.Abs(x.Item2 - y.Item2) + Math.Abs(x.Item3 - y.Item3)).Max();
            Assert.True(apart > 0.03f, $"looks {a} and {b} are the same");
        }
    }

    [Fact]
    public void TheBlackAndWhiteTables_HaveNoColour()
    {
        foreach (var file in Files().Where(file => Path.GetFileName(file).StartsWith("Black and White")))
        {
            var lut = CubeLut.Load(file);
            var (r, g, b) = lut.Map(0.8f, 0.3f, 0.1f);

            Assert.True(Math.Abs(r - g) < 0.001f && Math.Abs(g - b) < 0.001f, $"{Path.GetFileName(file)} gave {r:0.000} {g:0.000} {b:0.000}");
        }
    }

    // ── where the dialog starts ─────────────────────────────────────────────

    [Fact]
    public void TheDialog_StartsInTheFolderOfTheFileThatIsChosen_IfItIsStillThere()
    {
        var mine = _folder.File(@"my looks\kodak.cube");
        var bundled = _folder.File(@"app\Luts\a.cube");
        Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!); // the folder of the chosen file is there

        Assert.Equal(Path.GetDirectoryName(mine), BundledLuts.StartFolder(mine, Path.GetDirectoryName(bundled)!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Z:\\no such folder\\anywhere\\x.cube")]   // the folder of the file is gone
    [InlineData("x.cube")]                                // no folder at all
    [InlineData("bad|path?.cube")]                        // not a path
    public void TheDialog_StartsInTheFolderOfTheTablesThatCameWithTheProgram_WhenThereIsNoFileToStartFrom(string? current)
    {
        var bundled = Path.Combine(_folder.Path, "Luts");
        Directory.CreateDirectory(bundled);

        Assert.Equal(bundled, BundledLuts.StartFolder(current, bundled));
    }

    [Fact]
    public void WithNoBundledFolderEither_TheDialogUsesItsOwnIdea()
    {
        Assert.Null(BundledLuts.StartFolder(null, Path.Combine(_folder.Path, "missing")));
        Assert.Null(BundledLuts.StartFolder("Z:\\gone\\x.cube", Path.Combine(_folder.Path, "missing")));
    }
}
