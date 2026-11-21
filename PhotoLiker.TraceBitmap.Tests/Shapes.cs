namespace PhotoLiker.TraceBitmap.Tests;

/// <summary>Pictures drawn in memory for the tests, and a way to put a trace back into pixels so that it can be compared with what was traced.</summary>
internal static class Shapes
{
    /// <summary>A picture of a background colour with shapes drawn on it by a function that says, for the centre of a pixel, which colour it is (null: the background).</summary>
    public static PixelImage Draw(int width, int height, (byte R, byte G, byte B) background, Func<double, double, (byte R, byte G, byte B)?> shape)
    {
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = shape(x + 0.5, y + 0.5) ?? background;
                var i = (y * width + x) * 3;
                bgr[i] = b;
                bgr[i + 1] = g;
                bgr[i + 2] = r;
            }
        }

        return new PixelImage(width, height, bgr);
    }

    public static (byte R, byte G, byte B) Rgb(int r, int g, int b) => ((byte)r, (byte)g, (byte)b);

    public static readonly (byte R, byte G, byte B) White = (255, 255, 255);
    public static readonly (byte R, byte G, byte B) Black = (0, 0, 0);

    /// <summary>A black disc on white.</summary>
    public static PixelImage Disc(int size, double radius) =>
        Draw(size, size, White, (x, y) => Math.Sqrt((x - size / 2.0) * (x - size / 2.0) + (y - size / 2.0) * (y - size / 2.0)) < radius ? Black : null);

    /// <summary>A black ring (a disc with a hole) on white.</summary>
    public static PixelImage Ring(int size, double outer, double inner) =>
        Draw(size, size, White, (x, y) =>
        {
            var d = Math.Sqrt((x - size / 2.0) * (x - size / 2.0) + (y - size / 2.0) * (y - size / 2.0));
            return d < outer && d >= inner ? Black : null;
        });

    /// <summary>A black rectangle (whole pixels) on white.</summary>
    public static PixelImage Rectangle(int width, int height, int left, int top, int right, int bottom) =>
        Draw(width, height, White, (x, y) => x >= left && x < right && y >= top && y < bottom ? Black : null);

    /// <summary>The black pixels of a picture, as a Bilevel picture, the way the tracer reads brightness (below the cutoff is black).</summary>
    public static Bilevel BlackOf(PixelImage image, double cutoff = 0.45) =>
        Binarizer.BrightnessCutoff(Binarizer.Luminance(image), image.Width, image.Height, cutoff);

    /// <summary>
    /// The trace of one layer put back into pixels: a pixel is filled when its centre is inside an odd number of outlines (the even-odd rule the files are written with). The curves are cut into
    /// pieces a hundredth of a pixel from them.
    /// </summary>
    public static Bilevel Fill(TraceResult trace, int layer)
    {
        var result = new Bilevel(trace.Width, trace.Height);
        var outlines = trace.Layers[layer].Paths.Select(path => DxfWriter.Flatten(path, 1, 1, 0.01)).ToList();
        for (var y = 0; y < trace.Height; y++)
        {
            var crossings = new List<double>();
            var yCentre = y + 0.5;
            foreach (var outline in outlines)
            {
                for (var i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    if ((a.Y <= yCentre && b.Y > yCentre) || (b.Y <= yCentre && a.Y > yCentre))
                        crossings.Add(a.X + (yCentre - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
            }

            crossings.Sort();
            for (var c = 0; c + 1 < crossings.Count; c += 2)
            {
                for (var x = (int)Math.Ceiling(crossings[c] - 0.5); x < crossings[c + 1] - 0.5 + 1e-9 && x < trace.Width; x++)
                {
                    if (x >= 0 && x + 0.5 >= crossings[c] && x + 0.5 < crossings[c + 1])
                        result[x, y] = true;
                }
            }
        }

        return result;
    }

    /// <summary>How alike two pictures are: the pixels that are black in both over the pixels that are black in either (1 for the same picture).</summary>
    public static double Overlap(Bilevel a, Bilevel b)
    {
        double both = 0, either = 0;
        for (var i = 0; i < a.Pixels.Length; i++)
        {
            if (a.Pixels[i] == 1 && b.Pixels[i] == 1)
                both++;
            if (a.Pixels[i] == 1 || b.Pixels[i] == 1)
                either++;
        }

        return either == 0 ? 1 : both / either;
    }
}
