using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoLiker.TraceBitmap
{
    /// <summary>How the picture is cut into the shapes that are traced: the scans of the Trace Bitmap of Inkscape.</summary>
    public enum TraceMode
    {
        /// <summary>Single scan: what is darker than the brightness cutoff becomes one black shape.</summary>
        BrightnessCutoff,

        /// <summary>Single scan: the edges of the picture (Canny edge detection) become one black line drawing.</summary>
        EdgeDetection,

        /// <summary>Single scan: the picture is reduced to a few colours and the lines where the colours meet become one black line drawing.</summary>
        ColorQuantization,

        /// <summary>Multiple scans: the picture is cut at evenly spaced brightnesses, a gray shape for each.</summary>
        BrightnessSteps,

        /// <summary>Multiple scans: the picture is reduced to a few colours, a shape for each.</summary>
        Colors,

        /// <summary>Multiple scans: the picture is cut into a few shades of gray, a shape for each.</summary>
        Grays,
    }

    /// <summary>
    /// The settings of a trace; the names and the meaning are those of the Trace Bitmap window of Inkscape. A value outside its range is brought into it by <see cref="Normalized"/>, which the
    /// tracer does, so a file written by hand cannot break it.
    /// </summary>
    public sealed class TraceSettings
    {
        public const double MinCornerThreshold = 0, MaxCornerThreshold = 1.3334;
        public const double MinTolerance = 0, MaxTolerance = 5;
        public const int MinScans = 2, MaxScans = 64;
        public const int MaxSpeckleSize = 1000;

        public TraceMode Mode { get; set; } = TraceMode.BrightnessCutoff;

        /// <summary>Brightness cutoff, 0 to 1: a pixel darker than this is black. Inkscape's default is 0.450.</summary>
        public double BrightnessCutoff { get; set; } = 0.45;

        /// <summary>Edge detection, 0 to 1: how strong an edge has to be to start a line (the high threshold of the Canny detector). Inkscape's default is 0.650.</summary>
        public double EdgeThreshold { get; set; } = 0.65;

        /// <summary>Color quantization: the number of colours the picture is reduced to.</summary>
        public int QuantizeColors { get; set; } = 8;

        /// <summary>Brightness steps: the number of scans.</summary>
        public int BrightnessSteps { get; set; } = 8;

        /// <summary>Colors: the number of scans (colours).</summary>
        public int Colors { get; set; } = 8;

        /// <summary>Grays: the number of scans (shades).</summary>
        public int Grays { get; set; } = 8;

        /// <summary>Single scans: trace what is light instead of what is dark.</summary>
        public bool InvertImage { get; set; }

        /// <summary>Multiple scans: blur the picture a little before tracing it, so that the noise of a photo does not become a thousand little shapes.</summary>
        public bool Smooth { get; set; } = true;

        /// <summary>Multiple scans: each scan covers everything that is darker as well (no gaps between shapes, good for filling); otherwise each shape is only its own band (good for stroking).</summary>
        public bool StackScans { get; set; } = true;

        /// <summary>Multiple scans: leave out the bottom (the lightest) scan, which is the background.</summary>
        public bool RemoveBackground { get; set; } = true;

        /// <summary>Leave out shapes (and fill holes) of <see cref="SpeckleSize"/> pixels or fewer.</summary>
        public bool SuppressSpeckles { get; set; } = true;

        public int SpeckleSize { get; set; } = 2;

        /// <summary>Round the corners that are not sharp enough; off, every corner stays a corner (straight lines).</summary>
        public bool SmoothCorners { get; set; } = true;

        /// <summary>0 (every corner is kept) to 1.334 (every corner is rounded); Inkscape's default is 1.0.</summary>
        public double CornerThreshold { get; set; } = 1.0;

        /// <summary>Join curves into fewer ones, as far as the tolerance allows.</summary>
        public bool OptimizePaths { get; set; } = true;

        /// <summary>How far, at most, the joined curve may be from what it replaces; Inkscape's default is 0.20.</summary>
        public double OptimizeTolerance { get; set; } = 0.2;

        /// <summary>True for the three modes that make several shapes of different colours.</summary>
        [JsonIgnore]
        public bool IsMultipleScans => Mode is TraceMode.BrightnessSteps or TraceMode.Colors or TraceMode.Grays;

        /// <summary>How many scans a multiple-scan mode makes.</summary>
        [JsonIgnore]
        public int ScanCount => Mode switch
        {
            TraceMode.BrightnessSteps => BrightnessSteps,
            TraceMode.Colors => Colors,
            TraceMode.Grays => Grays,
            _ => 1,
        };

        public TraceSettings Clone() => (TraceSettings)MemberwiseClone();

        /// <summary>A copy with every value inside its range.</summary>
        public TraceSettings Normalized()
        {
            var copy = Clone();
            copy.Mode = Enum.IsDefined(Mode) ? Mode : TraceMode.BrightnessCutoff;
            copy.BrightnessCutoff = Clamp(BrightnessCutoff, 0, 1, 0.45);
            copy.EdgeThreshold = Clamp(EdgeThreshold, 0, 1, 0.65);
            copy.QuantizeColors = Math.Clamp(QuantizeColors, MinScans, MaxScans);
            copy.BrightnessSteps = Math.Clamp(BrightnessSteps, MinScans, MaxScans);
            copy.Colors = Math.Clamp(Colors, MinScans, MaxScans);
            copy.Grays = Math.Clamp(Grays, MinScans, MaxScans);
            copy.SpeckleSize = Math.Clamp(SpeckleSize, 0, MaxSpeckleSize);
            copy.CornerThreshold = Clamp(CornerThreshold, MinCornerThreshold, MaxCornerThreshold, 1.0);
            copy.OptimizeTolerance = Clamp(OptimizeTolerance, MinTolerance, MaxTolerance, 0.2);
            return copy;
        }

        private static double Clamp(double value, double min, double max, double fallback) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }

    /// <summary>The kind of file a trace is written as; both are written in millimetres.</summary>
    public enum TraceFileFormat
    {
        Svg,
        Dxf,
    }

    /// <summary>
    /// What the Trace Bitmap window remembers between two uses: the settings of the scan, the longest side the picture is traced at, the size of the drawing and the format. The size is kept as
    /// the length of the longest side (so that the next photo, whatever its shape, comes out at the size that was last used).
    /// </summary>
    public sealed class TraceDialogSettings
    {
        public const int CurrentVersion = 1;

        public const int MinDetail = 200, MaxDetail = 6000, DefaultDetail = 1500;
        public const double MinSizeMillimetres = 0.1, MaxSizeMillimetres = 100000, DefaultLongestSideMillimetres = 100;
        public const double MinCurveAccuracy = 0.001, MaxCurveAccuracy = 10, DefaultCurveAccuracy = 0.02;

        public int Version { get; set; } = CurrentVersion;

        public TraceSettings Trace { get; set; } = new();

        /// <summary>The longest side of the picture that is traced, in pixels; a bigger photo is made smaller first.</summary>
        public int Detail { get; set; } = DefaultDetail;

        /// <summary>The longest side of the drawing in millimetres.</summary>
        public double LongestSideMillimetres { get; set; } = DefaultLongestSideMillimetres;

        public TraceFileFormat Format { get; set; } = TraceFileFormat.Svg;

        /// <summary>DXF only: how far, in millimetres, the straight pieces a curve is cut into may be from it.</summary>
        public double CurveAccuracyMillimetres { get; set; } = DefaultCurveAccuracy;

        public bool KeepProportions { get; set; } = true;

        public bool LivePreview { get; set; }

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Every value inside its range.</summary>
        public TraceDialogSettings Normalized() => new()
        {
            Version = CurrentVersion,
            Trace = (Trace ?? new TraceSettings()).Normalized(),
            Detail = Math.Clamp(Detail, MinDetail, MaxDetail),
            LongestSideMillimetres = double.IsFinite(LongestSideMillimetres) ? Math.Clamp(LongestSideMillimetres, MinSizeMillimetres, MaxSizeMillimetres) : DefaultLongestSideMillimetres,
            Format = Enum.IsDefined(Format) ? Format : TraceFileFormat.Svg,
            CurveAccuracyMillimetres = double.IsFinite(CurveAccuracyMillimetres) ? Math.Clamp(CurveAccuracyMillimetres, MinCurveAccuracy, MaxCurveAccuracy) : DefaultCurveAccuracy,
            KeepProportions = KeepProportions,
            LivePreview = LivePreview,
        };

        /// <summary>Reads the file; a file that is missing, empty or not readable gives the defaults (it is not an error to have none).</summary>
        public static TraceDialogSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new TraceDialogSettings();

                var loaded = JsonSerializer.Deserialize<TraceDialogSettings>(File.ReadAllText(path), Options);
                return (loaded ?? new TraceDialogSettings()).Normalized();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new TraceDialogSettings();
            }
        }

        /// <summary>Writes the file; false when it could not be written (the settings are a convenience, never a reason to stop).</summary>
        public bool Save(string path)
        {
            try
            {
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(Normalized(), Options));
                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return false;
            }
        }
    }
}
