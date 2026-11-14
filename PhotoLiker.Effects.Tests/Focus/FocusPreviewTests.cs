using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Focus;
using PhotoLiker.Effects.Motion;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Tests.Focus;

/// <summary>The photo a person refocuses while they look at it: what it starts with, what a click says, that the copy is a copy (named after the photo, never over anything, at the size of the photo).</summary>
public class FocusPreviewTests : IDisposable
{
    private const int W = 320, H = 200;

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>A photo that is all fine detail, on a JPEG file with a name: a chessboard of 4 pixel squares.</summary>
    private string MakePhoto(string name = "granny.jpg", int width = W, int height = H)
    {
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var light = (x / 4 + y / 4) % 2 == 0 ? (byte)225 : (byte)35;
            bgr[(y * width + x) * 3] = bgr[(y * width + x) * 3 + 1] = bgr[(y * width + x) * 3 + 2] = light;
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, bgr, width * 3)));
        var path = _folder.File(name);
        using (var file = File.Create(path))
            encoder.Save(file);
        return path;
    }

    /// <summary>The left three quarters near, the right quarter far.</summary>
    private static DepthMap NearLeft() => new(16, 10, Enumerable.Range(0, 160).Select(i => i % 16 < 12 ? (byte)255 : (byte)0).ToArray());

    private FocusPreview Preview(string path, IReadOnlyList<PhotoRegion>? faces = null, DepthMap? depth = null)
    {
        var info = new FileInfo(path);
        var bytes = File.ReadAllBytes(path);
        return new FocusPreview(path, bytes, WicPhotoDecoder.Decode(bytes, FocusPreview.PreviewSide), depth ?? NearLeft(), faces ?? [], info.Length, info.LastWriteTimeUtc);
    }

    private static double Detail(DecodedPhoto photo, int from, int to)
    {
        double sum = 0;
        var count = 0;
        for (var y = 0; y < photo.Height; y++)
        for (var x = from; x < to - 1; x++)
        {
            sum += Math.Abs(photo.Bgr[(y * photo.Width + x) * 3] - photo.Bgr[(y * photo.Width + x + 1) * 3]);
            count++;
        }
        return sum / count;
    }

    [Fact]
    public void ItBeginsWithTheFocusOnTheMiddleOfThePhoto_OrOnTheLargestFace()
    {
        var path = MakePhoto();

        Assert.InRange(Preview(path).Initial.Focus, 0.95, 1);                                                              // the middle is near
        Assert.InRange(Preview(path, [new PhotoRegion(0.8, 0.3, 0.15, 0.25)]).Initial.Focus, 0, 0.05);                   // a face in the far quarter
        Assert.Equal(FocusLook.UsualBlur, Preview(path).Initial.Blur);
        Assert.Equal(FocusLook.UsualHaze, Preview(path).Initial.Haze);
    }

    [Fact]
    public void ThePreview_IsAsLargeAsThePhotoShownAndNotLarger()
    {
        var small = Preview(MakePhoto("small.jpg"));
        var large = Preview(MakePhoto("large.jpg", 2400, 1200));

        Assert.Equal((W, H), (small.Width, small.Height));
        Assert.Equal((FocusPreview.PreviewSide, 800), (large.Width, large.Height)); // the longer side is the preview side, the shape is kept
        var rendered = large.Render(large.Initial);
        Assert.Equal((large.Width, large.Height), (rendered.Width, rendered.Height));
    }

    [Fact]
    public void WhereThePersonPoints_IsWhereTheFocusIs_AndTheLookRendersIt()
    {
        var preview = Preview(MakePhoto());

        var onTheFar = preview.Render(new FocusLook(preview.FocusAt(0.9, 0.5), 1, 0.3, 0));
        var onTheNear = preview.Render(new FocusLook(preview.FocusAt(0.2, 0.5), 1, 0.3, 0));

        // Focused on the far quarter the near part is blurred; focused on the near part the far quarter is
        Assert.True(Detail(onTheFar, 0, W * 4 / 10) < Detail(onTheNear, 0, W * 4 / 10) * 0.3, $"near part: blurred {Detail(onTheFar, 0, W * 4 / 10):0.0}, sharp {Detail(onTheNear, 0, W * 4 / 10):0.0}; focus far {preview.FocusAt(0.9, 0.5):0.00} near {preview.FocusAt(0.2, 0.5):0.00}");
        Assert.True(Detail(onTheNear, W * 9 / 10, W) < Detail(onTheFar, W * 9 / 10, W) * 0.3, $"far part: blurred {Detail(onTheNear, W * 9 / 10, W):0.0}, sharp {Detail(onTheFar, W * 9 / 10, W):0.0}");
    }

    [Fact]
    public void TheCopy_IsNextToThePhoto_Named_Portrait_AndNeverWrittenOverAnything()
    {
        var path = MakePhoto();
        var preview = Preview(path);

        var first = preview.Save(preview.Initial);
        var second = preview.Save(preview.Initial);

        Assert.Equal(_folder.File("granny (portrait).jpg"), first);
        Assert.Equal(_folder.File("granny (portrait) 2.jpg"), second);
        Assert.True(File.Exists(path), "the photo is not touched");
        Assert.Equal(W, new JpegBitmapDecoder(new Uri(first), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth);
    }

    [Fact]
    public void SaveAs_WritesExactlyWhereTheLookIsSaid_OverAFileThatIsThere_AsAJpeg_WithTheDatesOfThePhoto()
    {
        var path = MakePhoto();
        File.SetLastWriteTimeUtc(path, new DateTime(2011, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        File.SetCreationTimeUtc(path, new DateTime(2010, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var preview = Preview(path);
        var destination = _folder.File(@"chosen\somewhere else.png"); // the extension is the person's: the content is a JPEG all the same
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "an older file");

        var written = preview.SaveAs(preview.Initial, destination);

        Assert.Equal(destination, written);
        var bytes = File.ReadAllBytes(destination);
        Assert.True(bytes[0] == 0xFF && bytes[1] == 0xD8, "a JPEG, not what was there");
        Assert.Equal(new DateTime(2011, 3, 4, 5, 6, 7, DateTimeKind.Utc), File.GetLastWriteTimeUtc(destination));
        Assert.Equal(new DateTime(2010, 1, 2, 3, 4, 5, DateTimeKind.Utc), File.GetCreationTimeUtc(destination));
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.part")); // no half-written file is left
        Assert.Single(Directory.GetFiles(_folder.Path, "*.jpg"));                          // and nothing was written next to the photo
    }

    [Fact]
    public void TheCopy_IsMadeAtTheSizeOfThePhoto_NotAtTheSizeOfThePreview()
    {
        var path = MakePhoto("big.jpg", 3000, 1500);
        var preview = Preview(path);
        Assert.Equal(FocusPreview.PreviewSide, preview.Width);

        var copy = preview.Save(preview.Initial);

        var frame = new JpegBitmapDecoder(new Uri(copy), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal((3000, 1500), (frame.PixelWidth, frame.PixelHeight)); // up to SaveSide: this photo is smaller
    }

    [Fact]
    public void ThePhotoThatWasChangedMeanwhile_IsNoLongerTheOneThatWasLookedAt()
    {
        var path = MakePhoto();
        var preview = Preview(path);
        Assert.True(preview.MatchesFile());

        File.AppendAllText(path, "x");

        Assert.False(preview.MatchesFile());
        File.Delete(path);
        Assert.False(preview.MatchesFile());
    }

    [Fact]
    public void ACopyOfAnotherKind_IsNamedByItsSuffix_AndTheColorizedOnesStayAsTheyWere()
    {
        Assert.Equal(@"C:\p\a (portrait).jpg", ColorizedCopy.PathFor(@"C:\p\a.jpg", FocusPreview.Suffix));
        Assert.Equal(@"C:\p\a (colorized).jpg", ColorizedCopy.PathFor(@"C:\p\a.jpg"));

        var photo = _folder.File("b.jpg");
        File.WriteAllText(photo, "photo");
        File.WriteAllText(_folder.File("b (portrait).jpg"), "taken");
        Assert.Equal(_folder.File("b (portrait) 2.jpg"), ColorizedCopy.NextFreePath(photo, FocusPreview.Suffix));
        Assert.Equal(_folder.File("b (colorized).jpg"), ColorizedCopy.NextFreePath(photo));
    }
}
