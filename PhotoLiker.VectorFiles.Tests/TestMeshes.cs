using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PhotoLiker.VectorFiles.Tests;

/// <summary>Triangles for the tests, as the nine numbers a triangle has in <see cref="StlMesh.Vertices"/>, and the bytes of STL files made of them.</summary>
internal static class TestMeshes
{
    public static float[] Triangle(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz) =>
        [ax, ay, az, bx, by, bz, cx, cy, cz];

    /// <summary>The twelve triangles of a box, every one facing outwards (counter-clockwise seen from outside).</summary>
    public static float[] Box(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        float[] v000 = [x0, y0, z0], v100 = [x1, y0, z0], v110 = [x1, y1, z0], v010 = [x0, y1, z0];
        float[] v001 = [x0, y0, z1], v101 = [x1, y0, z1], v111 = [x1, y1, z1], v011 = [x0, y1, z1];
        return Join(
            Quad(v000, v010, v110, v100), // bottom (faces -Z)
            Quad(v001, v101, v111, v011), // top
            Quad(v000, v100, v101, v001), // front (faces -Y)
            Quad(v010, v011, v111, v110), // back
            Quad(v000, v001, v011, v010), // left (faces -X)
            Quad(v100, v110, v111, v101)); // right
    }

    public static float[] Quad(float[] a, float[] b, float[] c, float[] d) => [.. a, .. b, .. c, .. a, .. c, .. d];

    public static float[] Join(params float[][] parts) => [.. parts.SelectMany(p => p)];

    /// <summary>The same triangles turned inside out.</summary>
    public static float[] Reversed(float[] triangles)
    {
        var copy = (float[])triangles.Clone();
        for (var i = 0; i < copy.Length; i += 9)
            for (var k = 0; k < 3; k++)
                (copy[i + 3 + k], copy[i + 6 + k]) = (copy[i + 6 + k], copy[i + 3 + k]);
        return copy;
    }

    /// <summary>A ball of the given radius round the origin, made of many small triangles.</summary>
    public static float[] Sphere(float radius, int rings, int segments)
    {
        float[] Point(int ring, int segment)
        {
            var polar = Math.PI * ring / rings;
            var azimuth = 2 * Math.PI * segment / segments;
            return [(float)(radius * Math.Sin(polar) * Math.Cos(azimuth)), (float)(radius * Math.Sin(polar) * Math.Sin(azimuth)), (float)(radius * Math.Cos(polar))];
        }

        var triangles = new List<float>();
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < segments; s++)
            {
                float[] a = Point(r, s), b = Point(r + 1, s), c = Point(r + 1, s + 1), d = Point(r, s + 1);
                if (r > 0) triangles.AddRange([.. a, .. d, .. b]);
                if (r < rings - 1) triangles.AddRange([.. d, .. c, .. b]);
            }
        }
        return [.. triangles];
    }

    public static StlMesh Mesh(float[] triangles, string name = "") => new(triangles, triangles.Length / 9, name, StlFormat.Binary);

    // ── the bytes of files ─────────────────────────────────────────────

    public static byte[] BinaryFile(float[] triangles, string header = "", uint? declaredCount = null, int trailing = 0)
    {
        var count = triangles.Length / 9;
        var bytes = new byte[84 + 50 * count + trailing];
        Encoding.ASCII.GetBytes(header, bytes.AsSpan(0, Math.Min(80, header.Length)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), declaredCount ?? (uint)count);
        for (var t = 0; t < count; t++)
        {
            var at = 84 + 50 * t + 12; // the normal before is left as zeros
            for (var k = 0; k < 9; k++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4 * k), triangles[t * 9 + k]);
        }
        return bytes;
    }

    public static string AsciiFile(float[] triangles, string name = "part", string newline = "\n", bool upper = false)
    {
        string Word(string text) => upper ? text.ToUpperInvariant() : text;
        var text = new StringBuilder();
        text.Append(Word("solid ")).Append(name).Append(newline);
        for (var t = 0; t < triangles.Length / 9; t++)
        {
            text.Append("  ").Append(Word("facet normal 0 0 0")).Append(newline);
            text.Append("    ").Append(Word("outer loop")).Append(newline);
            for (var k = 0; k < 3; k++)
            {
                text.Append("      ").Append(Word("vertex "));
                text.Append(string.Join(' ', Enumerable.Range(0, 3).Select(i => triangles[t * 9 + k * 3 + i].ToString("G9", CultureInfo.InvariantCulture)))).Append(newline);
            }
            text.Append("    ").Append(Word("endloop")).Append(newline);
            text.Append("  ").Append(Word("endfacet")).Append(newline);
        }
        text.Append(Word("endsolid ")).Append(name).Append(newline);
        return text.ToString();
    }

    // ── looking at pictures ────────────────────────────────────────────

    /// <summary>The smallest box (left, top, right, bottom, inclusive) that holds every pixel that is not the background; null when the picture is only background.</summary>
    public static (int Left, int Top, int Right, int Bottom)? Silhouette(RenderedPicture picture, int background)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                if (picture.ColorAt(x, y) == background)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? null : (left, top, right, bottom);
    }

    public static int Covered(RenderedPicture picture, int background)
    {
        var count = 0;
        for (var y = 0; y < picture.Height; y++)
            for (var x = 0; x < picture.Width; x++)
                if (picture.ColorAt(x, y) != background)
                    count++;
        return count;
    }

    public static int CoveredInColumn(RenderedPicture picture, int x, int background)
    {
        var count = 0;
        for (var y = 0; y < picture.Height; y++)
            if (picture.ColorAt(x, y) != background)
                count++;
        return count;
    }

    public static int CoveredInRow(RenderedPicture picture, int y, int background)
    {
        var count = 0;
        for (var x = 0; x < picture.Width; x++)
            if (picture.ColorAt(x, y) != background)
                count++;
        return count;
    }

    public static int Brightness(int rgb) => ((rgb >> 16) & 0xFF) + ((rgb >> 8) & 0xFF) + (rgb & 0xFF);
}
