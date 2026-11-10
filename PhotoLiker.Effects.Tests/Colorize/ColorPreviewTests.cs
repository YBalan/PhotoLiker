using System.Drawing;
using System.Drawing.Imaging;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Tests.Colorize;

/// <summary>A photo colorized while it is looked at: the model runs once, then the colours change and the copy is saved without it. Runs where the colorizing model is (see <see cref="RealModels.Folder"/>).</summary>
public sealed class ColorPreviewTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static bool ModelPresent => ToolModels.Colorizer.IsPresentIn(RealModels.Folder);

    /// <summary>A grey JPEG with shapes in it: a bright disc on a graded ground.</summary>
    private string GreyJpeg(string name, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = (x - width / 2.0) / (width * 0.25);
            var dy = (y - height / 2.0) / (height * 0.35);
            var light = dx * dx + dy * dy < 1 ? 200 - 60 * (dx * dx + dy * dy) : 70 + 80.0 * y / height;
            var level = (int)Math.Clamp(light + 10 * Math.Sin(x / 5.0), 0, 255);
            bitmap.SetPixel(x, y, Color.FromArgb(level, level, level));
        }

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static float MeanChroma(DecodedPhoto photo) => BlackAndWhite.Measure(photo).MeanChroma;

    [Fact]
    public void TheJob_WithAWarmth_MakesAWarmerCopy()
    {
        if (!ModelPresent) return;
        var path = GreyJpeg("old.jpg", 240, 160);
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(4);

        var plain = ColorizeJob.Colorize(colorizer, path, ColorTuning.AsGiven);
        var warm = ColorizeJob.Colorize(colorizer, path, new ColorTuning(Saturation: 0f, Warmth: 1f));

        float MeanB(string file)
        {
            var photo = WicPhotoDecoder.Decode(File.ReadAllBytes(file), 1000);
            double sum = 0;
            for (var i = 0; i < photo.Width * photo.Height; i++)
                sum += LabColor.FromBgr(photo.Bgr[i * 3], photo.Bgr[i * 3 + 1], photo.Bgr[i * 3 + 2]).B;
            return (float)(sum / (photo.Width * photo.Height));
        }

        Assert.True(MeanB(warm) > 6f, $"the warm copy is yellow: {MeanB(warm)}");
        Assert.NotEqual(MeanB(plain), MeanB(warm), 1f);
    }

    [Fact]
    public void ThePhoto_IsGivenItsColours_AndTheyChangeWithoutTheModel()
    {
        if (!ModelPresent) return;
        var path = GreyJpeg("old.jpg", 400, 300);
        ColorPreview preview;
        using (var hub = new EffectModels(RealModels.Folder))
        using (var colorizer = hub.CreateColorizer(4))
            preview = ColorPreview.Open(colorizer, path);
        // the model is gone (disposed): the colours still change

        var asGiven = preview.Render(ColorTuning.AsGiven);
        var pale = preview.Render(new ColorTuning(Saturation: 0.4f));
        var strong = preview.Render(new ColorTuning(Saturation: 1.6f));
        var warm = preview.Render(new ColorTuning(Saturation: 0f, Warmth: 1f));

        Assert.Equal(400, preview.Width);
        Assert.Equal(300, preview.Height);
        Assert.Equal(path, preview.Path);
        Assert.False(BlackAndWhite.Measure(asGiven).IsBlackAndWhite, "it has colours");
        Assert.True(MeanChroma(pale) < MeanChroma(asGiven) && MeanChroma(asGiven) < MeanChroma(strong));
        Assert.True(BlackAndWhite.Measure(warm).IsBlackAndWhite, "warmth alone is a tone");
        Assert.True(MeanChroma(warm) > ColorAmount.GreyChroma);
    }

    [Fact]
    public void WhatIsSaved_IsWhatIsShown_AtTheSizeOfThePhoto()
    {
        if (!ModelPresent) return;
        var path = GreyJpeg("old.jpg", 320, 200);
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(4);
        var preview = ColorPreview.Open(colorizer, path);
        var tuning = new ColorTuning(1.3f, 0.4f);

        var copy = preview.Save(tuning);

        Assert.Equal(_folder.File("old (colorized).jpg"), copy);
        Assert.True(File.Exists(path));
        var saved = WicPhotoDecoder.Decode(File.ReadAllBytes(copy), 1000);
        Assert.Equal(320, saved.Width);
        Assert.Equal(200, saved.Height);
        var shown = preview.Render(tuning);
        Assert.Equal(MeanChroma(shown), MeanChroma(saved), 2f); // the JPEG changes the numbers a little

        // another save is another copy
        Assert.Equal(_folder.File("old (colorized) 2.jpg"), preview.Save(ColorTuning.AsGiven));
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void APhotoLargerThanTheScreenPicture_IsSavedAtItsOwnSize()
    {
        if (!ModelPresent) return;
        var path = GreyJpeg("wide.jpg", ColorPreview.PreviewSide + 300, 120);
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(4);
        var preview = ColorPreview.Open(colorizer, path);

        Assert.Equal(ColorPreview.PreviewSide, preview.Width); // shown smaller, so that changing the colours stays quick
        var copy = preview.Save(ColorTuning.AsGiven);

        var saved = WicPhotoDecoder.Decode(File.ReadAllBytes(copy), 8000);
        Assert.Equal(ColorPreview.PreviewSide + 300, saved.Width);
        Assert.Equal(120, saved.Height);
    }

    [Fact]
    public void ThePreview_KnowsWhenItsFileWasChanged()
    {
        if (!ModelPresent) return;
        var path = GreyJpeg("old.jpg", 200, 150);
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(2);
        var preview = ColorPreview.Open(colorizer, path);

        Assert.True(preview.MatchesFile());

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3)); // turned, or edited, by something else
        Assert.False(preview.MatchesFile());

        File.Delete(path);
        Assert.False(preview.MatchesFile());
    }

    [Fact]
    public void APhotoThatCannotBeRead_IsAnError()
    {
        if (!ModelPresent) return;
        var bad = _folder.File("bad.jpg");
        File.WriteAllBytes(bad, [1, 2, 3, 4]);
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(2);

        Assert.ThrowsAny<Exception>(() => ColorPreview.Open(colorizer, bad));
        Assert.ThrowsAny<IOException>(() => ColorPreview.Open(colorizer, _folder.File("gone.jpg")));
    }
}
