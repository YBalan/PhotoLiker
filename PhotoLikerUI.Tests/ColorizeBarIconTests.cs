using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>The pictures on the buttons of the bar over a black and white photo (see <see cref="ColorizeBar"/>): they are drawn at the size asked for, are not empty, and the one that stands for the original is grey.</summary>
public sealed class ColorizeBarIconTests
{
    private static readonly (string Name, Func<Bitmap> Icon)[] Icons =
    [
        ("Colorize", AppIcons.Colorize),
        ("OriginalPhoto", AppIcons.OriginalPhoto),
        ("SaveCopy", AppIcons.SaveCopy),
        ("ManyPhotos", AppIcons.ManyPhotos),
    ];

    /// <summary>The pixels that are drawn (not transparent), as (R, G, B, A).</summary>
    private static List<Color> Drawn(Bitmap bitmap)
    {
        var drawn = new List<Color>();
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A > 0)
                    drawn.Add(pixel);
            }
        return drawn;
    }

    /// <summary>How far a pixel is from grey: the biggest difference between its red, green and blue.</summary>
    private static int Tint(Color pixel) => Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));

    [Theory]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(32)]
    public void EveryPicture_IsDrawnAtTheSizeAskedFor_AndIsNotEmpty(int size)
    {
        foreach (var (name, icon) in Icons)
        {
            using var bitmap = AppIcons.Sized(size, icon);

            Assert.True(bitmap.Width == size && bitmap.Height == size, $"{name} at {size}");
            Assert.True(Drawn(bitmap).Count > size * size / 4, $"{name} at {size} has something on it");
        }
    }

    [Fact]
    public void ThePicturesAreDrawnAtTheNormalSizeAgain_AfterOneWasMadeBigger()
    {
        using (AppIcons.Sized(32, AppIcons.SaveCopy))
        {
        }

        using var normal = AppIcons.SaveCopy();
        Assert.Equal(16, normal.Width);
    }

    [Fact]
    public void TheOriginal_IsGrey_AndTheColourPhotoIsNot()
    {
        using var original = AppIcons.OriginalPhoto();
        using var colorize = AppIcons.Colorize();

        Assert.All(Drawn(original), pixel => Assert.True(Tint(pixel) <= 2, $"a grey picture: {pixel}"));
        Assert.Contains(Drawn(colorize), pixel => Tint(pixel) > 60); // the colour half: the sky, the sun and the hill
    }

    [Fact]
    public void ThePicturesOfTheButtons_AreNotTheSame()
    {
        var pictures = Icons.Select(icon =>
        {
            using var bitmap = AppIcons.Sized(18, icon.Icon);
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            return Convert.ToBase64String(memory.ToArray());
        }).ToList();

        Assert.Equal(pictures.Count, pictures.Distinct().Count());
    }
}
