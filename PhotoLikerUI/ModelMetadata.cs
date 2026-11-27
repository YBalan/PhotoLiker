using System.Globalization;
using PhotoLiker.VectorFiles;

namespace PhotoLikerUI
{
    /// <summary>
    /// The rows that describe a 3D model in the metadata grid (the place where a photo has its EXIF), in the category "3D model". The names of the rows are English, like those of the EXIF rows;
    /// what each means is in the language that is on. An STL file does not say what unit its numbers are in: like the programs that print it, this reads them as millimetres.
    /// </summary>
    internal static class ModelMetadata
    {
        private const int Base = MetadataEntry.ModelTagBase;

        public const string TrianglesName = "Triangles";
        public const string SizeName = "Size";
        public const string VolumeName = "Volume";
        public const string AreaName = "Surface area";
        public const string FormatName = "Format";
        public const string ModelNameName = "Model name";
        public const string SkippedName = "Skipped triangles";
        public const string FileSizeName = "File size";
        public const string FileDatesName = "File dates";

        public static IReadOnlyList<MetadataEntry> Entries(StlMesh mesh, FileInfo? file = null)
        {
            var bounds = mesh.Bounds;
            var rows = new List<MetadataEntry>
            {
                Row(0, TrianglesName, mesh.TriangleCount.ToString("N0", CultureInfo.InvariantCulture), ModelStrings.DescTriangles),
                Row(1, SizeName, SizeText(bounds), ModelStrings.DescSize),
                Row(2, VolumeName, VolumeText(mesh.Volume), ModelStrings.DescVolume),
                Row(3, AreaName, AreaText(mesh.SurfaceArea), ModelStrings.DescArea),
                Row(4, FormatName, mesh.Format == StlFormat.Binary ? ModelStrings.FormatBinary : ModelStrings.FormatText, ModelStrings.DescFormat),
            };

            if (mesh.Name.Length > 0)
                rows.Add(Row(5, ModelNameName, mesh.Name, ModelStrings.DescName));
            if (mesh.SkippedTriangles > 0)
                rows.Add(Row(6, SkippedName, mesh.SkippedTriangles.ToString("N0", CultureInfo.InvariantCulture), ModelStrings.DescSkipped));
            if (file is { Exists: true })
            {
                rows.Add(Row(7, FileSizeName, FileSizeText(file.Length), ModelStrings.DescFileSize));
                rows.Add(Row(8, FileDatesName, FileDateRow.Describe(file.CreationTime, file.LastWriteTime, taken: null), ModelStrings.DescFileDates));
            }

            return rows;
        }

        private static MetadataEntry Row(int number, string name, string value, string description) =>
            new(Base + number, name, value, Description: description);

        internal static string SizeText(MeshBounds bounds) =>
            string.Create(CultureInfo.InvariantCulture, $"{Round(bounds.SizeX)} × {Round(bounds.SizeY)} × {Round(bounds.SizeZ)} mm");

        /// <summary>In cubic centimetres once it is a thousand cubic millimetres or more (a cubic centimetre is what one pictures), with the millimetres beside it.</summary>
        internal static string VolumeText(double cubicMillimetres) =>
            cubicMillimetres >= 1000
                ? string.Create(CultureInfo.InvariantCulture, $"{Round(cubicMillimetres / 1000)} cm³ ({cubicMillimetres:N0} mm³)")
                : string.Create(CultureInfo.InvariantCulture, $"{Round(cubicMillimetres)} mm³");

        internal static string AreaText(double squareMillimetres) =>
            squareMillimetres >= 100
                ? string.Create(CultureInfo.InvariantCulture, $"{Round(squareMillimetres / 100)} cm² ({squareMillimetres:N0} mm²)")
                : string.Create(CultureInfo.InvariantCulture, $"{Round(squareMillimetres)} mm²");

        internal static string FileSizeText(long bytes) => bytes switch
        {
            >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.##} GB"),
            >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.#} MB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0} KB"),
        };

        /// <summary>Two decimals at most, and none that are zeros: 40, 12.5, 0.07.</summary>
        private static string Round(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
