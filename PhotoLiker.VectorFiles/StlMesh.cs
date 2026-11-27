namespace PhotoLiker.VectorFiles
{
    /// <summary>How an STL file was written: the binary form (80 bytes of header, a count, 50 bytes a triangle) or the text form ("solid ... facet ... vertex ...").</summary>
    public enum StlFormat
    {
        Binary,
        Ascii,
    }

    /// <summary>The box around a mesh, as the extremes of its points.</summary>
    public readonly record struct MeshBounds(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
    {
        public float SizeX => MaxX - MinX;
        public float SizeY => MaxY - MinY;
        public float SizeZ => MaxZ - MinZ;

        public float CenterX => (MinX + MaxX) / 2;
        public float CenterY => (MinY + MaxY) / 2;
        public float CenterZ => (MinZ + MaxZ) / 2;

        /// <summary>Half the diagonal of the box: the radius of the ball that holds the whole mesh, whatever way it is turned.</summary>
        public double Radius => Math.Sqrt((double)SizeX * SizeX + (double)SizeY * SizeY + (double)SizeZ * SizeZ) / 2;
    }

    /// <summary>
    /// A 3D model as the triangles of its surface. The corners are kept as they were written, nine numbers a triangle (x, y, z of three corners), with no corner shared between
    /// two triangles: the normals of the file are not kept (many programs write zeros or wrong ones), they are worked out from the corners when the model is drawn.
    /// Nothing in it changes after it is made, so a thread may draw it while another reads its facts.
    /// </summary>
    public sealed class StlMesh
    {
        private readonly Lazy<(double Area, double Volume)> _measures;

        /// <param name="vertices">Nine numbers for each of the <paramref name="triangleCount"/> triangles; the array may be longer, the rest is not used. Every number is finite.</param>
        public StlMesh(float[] vertices, int triangleCount, string name, StlFormat format, int skippedTriangles = 0)
        {
            ArgumentNullException.ThrowIfNull(vertices);
            if (triangleCount < 0 || (long)triangleCount * 9 > vertices.Length)
                throw new ArgumentOutOfRangeException(nameof(triangleCount));

            Vertices = vertices;
            TriangleCount = triangleCount;
            Name = name ?? string.Empty;
            Format = format;
            SkippedTriangles = skippedTriangles;
            Bounds = Measure(vertices, triangleCount);
            _measures = new Lazy<(double, double)>(() => Integrate(Vertices, TriangleCount, Bounds), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>Nine numbers a triangle: x, y, z of the first corner, then of the second, then of the third.</summary>
        public float[] Vertices { get; }

        public int TriangleCount { get; }

        /// <summary>What the file calls the model (the name after "solid", or the text of the header of a binary file); empty when it says nothing.</summary>
        public string Name { get; }

        public StlFormat Format { get; }

        /// <summary>How many triangles of the file were left out because a corner was not a number (or the file stopped in the middle of one).</summary>
        public int SkippedTriangles { get; }

        public MeshBounds Bounds { get; }

        /// <summary>What the corners take in memory, for a cache that has a budget.</summary>
        public long Bytes => (long)TriangleCount * 9 * sizeof(float);

        /// <summary>The area of all the triangles added up, in the square of the unit of the file.</summary>
        public double SurfaceArea => _measures.Value.Area;

        /// <summary>
        /// What the surface holds, in the cube of the unit of the file: the sum of the little pyramids from the middle of the box to each triangle, which is the volume
        /// of a closed surface whichever way its triangles face. Of a surface that has holes it only says how much the part that is there encloses.
        /// </summary>
        public double Volume => Math.Abs(_measures.Value.Volume);

        private static MeshBounds Measure(float[] vertices, int triangleCount)
        {
            if (triangleCount == 0)
                return default;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            var end = triangleCount * 9;
            for (var i = 0; i < end; i += 3)
            {
                var x = vertices[i];
                var y = vertices[i + 1];
                var z = vertices[i + 2];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }
            return new MeshBounds(minX, minY, minZ, maxX, maxY, maxZ);
        }

        private static (double Area, double Volume) Integrate(float[] v, int triangleCount, MeshBounds bounds)
        {
            // From the middle of the box, so that a model far from the origin does not lose its digits in the big numbers that cancel
            double cx = bounds.CenterX, cy = bounds.CenterY, cz = bounds.CenterZ;
            double area = 0, volume = 0;
            for (var t = 0; t < triangleCount; t++)
            {
                var i = t * 9;
                double ax = v[i] - cx, ay = v[i + 1] - cy, az = v[i + 2] - cz;
                double bx = v[i + 3] - cx, by = v[i + 4] - cy, bz = v[i + 5] - cz;
                double dx = v[i + 6] - cx, dy = v[i + 7] - cy, dz = v[i + 8] - cz;

                // (b - a) x (d - a): twice the area, and the way the triangle faces
                double ex = bx - ax, ey = by - ay, ez = bz - az;
                double fx = dx - ax, fy = dy - ay, fz = dz - az;
                double nx = ey * fz - ez * fy;
                double ny = ez * fx - ex * fz;
                double nz = ex * fy - ey * fx;
                area += Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2;

                // a . (b x d) / 6: the pyramid from the centre to the triangle, counted with a sign (a triangle that faces away takes some back)
                volume += (ax * (by * dz - bz * dy) + ay * (bz * dx - bx * dz) + az * (bx * dy - by * dx)) / 6;
            }
            return (area, volume);
        }
    }
}
