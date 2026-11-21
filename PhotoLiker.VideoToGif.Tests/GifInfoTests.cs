namespace PhotoLiker.VideoToGif.Tests;

/// <summary>The reader of what is in a GIF, tried on GIF files that are built here byte by byte, so that every kind of block is known to be there.</summary>
public sealed class GifInfoTests
{
    /// <summary>A GIF of <paramref name="frames"/> frames of 1 x 1 pixel, each shown for <paramref name="delays"/> hundredths of a second.</summary>
    private static byte[] Gif(int width, int height, int[] delays, int? loop = 0, int globalColors = 4, int[]? localPalettes = null, bool trailer = true, bool comment = false)
    {
        var bytes = new List<byte>("GIF89a"u8.ToArray())
        {
            (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8),
        };
        if (globalColors > 0)
        {
            bytes.Add((byte)(0x80 | ((int)Math.Log2(globalColors) - 1))); // the size of the palette is 2 to the power of this + 1
            bytes.Add(0);
            bytes.Add(0);
            for (var i = 0; i < globalColors * 3; i++)
                bytes.Add((byte)i);
        }
        else
        {
            bytes.AddRange([0, 0, 0]);
        }

        if (comment)
            bytes.AddRange([0x21, 0xFE, 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0]);

        if (loop is { } repeats)
        {
            bytes.AddRange([0x21, 0xFF, 11]);
            bytes.AddRange("NETSCAPE2.0"u8.ToArray());
            bytes.AddRange([3, 1, (byte)repeats, (byte)(repeats >> 8), 0]);
        }

        for (var frame = 0; frame < delays.Length; frame++)
        {
            bytes.AddRange([0x21, 0xF9, 4, 0, (byte)delays[frame], (byte)(delays[frame] >> 8), 0, 0]);
            bytes.AddRange([0x2C, 0, 0, 0, 0, 1, 0, 1, 0]);
            var colors = localPalettes is not null && frame < localPalettes.Length ? localPalettes[frame] : 0;
            bytes.Add((byte)(colors > 0 ? 0x80 | ((int)Math.Log2(colors) - 1) : 0));
            for (var i = 0; i < colors * 3; i++)
                bytes.Add(7);

            bytes.AddRange([2, 3, 1, 2, 3, 0]); // the smallest code size, one sub-block of three bytes, the end of the data
        }

        if (trailer)
            bytes.Add(0x3B);
        return [.. bytes];
    }

    [Fact]
    public void TheSizeFramesAndLength_AreRead()
    {
        var info = GifInfo.Read(Gif(320, 240, [10, 10, 5, 25]));

        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
        Assert.Equal(4, info.FrameCount);
        Assert.Equal(0.5, info.DurationSeconds, 6);
    }

    [Fact]
    public void ASizeBeyondOneByte_IsRead()
    {
        var info = GifInfo.Read(Gif(1920, 1080, [2]));

        Assert.Equal((1920, 1080), (info.Width, info.Height));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(500)]
    public void TheRepeatCount_IsRead(int repeats)
    {
        var info = GifInfo.Read(Gif(2, 2, [5], loop: repeats));

        Assert.Equal(repeats, info.LoopCount);
        Assert.Equal(repeats == 0, info.LoopsForever);
    }

    [Fact]
    public void AGifWithoutTheRepeatBlock_PlaysOnce()
    {
        var info = GifInfo.Read(Gif(2, 2, [5, 5], loop: null));

        Assert.Null(info.LoopCount);
        Assert.False(info.LoopsForever);
    }

    [Fact]
    public void ThePalettes_AreCounted()
    {
        var info = GifInfo.Read(Gif(2, 2, [5, 5, 5], globalColors: 16, localPalettes: [0, 8, 4]));

        Assert.Equal(16, info.GlobalColors);
        Assert.Equal(2, info.LocalPalettes);
        Assert.Equal(3, info.FrameCount);
    }

    [Fact]
    public void AGifWithoutAGlobalPalette_Reads()
    {
        var info = GifInfo.Read(Gif(2, 2, [5, 5], globalColors: 0, localPalettes: [2, 2]));

        Assert.Equal(0, info.GlobalColors);
        Assert.Equal(2, info.LocalPalettes);
        Assert.Equal(2, info.FrameCount);
    }

    [Fact]
    public void ABlockThatIsOfNoInterest_IsSkipped()
    {
        var info = GifInfo.Read(Gif(2, 2, [7], comment: true));

        Assert.Equal(1, info.FrameCount);
        Assert.Equal(0.07, info.DurationSeconds, 6);
        Assert.Equal(0, info.LoopCount);
    }

    [Fact]
    public void AGifWithNoFrames_HasNone()
    {
        var info = GifInfo.Read(Gif(2, 2, []));

        Assert.Equal(0, info.FrameCount);
        Assert.Equal(0, info.DurationSeconds);
    }

    [Fact]
    public void ADelayIsOnlyForTheFrameThatFollowsIt()
    {
        // The second frame has no timing block of its own: it is shown for no time, not for the time of the first
        var bytes = Gif(2, 2, [50]).ToList();
        var frame = bytes.IndexOf(0x2C);
        var second = bytes.Skip(frame).TakeWhile(b => b != 0x3B).ToArray();
        bytes.InsertRange(bytes.Count - 1, second);

        var info = GifInfo.Read([.. bytes]);

        Assert.Equal(2, info.FrameCount);
        Assert.Equal(0.5, info.DurationSeconds, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GIF89")]
    [InlineData("PNG is a different file, long enough to be read")]
    public void WhatIsNotAGif_IsRefused(string text) =>
        Assert.Throws<InvalidDataException>(() => GifInfo.Read(System.Text.Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void AGif87a_IsAGifToo()
    {
        var bytes = Gif(2, 2, [5]);
        bytes[4] = (byte)'7';

        Assert.Equal(1, GifInfo.Read(bytes).FrameCount);
    }

    [Fact]
    public void AGifThatIsCutOff_IsRefused_AtEveryPlaceItCanBeCut()
    {
        var whole = Gif(3, 3, [5, 5], localPalettes: [2, 2]);
        Assert.Equal(2, GifInfo.Read(whole).FrameCount);

        // Every cut is refused (also the one that only lacks the end mark), and with nothing but a refusal: never an index that is out of range
        for (var length = 0; length < whole.Length; length++)
        {
            var cut = whole[..length];
            Assert.Throws<InvalidDataException>(() => GifInfo.Read(cut));
        }
    }

    [Fact]
    public void ABlockThatIsNotKnown_IsRefused()
    {
        var bytes = Gif(2, 2, [5]);
        bytes[^1] = 0x77;

        Assert.Throws<InvalidDataException>(() => GifInfo.Read(bytes));
    }

    [Fact]
    public void ThePathOverload_ReadsTheFile()
    {
        using var folder = new TempFolder();
        File.WriteAllBytes(folder.File("a.gif"), Gif(7, 5, [4, 4]));

        var info = GifInfo.Read(folder.File("a.gif"));

        Assert.Equal((7, 5, 2), (info.Width, info.Height, info.FrameCount));
    }
}
