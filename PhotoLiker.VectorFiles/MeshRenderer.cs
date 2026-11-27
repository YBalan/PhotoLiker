namespace PhotoLiker.VectorFiles
{
    /// <summary>
    /// Draws a model as a picture, on the processor (the computer may have no graphics card to speak of, and a thumbnail should not need one): every triangle is turned and projected
    /// straight onto the picture (an orthographic camera, so that the sizes of a part can be compared at a glance), lit by two lights and the viewer's own, and put in a depth buffer, so
    /// that what is nearer hides what is behind. A triangle is lit on both sides: many files have some triangles turned the wrong way, and they should not turn into holes.
    /// The work is split in bands of rows that are drawn at the same time, so a model of a million triangles takes a fraction of a second.
    /// </summary>
    public static class MeshRenderer
    {
        private const int ChunkSize = 8192;
        private const int MaxWireTriangles = 200_000;
        private const long MaxDrawnPixels = 16_000_000;

        // Directions to the lights, in the picture's own axes (x right, y up, z towards the viewer), and how much each lights a face that looks straight at it
        private static readonly (double X, double Y, double Z) KeyLight = Unit(-0.35, 0.55, 0.76);
        private static readonly (double X, double Y, double Z) FillLight = Unit(0.70, -0.20, 0.45);
        private const double Ambient = 0.22;
        private const double KeyShare = 0.50;
        private const double FillShare = 0.22;
        private const double HeadShare = 0.12;

        private struct Setup
        {
            public float X0, Y0, Z0, X1, Y1, Z1, X2, Y2, Z2;
            public uint Color;
            public int MinY, MaxY; // the rows the triangle can be on; MinY above MaxY: it is not drawn
        }

        /// <param name="maxTriangles">
        /// With more triangles than this only every n-th is drawn (0: all of them). For the picture that follows the mouse while the model is turned, where speed matters more than every
        /// little triangle: a model that is dense enough looks the same, a coarse one gets gaps.
        /// </param>
        public static RenderedPicture Render(StlMesh mesh, MeshView view, int width, int height, RenderStyle? style = null, int maxTriangles = 0, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            style ??= new RenderStyle();

            var factor = Math.Clamp(style.Supersample, 1, 4);
            while (factor > 1 && (long)width * factor * height * factor > MaxDrawnPixels)
                factor--;
            var w = width * factor;
            var h = height * factor;

            var colors = new uint[w * h];
            Array.Fill(colors, Pack(style.Background));
            var depth = new float[w * h];
            Array.Fill(depth, float.NegativeInfinity);

            var setups = Project(mesh, view, w, h, Pack(style.Surface), maxTriangles, cancel);
            Fill(setups, colors, depth, w, h, cancel);

            if (style.Wireframe)
                DrawEdges(setups, colors, depth, w, h, Pack(style.Edges), (float)(Math.Max(mesh.Bounds.Radius, 1e-9) * 0.004), cancel);

            return Resolve(colors, w, factor, width, height);
        }

        /// <summary>Turns every triangle (or every n-th) into what the drawing needs: its corners on the picture, how far from the viewer, and its colour.</summary>
        private static Setup[] Project(StlMesh mesh, MeshView view, int w, int h, uint surface, int maxTriangles, CancellationToken cancel)
        {
            var total = mesh.TriangleCount;
            var stride = maxTriangles > 0 && total > maxTriangles ? (int)Math.Ceiling(total / (double)maxTriangles) : 1;
            var count = (total + stride - 1) / stride;
            var setups = new Setup[count];
            if (count == 0)
                return setups;

            // The camera's axes in the model's: right, up, and towards the viewer (what is nearer has the greater number)
            var basis = view.Basis;
            double rx = basis.RightX, ry = basis.RightY;
            double dx = basis.ToX, dy = basis.ToY, dz = basis.ToZ;
            double ux = basis.UpX, uy = basis.UpY, uz = basis.UpZ;

            var bounds = mesh.Bounds;
            double cx = bounds.CenterX, cy = bounds.CenterY, cz = bounds.CenterZ;
            var radius = bounds.Radius > 0 ? bounds.Radius : 1;
            var shorter = Math.Min(w, h);
            var scale = 0.9 * shorter / (2 * radius) * Math.Clamp(view.Zoom, MeshView.MinZoom, MeshView.MaxZoom);
            var centerX = w / 2.0 + view.PanX * shorter;
            var centerY = h / 2.0 - view.PanY * shorter;

            double baseR = (surface >> 16) & 0xFF, baseG = (surface >> 8) & 0xFF, baseB = surface & 0xFF;
            var vertices = mesh.Vertices;
            var chunks = (count + ChunkSize - 1) / ChunkSize;

            Parallel.For(0, chunks, new ParallelOptions { CancellationToken = cancel }, chunk =>
            {
                var end = Math.Min(count, (chunk + 1) * ChunkSize);
                for (var n = chunk * ChunkSize; n < end; n++)
                {
                    var i = n * stride * 9;
                    ref var s = ref setups[n];

                    double ax = vertices[i] - cx, ay = vertices[i + 1] - cy, az = vertices[i + 2] - cz;
                    double bx = vertices[i + 3] - cx, by = vertices[i + 4] - cy, bz = vertices[i + 5] - cz;
                    double ex = vertices[i + 6] - cx, ey = vertices[i + 7] - cy, ez = vertices[i + 8] - cz;

                    // The way the face looks (not its length): from the two sides of the triangle that meet at the first corner
                    double px = bx - ax, py = by - ay, pz = bz - az;
                    double qx = ex - ax, qy = ey - ay, qz = ez - az;
                    double nx = py * qz - pz * qy, ny = pz * qx - px * qz, nz = px * qy - py * qx;
                    var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (!(length > 0))
                    {
                        s.MinY = int.MaxValue; // a triangle with no area: a line or a point
                        s.MaxY = int.MinValue;
                        continue;
                    }
                    nx /= length; ny /= length; nz /= length;

                    // The same in the camera's axes, and what the three lights make of it
                    double vx = nx * rx + ny * ry;
                    double vy = nx * ux + ny * uy + nz * uz;
                    double vz = nx * dx + ny * dy + nz * dz;
                    var light = Ambient
                        + KeyShare * Math.Abs(vx * KeyLight.X + vy * KeyLight.Y + vz * KeyLight.Z)
                        + FillShare * Math.Abs(vx * FillLight.X + vy * FillLight.Y + vz * FillLight.Z)
                        + HeadShare * Math.Abs(vz);
                    if (light > 1) light = 1;
                    s.Color = ((uint)(baseR * light) << 16) | ((uint)(baseG * light) << 8) | (uint)(baseB * light);

                    s.X0 = (float)(centerX + (ax * rx + ay * ry) * scale);
                    s.Y0 = (float)(centerY - (ax * ux + ay * uy + az * uz) * scale);
                    s.Z0 = (float)(ax * dx + ay * dy + az * dz);
                    s.X1 = (float)(centerX + (bx * rx + by * ry) * scale);
                    s.Y1 = (float)(centerY - (bx * ux + by * uy + bz * uz) * scale);
                    s.Z1 = (float)(bx * dx + by * dy + bz * dz);
                    s.X2 = (float)(centerX + (ex * rx + ey * ry) * scale);
                    s.Y2 = (float)(centerY - (ex * ux + ey * uy + ez * uz) * scale);
                    s.Z2 = (float)(ex * dx + ey * dy + ez * dz);

                    var low = Math.Floor(Math.Min(s.Y0, Math.Min(s.Y1, s.Y2)));
                    var high = Math.Ceiling(Math.Max(s.Y0, Math.Max(s.Y1, s.Y2)));
                    if (!(high >= 0) || !(low <= h - 1)) // off the picture (or not a number)
                    {
                        s.MinY = int.MaxValue;
                        s.MaxY = int.MinValue;
                        continue;
                    }
                    s.MinY = (int)Math.Max(low, 0);
                    s.MaxY = (int)Math.Min(high, h - 1);
                }
            });

            return setups;
        }

        /// <summary>Puts the triangles on the picture, band by band of rows: no two bands touch the same pixel, so they need no locks.</summary>
        private static void Fill(Setup[] setups, uint[] colors, float[] depth, int w, int h, CancellationToken cancel)
        {
            if (setups.Length == 0)
                return;

            var bands = Math.Clamp(Math.Min(Environment.ProcessorCount * 2, h / 8), 1, 64);
            var rows = (h + bands - 1) / bands;
            Parallel.For(0, bands, new ParallelOptions { CancellationToken = cancel }, band =>
            {
                var from = band * rows;
                var to = Math.Min(h, from + rows);
                if (from < to)
                    FillBand(setups, colors, depth, w, from, to);
            });
        }

        private static void FillBand(Setup[] setups, uint[] colors, float[] depth, int w, int rowFrom, int rowTo)
        {
            for (var n = 0; n < setups.Length; n++)
            {
                ref readonly var t = ref setups[n];
                if (t.MaxY < rowFrom || t.MinY >= rowTo)
                    continue;

                var area = (t.X1 - t.X0) * (t.Y2 - t.Y0) - (t.X2 - t.X0) * (t.Y1 - t.Y0);
                if (!(MathF.Abs(area) > 1e-12f))
                    continue;

                var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(t.X0, MathF.Min(t.X1, t.X2))));
                var maxX = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(t.X0, MathF.Max(t.X1, t.X2))));
                if (minX > maxX)
                    continue;

                var sign = area > 0 ? 1f : -1f;
                var inverse = 1f / area;
                float stepX0 = t.Y1 - t.Y2, stepX1 = t.Y2 - t.Y0, stepX2 = t.Y0 - t.Y1;

                var first = Math.Max(rowFrom, t.MinY);
                var last = Math.Min(rowTo - 1, t.MaxY);
                for (var y = first; y <= last; y++)
                {
                    // The three edge functions at the centre of the first pixel of the row; they grow by a fixed step for each pixel to the right
                    float px = minX + 0.5f, py = y + 0.5f;
                    var w0 = (t.X2 - t.X1) * (py - t.Y1) - (t.Y2 - t.Y1) * (px - t.X1);
                    var w1 = (t.X0 - t.X2) * (py - t.Y2) - (t.Y0 - t.Y2) * (px - t.X2);
                    var w2 = (t.X1 - t.X0) * (py - t.Y0) - (t.Y1 - t.Y0) * (px - t.X0);

                    var at = y * w + minX;
                    for (var x = minX; x <= maxX; x++, at++, w0 += stepX0, w1 += stepX1, w2 += stepX2)
                    {
                        if (w0 * sign < 0 || w1 * sign < 0 || w2 * sign < 0)
                            continue;

                        var z = (w0 * t.Z0 + w1 * t.Z1 + w2 * t.Z2) * inverse;
                        if (z > depth[at])
                        {
                            depth[at] = z;
                            colors[at] = t.Color;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The edges of the triangles that can be seen, over the surface: a line is drawn where it is not behind what the depth buffer holds. A model of more than <see cref="MaxWireTriangles"/>
        /// triangles gets the edges of every n-th one, which is all anybody can tell apart on a screen.
        /// </summary>
        private static void DrawEdges(Setup[] setups, uint[] colors, float[] depth, int w, int h, uint color, float bias, CancellationToken cancel)
        {
            var step = (setups.Length + MaxWireTriangles - 1) / MaxWireTriangles;
            for (var n = 0; n < setups.Length; n += step)
            {
                if ((n & 4095) == 0)
                    cancel.ThrowIfCancellationRequested();

                ref readonly var t = ref setups[n];
                if (t.MinY > t.MaxY)
                    continue;

                Line(colors, depth, w, h, t.X0, t.Y0, t.Z0, t.X1, t.Y1, t.Z1, bias, color);
                Line(colors, depth, w, h, t.X1, t.Y1, t.Z1, t.X2, t.Y2, t.Z2, bias, color);
                Line(colors, depth, w, h, t.X2, t.Y2, t.Z2, t.X0, t.Y0, t.Z0, bias, color);
            }
        }

        private static void Line(uint[] colors, float[] depth, int w, int h, float x0, float y0, float z0, float x1, float y1, float z1, float bias, uint color)
        {
            float dx = x1 - x0, dy = y1 - y0;
            var steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            if (steps > 8 * (w + h))
                return; // a line that runs far outside the picture: not worth walking

            steps = Math.Max(steps, 1);
            for (var s = 0; s <= steps; s++)
            {
                var f = s / (float)steps;
                var x = x0 + dx * f;
                var y = y0 + dy * f;
                if (!(x >= 0) || !(y >= 0) || x >= w || y >= h)
                    continue;

                var at = (int)y * w + (int)x;
                if (z0 + (z1 - z0) * f + bias < depth[at])
                    continue; // behind the surface

                var old = colors[at];
                var r = (((color >> 16) & 0xFF) * 11 + ((old >> 16) & 0xFF) * 9) / 20;
                var g = (((color >> 8) & 0xFF) * 11 + ((old >> 8) & 0xFF) * 9) / 20;
                var b = ((color & 0xFF) * 11 + (old & 0xFF) * 9) / 20;
                colors[at] = (r << 16) | (g << 8) | b;
            }
        }

        /// <summary>The finished picture: each pixel of it is the average of the factor-by-factor square of what was drawn.</summary>
        private static RenderedPicture Resolve(uint[] colors, int drawnWidth, int factor, int width, int height)
        {
            var bytes = new byte[width * height * 4];
            var area = factor * factor;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    uint r = 0, g = 0, b = 0;
                    for (var sy = 0; sy < factor; sy++)
                    {
                        var at = (y * factor + sy) * drawnWidth + x * factor;
                        for (var sx = 0; sx < factor; sx++, at++)
                        {
                            var c = colors[at];
                            r += (c >> 16) & 0xFF;
                            g += (c >> 8) & 0xFF;
                            b += c & 0xFF;
                        }
                    }

                    var o = (y * width + x) * 4;
                    bytes[o] = (byte)(b / area);
                    bytes[o + 1] = (byte)(g / area);
                    bytes[o + 2] = (byte)(r / area);
                    bytes[o + 3] = 255;
                }
            }
            return new RenderedPicture(width, height, bytes);
        }

        private static uint Pack(int rgb) => (uint)rgb & 0xFFFFFF;

        private static (double X, double Y, double Z) Unit(double x, double y, double z)
        {
            var length = Math.Sqrt(x * x + y * y + z * z);
            return (x / length, y / length, z / length);
        }
    }
}
