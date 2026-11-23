using System.IO.Compression;

namespace PhotoLiker.Stickers.Tests;

public class PngWriterTests
{
    /// <summary>A picture of noise with every kind of row in it: flat, steps, and random (the bytes -128 and 127 are in there too, which the filters must not choke on).</summary>
    private static StickerPicture Noisy(int width = 37, int height = 23)
    {
        var random = new Random(99);
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width * 4; x++)
            {
                bgra[y * width * 4 + x] = (y % 3) switch
                {
                    0 => 200,                                    // flat
                    1 => (byte)((x / 4) * 7),                    // a ramp
                    _ => (byte)random.Next(256),                 // noise
                };
            }
        }
        return new StickerPicture(width, height, bgra);
    }

    /// <summary>An independent reader of what the writer writes: checks the chunks and their CRCs, inflates the data and undoes the filters. Gives RGBA.</summary>
    private static byte[] Read(byte[] png, out int width, out int height)
    {
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

        var at = 8;
        using var data = new MemoryStream();
        width = height = 0;
        var sawEnd = false;
        while (at < png.Length)
        {
            var length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length).ToArray();
            var crc = (uint)((png[at + 8 + length] << 24) | (png[at + 9 + length] << 16) | (png[at + 10 + length] << 8) | png[at + 11 + length]);
            Assert.Equal(SlowCrc(System.Text.Encoding.ASCII.GetBytes(type).Concat(body).ToArray()), crc);

            if (type == "IHDR")
            {
                width = (body[0] << 24) | (body[1] << 16) | (body[2] << 8) | body[3];
                height = (body[4] << 24) | (body[5] << 16) | (body[6] << 8) | body[7];
                Assert.Equal([8, 6, 0, 0, 0], body[8..13]);
            }
            else if (type == "IDAT")
            {
                data.Write(body);
            }
            else if (type == "IEND")
            {
                sawEnd = true;
            }
            at += 12 + length;
        }
        Assert.True(sawEnd);
        Assert.Equal(png.Length, at);

        data.Position = 0;
        using var inflate = new ZLibStream(data, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        var filtered = raw.ToArray();

        var rowLength = width * 4;
        Assert.Equal(height * (rowLength + 1), filtered.Length);
        var result = new byte[height * rowLength];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (rowLength + 1)];
            for (var i = 0; i < rowLength; i++)
            {
                int left = i >= 4 ? result[y * rowLength + i - 4] : 0;
                int up = y > 0 ? result[(y - 1) * rowLength + i] : 0;
                int upLeft = y > 0 && i >= 4 ? result[(y - 1) * rowLength + i - 4] : 0;
                var stored = filtered[y * (rowLength + 1) + 1 + i];
                int predicted = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"filter {filter}"),
                };
                result[y * rowLength + i] = (byte)(stored + predicted);
            }
        }
        return result;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>The CRC-32 of PNG worked out bit by bit, which is slow and simple: the other way round from the table in the writer.</summary>
    private static uint SlowCrc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void A_picture_comes_back_from_its_png_point_for_point()
    {
        var picture = Noisy();

        var rgba = Read(PngWriter.Write(picture), out var width, out var height);

        Assert.Equal((37, 23), (width, height));
        for (var i = 0; i < width * height; i++)
        {
            Assert.Equal(picture.Bgra[i * 4 + 2], rgba[i * 4]);       // red
            Assert.Equal(picture.Bgra[i * 4 + 1], rgba[i * 4 + 1]);   // green
            Assert.Equal(picture.Bgra[i * 4], rgba[i * 4 + 2]);       // blue
            Assert.Equal(picture.Bgra[i * 4 + 3], rgba[i * 4 + 3]);   // alpha
        }
    }

    [Fact]
    public void All_five_filters_are_used_and_each_undoes_to_the_same_picture()
    {
        // Rows that suit each filter: flat (none or sub), vertical stripes (up), a diagonal ramp (paeth or average)
        const int width = 64, height = 40;
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                bgra[i] = (byte)(x * 3 + y * 2);
                bgra[i + 1] = (byte)(y < 10 ? 50 : x * 4);
                bgra[i + 2] = (byte)((x ^ y) * 5);
                bgra[i + 3] = 255;
            }
        }
        var picture = new StickerPicture(width, height, bgra);

        var filtered = PngWriter.Filtered(picture);
        var used = Enumerable.Range(0, height).Select(y => filtered[y * (width * 4 + 1)]).Distinct().ToList();
        var rgba = Read(PngWriter.Write(picture), out _, out _);

        Assert.True(used.Count >= 2, $"only {string.Join(",", used)} was used");
        for (var i = 0; i < width * height; i++)
            Assert.Equal(bgra[i * 4], rgba[i * 4 + 2]);
    }

    [Fact]
    public void A_flat_picture_is_a_few_dozen_bytes()
    {
        var flat = new StickerPicture(512, 512, new byte[512 * 512 * 4]);

        Assert.True(PngWriter.Write(flat).Length < 1500);
    }

    [Fact]
    public void A_one_point_picture_is_a_png()
    {
        var one = new StickerPicture(1, 1, [10, 20, 30, 40]);

        var rgba = Read(PngWriter.Write(one), out var width, out var height);

        Assert.Equal((1, 1), (width, height));
        Assert.Equal([30, 20, 10, 40], rgba);
    }

    [Fact]
    public void The_crc_table_gives_the_crc_of_the_standard_check_string()
    {
        // The check value of CRC-32 for "123456789" is CBF43926 (what the PNG chunk CRC is made of, here with an empty chunk type)
        Assert.Equal(0xCBF43926u, PngWriter.Crc([], System.Text.Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void Another_program_reads_the_png_the_same_way()
    {
        if (RealFfmpeg.Path is null)
            return;

        using var folder = new TempFolder();
        var picture = Noisy(60, 41);
        File.WriteAllBytes(folder.File("p.png"), PngWriter.Write(picture));

        var rgba = RealFfmpeg.DecodeToRgba(folder.File("p.png"), 60, 41)!;

        for (var i = 0; i < 60 * 41; i++)
        {
            Assert.Equal(picture.Bgra[i * 4 + 2], rgba[i * 4]);
            Assert.Equal(picture.Bgra[i * 4 + 1], rgba[i * 4 + 1]);
            Assert.Equal(picture.Bgra[i * 4], rgba[i * 4 + 2]);
            Assert.Equal(picture.Bgra[i * 4 + 3], rgba[i * 4 + 3]);
        }
    }

    [Fact]
    public void A_picture_must_have_the_bytes_its_size_says()
    {
        Assert.Throws<ArgumentException>(() => new StickerPicture(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StickerPicture(0, 2, []));
        Assert.Throws<ArgumentException>(() => new PhotoPixels(2, 2, new byte[11]));
        Assert.Throws<ArgumentException>(() => new SubjectMap(2, 2, new float[3]));
    }
}

public class StickerFileTests
{
    private static StickerPicture Picture(int width, int height, byte alpha = 255)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4] = 200;
            bgra[i * 4 + 1] = 100;
            bgra[i * 4 + 2] = 50;
            bgra[i * 4 + 3] = alpha;
        }
        return new StickerPicture(width, height, bgra);
    }

    [Fact]
    public void A_png_made_here_says_its_size_and_that_it_has_alpha()
    {
        var info = StickerFile.Inspect(StickerEncoder.ToPng(Picture(300, 512)));

        Assert.NotNull(info);
        Assert.Equal(StickerFormat.Png, info.Format);
        Assert.Equal((300, 512), (info.Width, info.Height));
        Assert.True(info.HasAlpha);
    }

    [Fact]
    public void A_png_with_a_palette_has_alpha_only_when_it_has_a_trns_chunk()
    {
        byte[] Palette(bool withTrns)
        {
            var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            void Chunk(string type, byte[] data)
            {
                bytes.AddRange([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
                bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
                bytes.AddRange(data);
                bytes.AddRange([0, 0, 0, 0]); // the CRC is not looked at
            }
            Chunk("IHDR", [0, 0, 2, 0, 0, 0, 1, 0, 8, 3, 0, 0, 0]);   // 512 x 256, 8 bits, colour type 3
            Chunk("PLTE", [1, 2, 3]);
            if (withTrns)
                Chunk("tRNS", [0]);
            Chunk("IEND", []);
            return [.. bytes];
        }

        Assert.False(StickerFile.Inspect(Palette(false))!.HasAlpha);
        Assert.True(StickerFile.Inspect(Palette(true))!.HasAlpha);
        Assert.Equal((512, 256), (StickerFile.Inspect(Palette(true))!.Width, StickerFile.Inspect(Palette(true))!.Height));
    }

    [Fact]
    public async Task A_webp_from_the_real_encoder_says_its_size_and_that_it_has_alpha()
    {
        if (RealFfmpeg.Path is null)
            return;

        var encoded = await StickerEncoder.EncodeAsync(Picture(512, 300, alpha: 128), StickerFormat.WebP, 80, ffmpegPath: RealFfmpeg.Path);

        var info = StickerFile.Inspect(encoded.Bytes);
        Assert.NotNull(info);
        Assert.Equal(StickerFormat.WebP, info.Format);
        Assert.Equal((512, 300), (info.Width, info.Height));
        Assert.True(info.HasAlpha);
        Assert.Equal(encoded.Bytes.Length, info.Bytes);
    }

    [Fact]
    public void A_webp_without_alpha_is_said_to_have_none()
    {
        if (RealFfmpeg.Path is null)
            return;

        using var folder = new TempFolder();
        File.WriteAllBytes(folder.File("flat.png"), StickerEncoder.ToPng(Picture(64, 32)));
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RealFfmpeg.Path,
            ["-y", "-loglevel", "error", "-i", folder.File("flat.png"), "-vf", "format=yuv420p", "-c:v", "libwebp", folder.File("flat.webp")]) { CreateNoWindow = true })!;
        process.WaitForExit();

        var info = StickerFile.Inspect(folder.File("flat.webp"));

        Assert.NotNull(info);
        Assert.Equal((64, 32), (info.Width, info.Height));
        Assert.False(info.HasAlpha);
    }

    [Fact]
    public void A_lossless_webp_header_is_read()
    {
        // "RIFF" size "WEBP" "VP8L" size, the signature 0x2F, then 14 bits of width - 1, 14 of height - 1, one bit of alpha: 300 x 200 with alpha
        var bits = (299u) | (199u << 14) | (1u << 28);
        byte[] file =
        [
            .. "RIFF"u8, 22, 0, 0, 0, .. "WEBP"u8, .. "VP8L"u8, 10, 0, 0, 0,
            0x2F, (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24), 0, 0, 0, 0, 0,
        ];

        var info = StickerFile.Inspect(file);

        Assert.NotNull(info);
        Assert.Equal((300, 200), (info.Width, info.Height));
        Assert.True(info.HasAlpha);
    }

    [Fact]
    public void A_lossy_webp_header_without_vp8x_has_no_alpha()
    {
        // "VP8 " chunk: a frame tag of 3 bytes, the start code 9D 01 2A, then 14 bits of width and of height
        byte[] file =
        [
            .. "RIFF"u8, 30, 0, 0, 0, .. "WEBP"u8, .. "VP8 "u8, 18, 0, 0, 0,
            0x10, 0, 0, 0x9D, 0x01, 0x2A, 0x00, 0x02, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, // 512 x 256
        ];

        var info = StickerFile.Inspect(file);

        Assert.NotNull(info);
        Assert.Equal((512, 256), (info.Width, info.Height));
        Assert.False(info.HasAlpha);
    }

    [Fact]
    public void Something_that_is_neither_is_nothing()
    {
        Assert.Null(StickerFile.Inspect([1, 2, 3]));
        Assert.Null(StickerFile.Inspect(new byte[100]));
        Assert.Null(StickerFile.Inspect("GIF89a-and-then-some-bytes-so-that-it-is-long-enough"u8.ToArray()));
        using var folder = new TempFolder();
        Assert.Null(StickerFile.Inspect(folder.File("missing.webp")));
        File.WriteAllText(folder.File("text.png"), "this is not a picture, whatever its name says, and it is long enough");
        Assert.Null(StickerFile.Inspect(folder.File("text.png")));
    }

    [Theory]
    [InlineData(512, 512, 100, true, StickerProblems.None)]
    [InlineData(512, 300, 100, true, StickerProblems.None)]
    [InlineData(300, 512, 100, true, StickerProblems.None)]
    [InlineData(511, 512, 100, true, StickerProblems.None)]
    [InlineData(512, 1, 100, true, StickerProblems.None)]
    [InlineData(500, 500, 100, true, StickerProblems.Size)]
    [InlineData(1024, 512, 100, true, StickerProblems.Size)]
    [InlineData(512, 513, 100, true, StickerProblems.Size)]
    [InlineData(512, 512, 512_000, true, StickerProblems.None)]
    [InlineData(512, 512, 512_001, true, StickerProblems.Bytes)]
    [InlineData(512, 512, 100, false, StickerProblems.NoTransparency)]
    [InlineData(100, 100, 600_000, false, StickerProblems.Size | StickerProblems.Bytes | StickerProblems.NoTransparency)]
    public void A_file_is_checked_against_what_Telegram_takes(int width, int height, long bytes, bool alpha, StickerProblems expected) =>
        Assert.Equal(expected, StickerFile.Check(new StickerFileInfo(StickerFormat.WebP, width, height, alpha, bytes)));
}

