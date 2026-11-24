namespace PhotoLiker.PhotoBook.Tests;

/// <summary>Two tiny JPEG files (a landscape of 64 x 48 and a portrait of 48 x 64) for the writers; the tests cannot draw any, this project has no picture library.</summary>
internal static class TestImages
{
    private const string Landscape =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAA0JCgsKCA0LCgsODg0PEyAVExISEyccHhcgLikxMC4pLSwzOko+MzZGNywtQFdBRkxOUlNSMj5aYVpQYEpRUk//2wBDAQ4ODhMREyYVFSZPNS01T09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT0//wAARCAAwAEADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwCjRRRXvHiBRVuG0yA0uR/s1YWCJRgRr+IzXm1s0o03yrU9jD5LiKseaVorz3MyitNoImGDGv4DFV5rTALRZP8As0Uc0o1HyvQMRkuIpR5o2kvLcqUUUV6R44VYskDSFiAQv86r1YsnCyFSQA3865cdzfV5cm/9X/A7st5PrUOfa/8Aw34l+iiivkT7sKKKKAKF6gWQMAAG/nVerF64aQKCCF/nVevrsDzfV48+/wDVvwPhMy5PrU+Ta/8Aw/4hRRRXUcJbhu8ALLk/7VWFniYZEi/icVmUV5tbK6NR8y0PYw+dYilHllaS89zTaeJRkyL+BzVea7yCsWR/tVUooo5XRpvmeoYjOsRVjyxtFeW4UUUV6R45/9k=";

    private const string Portrait =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAA0JCgsKCA0LCgsODg0PEyAVExISEyccHhcgLikxMC4pLSwzOko+MzZGNywtQFdBRkxOUlNSMj5aYVpQYEpRUk//2wBDAQ4ODhMREyYVFSZPNS01T09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT0//wAARCABAADADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwCOiiiuU8YKK17XQbiZA80iwhhkDGWH1FF1oNxCheGRZgoyRjDH6CsPrFK/Lc3+rVeXm5TIooorcwCtzw/YK+buZM7TiMH17n/PvWHXZaXGItNt1UnBQNz78/1rkxlRxp2XU7MDTU6l30LVFFFeOe0c/wCILBUxdwpjccSAevY/59qw67LVIxLptwrE4CFuPbn+lcbXsYOo5U7PoeLjqahUuuoV2WlyCXTbdlBwEC8+3H9K42tzw/fqmbSZ8bjmMn17j/PvRjKblTuugYGooVLPqdBRRRXjntFXVJBFptwzA4KFePfj+tcbW54gv1fFpC+dpzIR69h/n2rDr2MHTcad31PFx1RTqWXQKKKK6zjNe1164hQJNGswUYBzhj9TRda9cTIUhjWEMME5yw+hrIorD6vSvzWN/rNXl5eYKKKK3MD/2Q==";

    public static byte[] LandscapeJpeg() => Convert.FromBase64String(Landscape);

    public static byte[] PortraitJpeg() => Convert.FromBase64String(Portrait);

    /// <summary>A PNG with a see-through background (the left half is red and solid, the right half is see-through), as a picture cut out of a photo is; the tests have no picture library to make one.</summary>
    public static byte[] SeeThroughPng(int width = 40, int height = 30)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // no filter on this row
            for (var x = 0; x < width; x++)
                raw.Write([200, 30, 30, x < width / 2 ? (byte)255 : (byte)0]);
        }

        using var packed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw.ToArray());

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", [.. BigEndian(width), .. BigEndian(height), 8, 6, 0, 0, 0]); // 8 bits, colour with alpha
        Chunk(png, "IDAT", packed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(BigEndian(data.Length));
        png.Write(typed);
        var crc = 0xFFFFFFFFu;
        foreach (var b in typed)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        png.Write(BigEndian((int)~crc));
    }

    public const int LandscapeWidth = 64, LandscapeHeight = 48, PortraitWidth = 48, PortraitHeight = 64;
}
