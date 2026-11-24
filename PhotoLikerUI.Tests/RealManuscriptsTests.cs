using System.Security.Cryptography;
using PhotoLiker.Drawing;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.PhotoBook;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The reading and the cutting on photos of real manuscripts, which are the person's own and so are not in the repository: the folder is named by the environment variable
/// <c>PHOTOLIKER_MANUSCRIPTS</c> (for example <c>D:\Manuscripts</c>), and the models are looked for in <c>PHOTOLIKER_MODELS</c> and in the models folder of the program
/// (<c>%LocalAppData%\PhotoLiker\models</c>). A test whose folder or models are not there ends at once and counts as passed, so the machines that build the program, which have neither, are not
/// held up. What is asserted is what must hold for any page, not what a particular page says: every page gives lines, the boxes lie on the page, the sureness is a chance, most of the letters
/// of a Ukrainian manuscript are Cyrillic, and a sheet that is cut out is a sheet (a plausible quadrilateral that is a good part of the photo).
/// </summary>
public sealed class RealManuscriptsTests
{
    private static string? Folder =>
        Environment.GetEnvironmentVariable("PHOTOLIKER_MANUSCRIPTS") is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>The file of a model in the folder named by <c>PHOTOLIKER_MODELS</c>, else in the models folder of the program; null when it is in neither.</summary>
    internal static string? Model(ToolModel model)
    {
        var folders = new[]
        {
            Environment.GetEnvironmentVariable("PHOTOLIKER_MODELS"),
            Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty, "PhotoLiker", "models"),
        };
        return folders.Where(folder => !string.IsNullOrEmpty(folder)).Select(folder => model.PathIn(folder!)).FirstOrDefault(File.Exists);
    }

    /// <summary>The photos of the folder, each different one once (a photo that was sent twice is the same bytes).</summary>
    private static List<(string Path, Bitmap32 Picture)> Photos()
    {
        var seen = new HashSet<string>();
        var photos = new List<(string, Bitmap32)>();
        foreach (var path in Directory.GetFiles(Folder!, "*.jpg").Concat(Directory.GetFiles(Folder!, "*.jpeg")).Order())
        {
            var bytes = File.ReadAllBytes(path);
            if (!seen.Add(Convert.ToHexString(SHA1.HashData(bytes))))
                continue;

            var decoded = WicPhotoDecoder.DecodeFull(bytes);
            photos.Add((path, new Bitmap32(decoded.Width, decoded.Height, decoded.Bgra)));
        }

        return photos;
    }

    [Fact]
    public void EveryPhotoOfTheFolder_IsRead_WithBoxesOnThePage_AndMostOfTheLettersAreCyrillic()
    {
        if (Folder is null || Model(ToolModels.OcrFinder) is not { } finder || Model(ToolModels.HandwritingUkrainian) is not { } checkpoint)
            return;

        using var scanner = ManuscriptScanner.Handwriting(finder, checkpoint, ScanLanguage.Ukrainian);
        var photos = Photos();
        Assert.NotEmpty(photos);

        double letters = 0, cyrillic = 0;
        foreach (var (path, picture) in photos)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var result = scanner.Scan(picture);

            Assert.True(started.Elapsed < TimeSpan.FromSeconds(60), $"{Path.GetFileName(path)} took {started.Elapsed}");
            Assert.False(result.IsEmpty, $"nothing was read on {Path.GetFileName(path)}");
            foreach (var line in result.Lines)
            {
                Assert.InRange(line.Confidence, 0f, 1f);
                Assert.False(string.IsNullOrWhiteSpace(line.Text));
                foreach (var corner in line.Box.Corners)
                {
                    // A line that goes a little past the edge of the page (its box is made larger than its strip) is fine; one that is far away is a bug
                    Assert.InRange(corner.X, -0.15f * picture.Width, 1.15f * picture.Width);
                    Assert.InRange(corner.Y, -0.15f * picture.Height, 1.15f * picture.Height);
                }

                foreach (var c in line.Text.Where(char.IsLetter))
                {
                    letters++;
                    if (c is >= '\u0400' and <= '\u04FF')
                        cyrillic++;
                }
            }
        }

        Assert.True(letters > 0);
        Assert.True(cyrillic / letters >= 0.5, $"{cyrillic} of {letters} letters are Cyrillic");
    }

    [Fact]
    public void TheLinesOfEveryPhoto_AreLaidOutInRows_WhoseLinesAreSideBySide_AndNoLineIsLost()
    {
        if (Folder is null || Model(ToolModels.OcrFinder) is not { } finder || Model(ToolModels.HandwritingUkrainian) is not { } checkpoint)
            return;

        using var scanner = ManuscriptScanner.Handwriting(finder, checkpoint, ScanLanguage.Multilingual);
        foreach (var (name, page) in Photos())
        {
            var lines = scanner.Scan(page).Lines;
            var rows = ManuscriptLayout.Rows([.. lines], page.Width);

            Assert.Equal(lines.Count, rows.Sum(row => row.Lines.Count)); // no line is lost, none is in two rows
            Assert.True(rows.Count <= lines.Count, name);
            foreach (var row in rows)
            {
                Assert.True(row.Indent >= 0 && row.Indent <= RichDocument.ReferenceWidth, $"{name}: indent {row.Indent}");
                for (var k = 1; k < row.Lines.Count; k++)
                {
                    // Side by side: the next line of a row is to the right of the one before it and over another part of the page (lines one under the other are rows)
                    System.Drawing.Rectangle a = lines[row.Lines[k - 1]].Box.Bounds, b = lines[row.Lines[k]].Box.Bounds;
                    Assert.True(Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) <= 0.1 * Math.Min(a.Width, b.Width), $"{name}: two lines of a row are over each other");
                }

                Assert.Equal(row.Lines.Count - 1, row.Text.Count(c => c is '\t' or ' ') - row.Lines.Sum(i => lines[i].Text.Count(c => c == ' ')));
            }
        }
    }

    [Fact]
    public void English_IsReadWithNoCyrillicAtAll_OnEveryPhotoOfTheFolder()
    {
        if (Folder is null || Model(ToolModels.OcrFinder) is not { } finder || Model(ToolModels.HandwritingUkrainian) is not { } checkpoint)
            return;

        using var scanner = ManuscriptScanner.Handwriting(finder, checkpoint, ScanLanguage.English);
        foreach (var (name, page) in Photos().Take(4))
        {
            var text = string.Concat(scanner.Scan(page).Lines.Select(line => line.Text));

            Assert.DoesNotContain(text, c => c is >= 'Ѐ' and <= 'ӿ');
        }
    }

    [Fact]
    public void ThePicturesFoundOnThePhotosOfTheFolder_AreSolidShapesOnThePage_AndCanBeCutOutAlongTheirOutlines()
    {
        if (Folder is null || Model(ToolModels.Cutout) is not { } cutout || Model(ToolModels.OcrFinder) is not { } finder)
            return;

        using var pictures = new PictureFinder(cutout, finder);
        foreach (var (path, picture) in Photos())
        {
            var name = Path.GetFileName(path);
            var found = pictures.Find(picture);
            Assert.True(found.Count <= PictureFinder.MaximumPictures, $"{name}: {found.Count} pictures");
            foreach (var one in found)
            {
                var bounds = one.Bounds;
                Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= picture.Width + 1 && bounds.Bottom <= picture.Height + 1, $"{name}: a picture outside the page");
                Assert.True(bounds.Width * bounds.Height >= PictureFinder.MinimumShare * picture.Width * picture.Height, $"{name}: a picture that is a speck");

                // It cuts out, and what is cut out has both a part that is the photo and a part that is see-through
                var cut = PictureCutter.Cut(picture, one.Outline);
                Assert.NotNull(cut);
                var opaque = 0;
                for (var i = 3; i < cut!.Bgra.Length; i += 4)
                {
                    if (cut.Bgra[i] == 255)
                        opaque++;
                }

                Assert.True(opaque > cut.Width * cut.Height / 4, $"{name}: the picture cut out is mostly empty");
            }
        }
    }

    [Fact]
    public void EveryPhotoOfTheFolder_IsEitherCutOutAsASheet_OrLeftAsItIs()
    {
        if (Folder is null || Model(ToolModels.Cutout) is not { } model)
            return;

        using var cutter = new PageCutter(model);
        foreach (var (path, picture) in Photos())
        {
            var cut = cutter.Find(picture);

            if (cut.Page is null)
            {
                Assert.Null(cut.Quad);
                continue;
            }

            var quad = cut.Quad!.Value;
            var name = Path.GetFileName(path);
            Assert.InRange(cut.Page.Width, 100, 3600);
            Assert.InRange(cut.Page.Height, 100, 3600);
            Assert.True(quad.Area >= 0.2f * picture.Width * picture.Height, $"{name}: the sheet is {quad.Area / (picture.Width * picture.Height):P0} of the photo");
            Assert.True(PageOutline.IsPlausible(quad), $"{name}: the corners are not those of a sheet");
        }
    }
}