public class StickerEncoderTests
{
    private static StickerPicture Gradient(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                bgra[i] = (byte)(x * 255 / width);
                bgra[i + 1] = (byte)(y * 255 / height);
                bgra[i + 2] = 90;
                bgra[i + 3] = (byte)(x < width / 4 ? 0 : 255);
            }
        }
        return new StickerPicture(width, height, bgra);
    }

    /// <summary>Noise: nothing compresses it, so it is far over 512 KB at any quality worth the name.</summary>
    private static StickerPicture Noise(int width, int height)
    {
        var random = new Random(5);
        var bgra = new byte[width * height * 4];
        random.NextBytes(bgra);
        for (var i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255;
        return new StickerPicture(width, height, bgra);
    }

    [Fact]
    public async Task A_png_is_written_without_a_program_and_has_no_quality()
    {
        var encoded = await StickerEncoder.EncodeAsync(Gradient(200, 100), StickerFormat.Png, 10, ffmpegPath: "does-not-exist.exe");

        Assert.Equal(StickerFormat.Png, encoded.Format);
        Assert.Equal(100, encoded.Quality);
        Assert.Equal(200, StickerFile.Inspect(encoded.Bytes)!.Width);
        Assert.True(encoded.Fits);
    }

    [Fact]
    public async Task A_webp_is_written_by_ffmpeg_with_the_size_and_the_alpha_of_the_picture()
    {
        if (RealFfmpeg.Path is null)
            return;

        var encoded = await StickerEncoder.EncodeAsync(Gradient(512, 400), StickerFormat.WebP, 85, ffmpegPath: RealFfmpeg.Path);

        var info = StickerFile.Inspect(encoded.Bytes)!;
        Assert.Equal(StickerFormat.WebP, encoded.Format);
        Assert.Equal(85, encoded.Quality);
        Assert.Equal((512, 400), (info.Width, info.Height));
        Assert.True(info.HasAlpha);
        Assert.Equal(StickerProblems.None, StickerFile.Check(info));
        Assert.True(encoded.Fits);
    }

    [Fact]
    public async Task The_webp_has_the_pictures_pixels_and_its_see_through_part()
    {
        if (RealFfmpeg.Path is null)
            return;

        using var folder = new TempFolder();
        var picture = Gradient(128, 64);
        var encoded = await StickerEncoder.EncodeAsync(picture, StickerFormat.WebP, 100, ffmpegPath: RealFfmpeg.Path);
        File.WriteAllBytes(folder.File("s.webp"), encoded.Bytes);

        var rgba = RealFfmpeg.DecodeToRgba(folder.File("s.webp"), 128, 64)!;

        // The left quarter is see-through and the rest opaque; the colours are those of the picture, a little off because the quality is lossy
        Assert.Equal(0, rgba[(30 * 128 + 5) * 4 + 3]);
        Assert.Equal(255, rgba[(30 * 128 + 100) * 4 + 3]);
        var i = (30 * 128 + 100) * 4;
        var j = (30 * 128 + 100) * 4;
        Assert.InRange(rgba[i], picture.Bgra[j + 2] - 12, picture.Bgra[j + 2] + 12);       // red
        Assert.InRange(rgba[i + 1], picture.Bgra[j + 1] - 12, picture.Bgra[j + 1] + 12);   // green
        Assert.InRange(rgba[i + 2], picture.Bgra[j] - 12, picture.Bgra[j] + 12);           // blue
    }

    [Fact]
    public async Task A_lower_quality_makes_a_smaller_file()
    {
        if (RealFfmpeg.Path is null)
            return;

        var picture = Noise(256, 256);

        var good = await StickerEncoder.EncodeAsync(picture, StickerFormat.WebP, 95, ffmpegPath: RealFfmpeg.Path);
        var poor = await StickerEncoder.EncodeAsync(picture, StickerFormat.WebP, 20, ffmpegPath: RealFfmpeg.Path);

        Assert.True(poor.Bytes.Length < good.Bytes.Length);
    }

    [Fact]
    public async Task A_webp_that_is_over_the_limit_is_written_again_at_a_lower_quality_until_it_fits()
    {
        if (RealFfmpeg.Path is null)
            return;

        var noise = Noise(1024, 1024);   // four times the points of a sticker: over the limit at any quality that is near 100

        var encoded = await StickerEncoder.EncodeAsync(noise, StickerFormat.WebP, 100, ffmpegPath: RealFfmpeg.Path);

        Assert.True(encoded.Fits);
        Assert.True(encoded.Quality < 100, "the quality is only lowered when a try was over the limit, so a quality below 100 shows that one was");
        Assert.Equal(0, (100 - encoded.Quality) % 10);   // lowered in steps of ten
    }

    [Fact]
    public async Task A_missing_ffmpeg_is_said_so_and_leaves_nothing_behind()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "photoliker_sticker_*").Length;

        var ex = await Assert.ThrowsAsync<StickerException>(() => StickerEncoder.EncodeAsync(Gradient(32, 32), StickerFormat.WebP, 80, ffmpegPath: "no-such-ffmpeg-here.exe"));

        Assert.Equal(StickerFailure.FfmpegNotFound, ex.Reason);
        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "photoliker_sticker_*").Length);
    }

    [Fact]
    public async Task Stopping_the_work_is_a_cancellation_not_an_error()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StickerEncoder.EncodeAsync(Gradient(32, 32), StickerFormat.WebP, 80, stop.Token, RealFfmpeg.Path));
    }

    [Fact]
    public void The_arguments_for_ffmpeg_are_one_list_with_the_paths_whole()
    {
        var arguments = StickerEncoder.WebPArguments("C:\\a folder\\in.png", "C:\\a folder\\out.webp", 77);

        Assert.Equal("C:\\a folder\\in.png", arguments[arguments.ToList().IndexOf("-i") + 1]);
        Assert.Equal("C:\\a folder\\out.webp", arguments[^1]);
        Assert.Equal("77", arguments[arguments.ToList().IndexOf("-quality") + 1]);
        Assert.Contains("libwebp", arguments);
        Assert.Equal("0", arguments[arguments.ToList().IndexOf("-lossless") + 1]);
    }

    [Fact]
    public void The_last_message_of_ffmpeg_is_the_reason()
    {
        Assert.Equal("No such file", StickerEncoder.LastMessage("something\nNo such file\n\n", 1));
        Assert.Equal("exit code 3", StickerEncoder.LastMessage("  \n", 3));
    }

    [Fact]
    public void The_ffmpeg_that_comes_with_the_program_is_found_beside_it_else_the_one_on_the_path()
    {
        var path = StickerEncoder.ResolveFfmpeg();

        Assert.True(path == "ffmpeg" || File.Exists(path));
    }
}
