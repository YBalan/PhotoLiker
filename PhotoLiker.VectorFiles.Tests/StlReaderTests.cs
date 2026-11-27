using System.Text;

namespace PhotoLiker.VectorFiles.Tests;

public class StlReaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("stl-reader-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static readonly float[] Cube = TestMeshes.Box(0, 0, 0, 10, 10, 10);

    // ── binary files ───────────────────────────────────────────────────

    [Fact]
    public void ABinaryFile_IsReadTriangleForTriangle()
    {
        var triangles = TestMeshes.Join(Cube, TestMeshes.Triangle(1.5f, -2.25f, 3, 4, 5, -6.5f, 7, 8, 9));

        var mesh = StlReader.Read(TestMeshes.BinaryFile(triangles));

        Assert.Equal(StlFormat.Binary, mesh.Format);
        Assert.Equal(13, mesh.TriangleCount);
        Assert.Equal(triangles, mesh.Vertices.Take(13 * 9));
        Assert.Equal(0, mesh.SkippedTriangles);
    }

    [Fact]
    public void TheHeaderOfABinaryFile_IsItsNameWhenItIsText()
    {
        Assert.Equal("Bracket v3", StlReader.Read(TestMeshes.BinaryFile(Cube, "Bracket v3")).Name);
        Assert.Equal("Bracket", StlReader.Read(TestMeshes.BinaryFile(Cube, "solid Bracket")).Name);
        Assert.Equal(string.Empty, StlReader.Read(TestMeshes.BinaryFile(Cube)).Name);
    }

    [Fact]
    public void ABinaryFile_WhoseHeaderStartsWithSolid_IsStillBinary()
    {
        // Many CAD programs write "solid" into the header of a binary file; the exact size is what tells
        var mesh = StlReader.Read(TestMeshes.BinaryFile(Cube, "solid exported by something"));

        Assert.Equal(StlFormat.Binary, mesh.Format);
        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void ABinaryFile_WithSomethingAfterItsLastTriangle_IsStillBinary()
    {
        var mesh = StlReader.Read(TestMeshes.BinaryFile(Cube, trailing: 17));

        Assert.Equal(StlFormat.Binary, mesh.Format);
        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void ABinaryFile_ThatIsCutShort_IsRefused_NotShownAsGarbage()
    {
        // It says twelve triangles and holds seven and a bit: nothing tells it from a file of random bytes, so it is not guessed at
        var cut = TestMeshes.BinaryFile(Cube)[..(84 + 50 * 7 + 20)];

        Assert.Throws<InvalidDataException>(() => StlReader.Read(cut));
    }

    [Fact]
    public void ATriangle_WithACornerThatIsNotANumber_IsLeftOut_AndCounted()
    {
        var broken = TestMeshes.Join(Cube, TestMeshes.Triangle(float.NaN, 0, 0, 1, 1, 1, 2, 2, 2), TestMeshes.Triangle(0, 0, 0, float.PositiveInfinity, 1, 1, 2, 2, 2));

        var mesh = StlReader.Read(TestMeshes.BinaryFile(broken));

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(2, mesh.SkippedTriangles);
        Assert.Equal(10, mesh.Bounds.MaxX); // nothing of the broken ones is in the box
    }

    [Fact]
    public void ABinaryFileWithNoTriangles_IsAnEmptyModel()
    {
        var mesh = StlReader.Read(TestMeshes.BinaryFile([]));

        Assert.Equal(0, mesh.TriangleCount);
        Assert.Equal(default, mesh.Bounds);
    }

    // ── text files ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    public void ATextFile_IsReadWhateverTheLineEndsAndTheCaseOfItsWords(string newline, bool upper)
    {
        var text = TestMeshes.AsciiFile(Cube, "block", newline, upper);

        var mesh = StlReader.Read(Encoding.ASCII.GetBytes(text));

        Assert.Equal(StlFormat.Ascii, mesh.Format);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal("block", mesh.Name);
        Assert.Equal(Cube, mesh.Vertices.Take(12 * 9));
    }

    [Fact]
    public void ATextFile_ReadsNumbersWithAnExponentAndASign()
    {
        const string text = "solid x\nfacet normal 0 0 1\nouter loop\nvertex 1.5E+01 -2e-1 +3\nvertex 0 0 0\nvertex .5 1. -0\nendloop\nendfacet\nendsolid x\n";

        var mesh = StlReader.Read(Encoding.ASCII.GetBytes(text));

        Assert.Equal([15f, -0.2f, 3f, 0f, 0f, 0f, 0.5f, 1f, 0f], mesh.Vertices.Take(9));
    }

    [Fact]
    public void ATextFile_WithABomAndBlankLinesBefore_IsRead()
    {
        var text = "﻿\r\n\r\n  " + TestMeshes.AsciiFile(Cube);

        var mesh = StlReader.Read(new UTF8Encoding(false).GetBytes(text));

        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void ATextFile_WithSeveralSolids_IsOneModel()
    {
        var text = TestMeshes.AsciiFile(TestMeshes.Box(0, 0, 0, 1, 1, 1), "first") + TestMeshes.AsciiFile(TestMeshes.Box(5, 5, 5, 6, 6, 6), "second");

        var mesh = StlReader.Read(Encoding.ASCII.GetBytes(text));

        Assert.Equal(24, mesh.TriangleCount);
        Assert.Equal("first", mesh.Name);
        Assert.Equal(6, mesh.Bounds.MaxX);
    }

    [Fact]
    public void ATextFile_ThatStopsInTheMiddleOfATriangle_KeepsTheWholeOnes()
    {
        var text = TestMeshes.AsciiFile(Cube);
        var cut = text[..(text.LastIndexOf("vertex", StringComparison.Ordinal) + 12)]; // inside the last vertex of the last facet

        var mesh = StlReader.Read(Encoding.ASCII.GetBytes(cut));

        Assert.Equal(11, mesh.TriangleCount);
        Assert.Equal(1, mesh.SkippedTriangles);
    }

    [Fact]
    public void ATextFile_WithAVertexThatIsNoNumber_IsRefused()
    {
        const string text = "solid x\nfacet normal 0 0 1\nouter loop\nvertex 1 two 3\nvertex 0 0 0\nvertex 1 1 1\nendloop\nendfacet\nendsolid x\n";

        var failure = Assert.Throws<InvalidDataException>(() => StlReader.Read(Encoding.ASCII.GetBytes(text)));

        Assert.Contains("two", failure.Message);
    }

    [Fact]
    public void ATextFile_WithoutAnyTriangle_IsRefused() =>
        Assert.Throws<InvalidDataException>(() => StlReader.Read("solid empty\nendsolid empty\n"u8));

    // ── what is no STL ─────────────────────────────────────────────────

    [Fact]
    public void AnEmptyFile_IsRefused() => Assert.Throws<InvalidDataException>(() => StlReader.Read(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void ThingsThatAreNoSTL_AreRefused()
    {
        Assert.Throws<InvalidDataException>(() => StlReader.Read(new byte[200]));                                       // zeros: a count of nothing and a size that is not 84
        Assert.Throws<InvalidDataException>(() => StlReader.Read("hello, this is not a model at all"u8));
        Assert.Throws<InvalidDataException>(() => StlReader.Read(Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray()));
        Assert.Throws<InvalidDataException>(() => StlReader.Read([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));  // a PNG
    }

    // ── from a file ────────────────────────────────────────────────────

    [Fact]
    public void AFile_IsReadFromItsPath()
    {
        var path = Path.Combine(_folder, "cube.stl");
        File.WriteAllBytes(path, TestMeshes.BinaryFile(Cube, "cube"));

        var mesh = StlReader.Read(path);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal("cube", mesh.Name);
    }

    [Fact]
    public void AFile_ThatAnotherProgramHasOpenForWriting_CanBeRead()
    {
        var path = Path.Combine(_folder, "slicing.stl");
        File.WriteAllBytes(path, TestMeshes.BinaryFile(Cube));
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Write);

        Assert.Equal(12, StlReader.Read(path).TriangleCount);
    }

    [Fact]
    public void AFile_ThatIsNotThere_ThrowsFileNotFound() =>
        Assert.Throws<FileNotFoundException>(() => StlReader.Read(Path.Combine(_folder, "gone.stl")));
}

public class StlMeshTests
{
    [Fact]
    public void TheBounds_AreTheExtremesOfAllTheCorners()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Join(TestMeshes.Box(-2, 1, 3, 4, 5, 9), TestMeshes.Triangle(0, 0, 0, 1, 1, 1, 2, 2, 2)));

        Assert.Equal(new MeshBounds(-2, 0, 0, 4, 5, 9), mesh.Bounds);
        Assert.Equal(6, mesh.Bounds.SizeX);
        Assert.Equal(5, mesh.Bounds.SizeY);
        Assert.Equal(9, mesh.Bounds.SizeZ);
        Assert.Equal(1, mesh.Bounds.CenterX);
        Assert.Equal(Math.Sqrt(36 + 25 + 81) / 2, mesh.Bounds.Radius, 6);
    }

    [Fact]
    public void ABox_HasTheVolumeAndTheAreaOfABox()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, 10, 20, 30));

        Assert.Equal(6000, mesh.Volume, 6);
        Assert.Equal(2 * (200 + 300 + 600), mesh.SurfaceArea, 6);
    }

    [Fact]
    public void ABoxTurnedInsideOut_HasTheSameVolume() =>
        Assert.Equal(1000, TestMeshes.Mesh(TestMeshes.Reversed(TestMeshes.Box(0, 0, 0, 10, 10, 10))).Volume, 6);

    [Fact]
    public void ABoxFarFromTheOrigin_KeepsItsVolume()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Box(1_000_000.0625f, 1_000_000.1875f, 1_000_000.3125f, 1_000_010.5625f, 1_000_010.6875f, 1_000_010.8125f));

        Assert.Equal(1157.625, mesh.Volume, 3);
        Assert.Equal(661.5, mesh.SurfaceArea, 3);
    }

    [Fact]
    public void ASkewSolidTwelveMillionFromTheOrigin_KeepsItsVolume()
    {
        // A tetrahedron whose edges are (1000,300,100), (200,900,400) and (500,400,1100): six times its volume is the determinant, 787,000,000. Twelve million from the origin each pyramid from the origin is
        // of the order of 1e18, more than the 9e15 up to which a double holds whole numbers, so the rounding of them swamps the volume; from the middle of the solid the numbers are small and exact.
        // (A box of whole numbers with short edges does not show it: the cancelling happens inside the products, which stay exact.)
        const float x = 12_345_677, y = 11_111_113, z = 13_579_241; // odd numbers: a round one (12,000,000 is 2^8 x 46875) makes every product a multiple of a power of two, and exact
        float[] v0 = [x, y, z], v1 = [x + 1000, y + 300, z + 100], v2 = [x + 200, y + 900, z + 400], v3 = [x + 500, y + 400, z + 1100];
        var mesh = TestMeshes.Mesh([.. v0, .. v2, .. v1, .. v0, .. v1, .. v3, .. v0, .. v3, .. v2, .. v1, .. v2, .. v3]);

        Assert.Equal(787_000_000.0 / 6, mesh.Volume, 5); // measured: from the origin it is 131166666.66699, off by 0.0003
    }

    [Fact]
    public void ABall_HasAboutTheVolumeOfABall()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Sphere(10, 60, 120));

        Assert.Equal(4.0 / 3 * Math.PI * 1000, mesh.Volume, tolerance: 4189 * 0.01);
        Assert.Equal(4 * Math.PI * 100, mesh.SurfaceArea, tolerance: 1257 * 0.01);
    }

    [Fact]
    public void ATriangleWithNoArea_AddsNothing()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Join(TestMeshes.Box(0, 0, 0, 1, 1, 1), TestMeshes.Triangle(0, 0, 0, 1, 1, 1, 2, 2, 2)));

        Assert.Equal(6, mesh.SurfaceArea, 9);
    }

    [Fact]
    public void TheMemory_IsWhatTheCornersTake() => Assert.Equal(12 * 9 * 4, TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, 1, 1, 1)).Bytes);

    [Fact]
    public void AMesh_RefusesACountItsCornersCannotHold() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StlMesh(new float[17], 2, "x", StlFormat.Binary));
}
