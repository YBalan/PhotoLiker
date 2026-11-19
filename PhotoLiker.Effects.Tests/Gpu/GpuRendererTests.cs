using PhotoLiker.Effects.Compositing;
using PhotoLiker.Effects.Gpu;
using PhotoLiker.Effects.Transitions;

namespace PhotoLiker.Effects.Tests.Gpu;

/// <summary>
/// The GPU renderer against the processor's compositor and mixer: the same layers and the same step of a change, put through <see cref="GpuRenderer"/> and through <see cref="FrameCompositor"/> /
/// <see cref="TransitionMixer"/>, and the two pictures compared pixel by pixel. They use the graphics chip of the computer, or WARP (the software one that Windows has) where there is none.
/// </summary>
public sealed class GpuRendererTests : IDisposable
{
    private const int W = 128, H = 72;

    private readonly GpuRenderer _gpu;

    public GpuRendererTests()
    {
        _gpu = GpuRenderer.TryCreate(allowSoftware: true)!;
        Assert.True(_gpu is not null, "a Direct3D 11 device: " + GpuRenderer.LastError);
    }

    public void Dispose() => _gpu.Dispose();

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    private static int R(uint p) => (int)(p >> 16 & 0xFF);
    private static int G(uint p) => (int)(p >> 8 & 0xFF);
    private static int B(uint p) => (int)(p & 0xFF);

    /// <summary>A picture with no sharp edges (so that the chip's smoothing and the processor's agree): red goes across, green down, blue along the diagonal, shifted by <paramref name="shift"/>.</summary>
    private static uint[] Smooth(int width, int height, int shift = 0) =>
        Enumerable.Range(0, width * height).Select(i =>
        {
            int x = i % width, y = i / width;
            return Rgb((x * 255 / Math.Max(1, width - 1) + shift) % 256, (y * 255 / Math.Max(1, height - 1) + shift * 2) % 256, ((x + y) * 255 / Math.Max(1, width + height - 2) + shift * 3) % 256);
        }).ToArray();

    private static uint[] Cpu(int width, int height, params CompositeLayer[] layers)
    {
        var screen = new uint[width * height];
        FrameCompositor.Compose(screen, width, height, layers);
        return screen;
    }

    private uint[] Gpu(int width, int height, SlideshowTransition transition, double progress, params CompositeLayer[] layers)
    {
        var screen = new uint[width * height];
        _gpu.Render(width, height, layers, transition, progress, screen);
        return screen;
    }

