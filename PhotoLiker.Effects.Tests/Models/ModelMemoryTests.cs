using System.Diagnostics;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Models;

namespace PhotoLiker.Effects.Tests.Models;

/// <summary>Nothing else runs while these do: they look at the memory of the whole process.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModelMemoryCollection
{
    public const string Name = "ModelMemory";
}

/// <summary>
/// What the models of the effects keep of their runs. ONNX Runtime's memory arena keeps everything a session has used, for as long as the session lives: the colorizing model sat on 4.6 GB after
/// two runs (a model the colorize bar keeps open for as long as the person looks at photos), the depth model on 0.5 to 1.3 GB (kept for the whole run of the program). The sessions are made
/// without it; these tests run the real models where their files are, and look at what the process holds after the runs.
/// </summary>
[Collection(ModelMemoryCollection.Name)]
public class ModelMemoryTests
{
    private static long PrivateMegabytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64 / (1024 * 1024);
    }

    private static PhotoLiker.Search.DecodedPhoto GreyDisc(int width, int height)
    {
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = (x - width / 2.0) / (width * 0.25);
            var dy = (y - height / 2.0) / (height * 0.35);
            var light = dx * dx + dy * dy < 1 ? 200 - 60 * (dx * dx + dy * dy) : 70 + 80.0 * y / height;
            Array.Fill(bgr, (byte)Math.Clamp(light + 10 * Math.Sin(x / 5.0), 0, 255), (y * width + x) * 3, 3);
        }
        return new PhotoLiker.Search.DecodedPhoto(width, height, bgr);
    }

    [Fact]
    public void TheColorizingModel_DoesNotKeepTheMemoryOfItsRuns()
    {
        if (!ToolModels.Colorizer.IsPresentIn(RealModels.Folder)) return;
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(4);
        var photo = GreyDisc(640, 400);

        _ = colorizer.Analyze(photo); // the model is open and has run once
        GC.Collect();
        var before = PrivateMegabytes();
        for (var run = 0; run < 3; run++)
            _ = colorizer.Analyze(photo);
        GC.Collect();
        var grown = PrivateMegabytes() - before;

        // Measured: a few tens of MB without the arena, 2100 MB more with it (the second run takes as much again as the first)
        Assert.True(grown < 800, $"the process grew by {grown} MB over three more runs");
    }

    [Fact]
    public void TheDepthModel_DoesNotKeepTheMemoryOfSeveralRunsAtOnce()
    {
        if (!ToolModels.Depth.IsPresentIn(RealModels.Folder)) return;
        using var hub = new EffectModels(RealModels.Folder);
        var depth = hub.Depth;
        var sizes = new (int Width, int Height)[] { (1920, 1280), (1440, 1080), (720, 1080), (1920, 1080), (1080, 1080), (810, 1080), (1920, 900), (1920, 1100) };
        var pictures = sizes.Select(size => new uint[size.Width * size.Height]).ToArray();
        for (var i = 0; i < pictures.Length; i++)
            new Random(i).NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pictures[i].AsSpan()));

        // The slideshow works out the depth of several photos at once, each of a shape of its own
        Parallel.For(0, pictures.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => depth.Estimate(pictures[i], sizes[i].Width, sizes[i].Height));
        GC.Collect();
        var before = PrivateMegabytes();
        for (var round = 0; round < 3; round++)
            Parallel.For(0, pictures.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => depth.Estimate(pictures[i], sizes[i].Width, sizes[i].Height));
        GC.Collect();
        var grown = PrivateMegabytes() - before;

        Assert.True(grown < 250, $"the process grew by {grown} MB over 24 more runs");
    }
}