    /// <summary>The largest difference of any colour of any pixel, and the share of pixels that differ by more than <paramref name="tolerance"/>.</summary>
    private static (int Largest, double Bad, double Mean) Differ(uint[] a, uint[] b, int tolerance)
    {
        Assert.Equal(a.Length, b.Length);
        var largest = 0;
        var bad = 0;
        long sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = Math.Max(Math.Abs(R(a[i]) - R(b[i])), Math.Max(Math.Abs(G(a[i]) - G(b[i])), Math.Abs(B(a[i]) - B(b[i]))));
            largest = Math.Max(largest, d);
            sum += Math.Abs(R(a[i]) - R(b[i])) + Math.Abs(G(a[i]) - G(b[i])) + Math.Abs(B(a[i]) - B(b[i]));
            if (d > tolerance)
                bad++;
        }
        return (largest, bad / (double)a.Length, sum / (3.0 * a.Length));
    }

    private static void AssertSame(uint[] cpu, uint[] gpu, int tolerance, double maxBad, string what)
    {
        var (largest, bad, mean) = Differ(cpu, gpu, tolerance);
        Assert.True(bad <= maxBad, $"{what}: {bad:P2} of the pixels differ by more than {tolerance} (the largest difference {largest}, the mean {mean:0.00})");
    }

    // ── layers put together ─────────────────────────────────────────────────

    [Fact]
    public void APictureThatIsOnlyMoved_IsCopiedExactly_AndBlackAround()
    {
        var picture = Smooth(60, 40);
        var layer = new CompositeLayer(picture, 60, 40, 64.4, 36.2, 1, 256);

        Assert.Equal(Cpu(W, H, layer), Gpu(W, H, SlideshowTransition.None, 0, layer));
    }

    [Fact]
    public void NoLayers_IsBlack_AndWhatIsOutsideTheScreenIsLeftOut()
    {
        Assert.All(Gpu(W, H, SlideshowTransition.None, 0), pixel => Assert.Equal(0xFF000000u, pixel));
        Assert.All(Gpu(W, H, SlideshowTransition.None, 0, new CompositeLayer(Smooth(30, 20), 30, 20, 900, 900, 1, 256)), pixel => Assert.Equal(0xFF000000u, pixel));
    }

    [Fact]
    public void APictureWithPartOfItsWeight_IsAsBrightAsThatPart()
    {
        var picture = Smooth(60, 40);
        var layer = new CompositeLayer(picture, 60, 40, 64, 36, 1, 128);

        AssertSame(Cpu(W, H, layer), Gpu(W, H, SlideshowTransition.None, 0, layer), tolerance: 1, maxBad: 0, "half the weight");
    }

    [Theory]
    [InlineData(1.3)]
    [InlineData(0.6)]
    [InlineData(2.4)]
    public void AMagnifiedPicture_IsTheProcessors_Within_TheSmoothingOfTheChip(double scale)
    {
        var picture = Smooth(60, 40);
        var layer = new CompositeLayer(picture, 60, 40, 70.3, 30.8, scale, 256);

        AssertSame(Cpu(W, H, layer), Gpu(W, H, SlideshowTransition.None, 0, layer), tolerance: 4, maxBad: 0.005, $"scale {scale}");
    }

    [Theory]
    [InlineData(0.05, 1.0)]
    [InlineData(-0.3, 1.0)]
    [InlineData(0.6, 0.8)]
    [InlineData(Math.PI / 2, 1.0)]
    public void ATurnedPicture_IsTheProcessors_AndItsEdgeIsSmoothToo(double rotation, double scale)
    {
        var picture = Smooth(60, 40);
        var layer = new CompositeLayer(picture, 60, 40, 66, 34, scale, 256, null, rotation);

        AssertSame(Cpu(W, H, layer), Gpu(W, H, SlideshowTransition.None, 0, layer), tolerance: 6, maxBad: 0.01, $"turn {rotation}");
    }

    [Fact]
    public void TwoLayersThatDissolve_AddUpToTheWhole()
    {
        var a = Smooth(W, H);
        var b = Smooth(W, H, shift: 90);
        var layers = new[] { new CompositeLayer(a, W, H, W / 2.0, H / 2.0, 1, 100), new CompositeLayer(b, W, H, W / 2.0, H / 2.0, 1.15, 156) };

        AssertSame(Cpu(W, H, layers), Gpu(W, H, SlideshowTransition.Fade, 0.4, layers), tolerance: 4, maxBad: 0.005, "a dissolve of a picture and a magnified one");
    }

    [Fact]
    public void ADepthLayer_MovesByItsDepth_AsOnTheProcessor()
    {
        var picture = Smooth(W, H);
        var depth = Enumerable.Range(0, 32 * 18).Select(i => (byte)(i % 32 * 255 / 31)).ToArray(); // near on the right, far on the left
        var parallax = new CompositeLayer(picture, W, H, W / 2.0, H / 2.0, 1.1, 256, new DepthShift(depth, 32, 18, 6, -3, 0.5));
        var dolly = new CompositeLayer(picture, W, H, W / 2.0, H / 2.0, 1.15, 256, new DepthShift(depth, 32, 18, 0, 0, 0.5, 0.25));

        AssertSame(Cpu(W, H, parallax), Gpu(W, H, SlideshowTransition.None, 0, parallax), tolerance: 8, maxBad: 0.02, "parallax");
        AssertSame(Cpu(W, H, dolly), Gpu(W, H, SlideshowTransition.None, 0, dolly), tolerance: 8, maxBad: 0.02, "dolly zoom");
        Assert.NotEqual(Gpu(W, H, SlideshowTransition.None, 0, parallax), Gpu(W, H, SlideshowTransition.None, 0, new CompositeLayer(picture, W, H, W / 2.0, H / 2.0, 1.1, 256)));
    }

    [Fact]
    public void ADepthThatDoesNotFit_LeavesThePictureAsItIs()
    {
        var picture = Smooth(60, 40);
        var broken = new CompositeLayer(picture, 60, 40, 64, 36, 1.5, 256, new DepthShift([1, 2, 3], 16, 16, 5, 5, 0.5));

        Assert.Equal(Cpu(W, H, broken), Gpu(W, H, SlideshowTransition.None, 0, broken));
    }

    // ── changes ─────────────────────────────────────────────────────────────

    private static readonly SlideshowTransition[] Mixed =
    [
        SlideshowTransition.Wipe, SlideshowTransition.Iris, SlideshowTransition.Pixelate, SlideshowTransition.Ripple,
        SlideshowTransition.Cube, SlideshowTransition.BlurDissolve, SlideshowTransition.DipBlack, SlideshowTransition.DipWhite,
    ];

    /// <summary>The step of a change on the processor: each picture put together alone, then mixed.</summary>
    private static uint[] CpuChange(CompositeLayer[] layers, SlideshowTransition transition, double progress, int width, int height)
    {
        var from = Cpu(width, height, layers[0] with { Weight = 256 });
        var to = Cpu(width, height, layers[1] with { Weight = 256 });
        var screen = new uint[width * height];
        TransitionMixer.Mix(screen, from, to, width, height, transition, progress);
        return screen;
    }

    [Theory]
    [InlineData(SlideshowTransition.Wipe, 3, 0.001)]
    [InlineData(SlideshowTransition.Iris, 3, 0.001)]
    [InlineData(SlideshowTransition.Pixelate, 3, 0.001)]
    [InlineData(SlideshowTransition.Ripple, 10, 0.02)]
    [InlineData(SlideshowTransition.Cube, 12, 0.03)]
    [InlineData(SlideshowTransition.DipBlack, 3, 0.001)]
    [InlineData(SlideshowTransition.DipWhite, 3, 0.001)]
    public void AChange_LooksLikeTheProcessorsAtEveryStep(SlideshowTransition transition, int tolerance, double maxBad)
    {
        var layers = new[]
        {
            new CompositeLayer(Smooth(W, H), W, H, W / 2.0, H / 2.0, 1, 256),
            new CompositeLayer(Smooth(W, H, shift: 120), W, H, W / 2.0, H / 2.0, 1, 256),
        };

        foreach (var progress in new[] { 0.15, 0.3, 0.5, 0.7, 0.9 })
            AssertSame(CpuChange(layers, transition, progress, W, H), Gpu(W, H, transition, progress, layers), tolerance, maxBad, $"{transition} at {progress}");
    }

    [Fact]
    public void TheBlur_IsSmootherThanThePictures_AndComesBackToThem_AndIsNearTheProcessors()
    {
        // A chessboard of 2 by 2 pixels: the sharpest there is
        var board = Enumerable.Range(0, W * H).Select(i => (i % W / 2 + i / W / 2) % 2 == 0 ? 0xFFFFFFFFu : 0xFF000000u).ToArray();
        var layers = new[] { new CompositeLayer(board, W, H, W / 2.0, H / 2.0, 1, 256), new CompositeLayer(board, W, H, W / 2.0, H / 2.0, 1, 256) };

        int Roughness(uint[] picture)
        {
            var sum = 0;
            for (var y = 0; y < H; y++)
            for (var x = 1; x < W; x++)
                sum += Math.Abs(R(picture[y * W + x]) - R(picture[y * W + x - 1]));
            return sum;
        }

        var half = Gpu(W, H, SlideshowTransition.BlurDissolve, 0.5, layers);
        var early = Gpu(W, H, SlideshowTransition.BlurDissolve, 0.1, layers);
        Assert.True(Roughness(half) < Roughness(board) / 4, $"half way it is blurred: {Roughness(half)} against {Roughness(board)}");
        Assert.True(Roughness(early) > Roughness(half), "and less so at the start");

        var smooth = new[] { new CompositeLayer(Smooth(W, H), W, H, W / 2.0, H / 2.0, 1, 256), new CompositeLayer(Smooth(W, H, 100), W, H, W / 2.0, H / 2.0, 1, 256) };
        AssertSame(CpuChange(smooth, SlideshowTransition.BlurDissolve, 0.4, W, H), Gpu(W, H, SlideshowTransition.BlurDissolve, 0.4, smooth), tolerance: 12, maxBad: 0.05, "blur of smooth pictures");
    }

    [Fact]
    public void EveryChange_BeginsOnTheFirstPicture_AndEndsOnTheSecond_Exactly()
    {
        var layers = new[]
        {
            new CompositeLayer(Smooth(W, H), W, H, W / 2.0, H / 2.0, 1.1, 256),
            new CompositeLayer(Smooth(W, H, shift: 120), W, H, W / 2.0, H / 2.0, 0.9, 256),
        };
        var first = Cpu(W, H, layers[0]);
        var second = Cpu(W, H, layers[1]);

        foreach (var transition in Mixed)
        {
            AssertSame(first, Gpu(W, H, transition, 0, layers), 3, 0, $"{transition} at 0"); // magnified layers: the chip smooths a little differently
            AssertSame(second, Gpu(W, H, transition, 1, layers), 3, 0, $"{transition} at 1");
            AssertSame(first, Gpu(W, H, transition, double.NaN, layers), 3, 0, $"{transition} at no number");
        }
    }

    [Fact]
    public void AChangeThatIsNotAMixedOne_IsLayersLaidOverEachOther()
    {
        var layers = new[]
        {
            new CompositeLayer(Smooth(W, H), W, H, W / 2.0, H / 2.0, 1, 90),
            new CompositeLayer(Smooth(W, H, 50), W, H, W / 2.0, H / 2.0, 1, 166),
        };

        AssertSame(Cpu(W, H, layers), Gpu(W, H, SlideshowTransition.Fade, 0.5, layers), 2, 0, "a dissolve is not mixed");
    }

    // ── the pixels and the textures ─────────────────────────────────────────

    [Fact]
    public void ThePixels_AreHandedOverWithTheirRowPitch_AndOnlyWhileTheyAreReadable()
    {
        var picture = Smooth(W, H);
        var layer = new CompositeLayer(picture, W, H, W / 2.0, H / 2.0, 1, 256);
        var pitch = 0;
        uint first = 0;

        _gpu.Render(W, H, [layer], SlideshowTransition.None, 0, (pixels, rowPitch) =>
        {
            pitch = rowPitch;
            first = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(pixels + rowPitch * 10 + 4 * 20);
        });

        Assert.True(pitch >= W * 4, $"a row takes {pitch} bytes");
        Assert.Equal(picture[10 * W + 20], first);
    }

    [Fact]
    public void AFrame_IsMadeATextureOnce_WhoeverAsks_AndLetGoOfWhenItIsForgotten()
    {
        var a = Smooth(W, H);
        var b = Smooth(W, H, 7);
        Assert.Equal(0, _gpu.TextureCount);

        _gpu.Warm(a, W, H);
        _gpu.Warm(a, W, H);
        Assert.Equal(1, _gpu.TextureCount);
        Assert.Equal((long)W * H * 4, _gpu.TextureBytes);

        Gpu(W, H, SlideshowTransition.None, 0, new CompositeLayer(a, W, H, W / 2.0, H / 2.0, 1, 256)); // used: no second texture
        Assert.Equal(1, _gpu.TextureCount);
        Gpu(W, H, SlideshowTransition.None, 0, new CompositeLayer(b, W, H, W / 2.0, H / 2.0, 1, 256)); // made by the step that needs it
        Assert.Equal(2, _gpu.TextureCount);

        _gpu.Forget(a);
        Assert.Equal(1, _gpu.TextureCount);
        Assert.Equal((long)W * H * 4, _gpu.TextureBytes);
        _gpu.Forget(a); // not there any more: nothing happens
        _gpu.Forget(new uint[5]);
        Assert.Equal(1, _gpu.TextureCount);
    }

    [Fact]
    public void TheTextures_StayWithinTheirMemory_TheOldestGoFirst_AndNeverOneThatTheStepUses()
    {
        _gpu.MaxTextureBytes = 3L * W * H * 4;
        var pictures = Enumerable.Range(0, 5).Select(i => Smooth(W, H, i * 10)).ToArray();

        foreach (var picture in pictures)
            Gpu(W, H, SlideshowTransition.None, 0, new CompositeLayer(picture, W, H, W / 2.0, H / 2.0, 1, 256));
        Assert.Equal(3, _gpu.TextureCount); // the first two were let go of, the last three stay

        // A step that uses two pictures keeps both even when the memory is short
        var layers = new[] { new CompositeLayer(pictures[0], W, H, W / 2.0, H / 2.0, 1, 256), new CompositeLayer(pictures[1], W, H, W / 2.0, H / 2.0, 1, 256) };
        AssertSame(CpuChange(layers, SlideshowTransition.Wipe, 0.5, W, H), Gpu(W, H, SlideshowTransition.Wipe, 0.5, layers), 3, 0.001, "wipe with the pictures made again");
        Assert.True(_gpu.TextureCount <= 3);
        Assert.True(_gpu.TextureBytes <= _gpu.MaxTextureBytes);
    }

    [Fact]
    public void WarmingAhead_MakesNothing_WhenTheMemoryIsUsedUp()
    {
        _gpu.MaxTextureBytes = (long)W * H * 4;
        _gpu.Warm(Smooth(W, H), W, H);
        _gpu.Warm(Smooth(W, H, 5), W, H);

        Assert.Equal(1, _gpu.TextureCount);
    }

    [Fact]
    public void WarmingFromManyThreads_IsSafe()
    {
        var pictures = Enumerable.Range(0, 24).Select(i => Smooth(W, H, i)).ToArray();

        Parallel.ForEach(pictures, picture => _gpu.Warm(picture, W, H));

        Assert.Equal(24, _gpu.TextureCount);
        Parallel.ForEach(pictures, picture => _gpu.Forget(picture));
        Assert.Equal(0, _gpu.TextureCount);
        Assert.Equal(0, _gpu.TextureBytes);
    }

    [Fact]
    public void ASizeThatChanges_IsDrawnAtTheNewSize()
    {
        foreach (var (w, h) in new[] { (64, 36), (200, 100), (64, 36), (31, 17) })
        {
            var picture = Smooth(w, h);
            var layer = new CompositeLayer(picture, w, h, w / 2.0, h / 2.0, 1, 256);
            Assert.Equal(Cpu(w, h, layer), Gpu(w, h, SlideshowTransition.None, 0, layer));
        }
    }

    [Fact]
    public void AfterItIsDisposed_ItRefusesToDraw()
    {
        var other = GpuRenderer.TryCreate(allowSoftware: true)!;
        other.Dispose();
        other.Dispose(); // twice is fine

        Assert.Throws<ObjectDisposedException>(() => other.Render(W, H, [], SlideshowTransition.None, 0, (_, _) => { }));
        other.Warm(Smooth(W, H), W, H); // nothing happens
        Assert.Equal(0, other.TextureCount);
    }

    [Fact]
    public void ItNamesTheChipThatDoesTheWork()
    {
        Assert.False(string.IsNullOrWhiteSpace(_gpu.AdapterName));
        Assert.Null(GpuRenderer.LastError);
    }

    [Fact]
    public void BadArguments_AreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _gpu.Render(0, H, [], SlideshowTransition.None, 0, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => _gpu.Render(W, H, [], SlideshowTransition.None, 0, (GpuPixels)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => _gpu.Render(W, H, [], SlideshowTransition.None, 0, new uint[5]));
    }
}
