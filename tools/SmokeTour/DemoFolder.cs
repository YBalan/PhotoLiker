using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Text;
using PhotoLiker.Core;

namespace SmokeTour;

/// <summary>
/// A folder of generated "holiday" photos with EXIF and GPS data, so the screenshots show something pleasant and
/// nothing of anybody's real photos. Five JPEGs, three more in a subfolder, two PNGs.
/// </summary>
internal static class DemoFolder
{
    public const string SubFolder = "Day 2";

    // Marks a folder as made by this tool: only such a folder is ever deleted and recreated
    private const string Marker = ".photoliker-smoketour";

    private sealed record Scene(
        string File, Color SkyTop, Color SkyBottom, Color Sun, float SunX, float SunY, bool Night, Color[] Hills, double Latitude, double Longitude);

    // Near Lisbon; every photo is a little further along the coast
    private static readonly Scene[] Scenes =
    [
        new("01-beach.jpg",      Color.FromArgb(70, 140, 220), Color.FromArgb(250, 220, 170), Color.FromArgb(255, 240, 190), 0.75f, 0.28f, false, [Color.FromArgb(70, 130, 150), Color.FromArgb(235, 205, 150), Color.FromArgb(215, 180, 120)], 38.6979, -9.4215),
        new("02-harbour.jpg",    Color.FromArgb(40, 90, 160),  Color.FromArgb(240, 170, 120), Color.FromArgb(255, 200, 120), 0.30f, 0.40f, false, [Color.FromArgb(40, 70, 110), Color.FromArgb(30, 55, 90), Color.FromArgb(20, 35, 60)], 38.7053, -9.4147),
        new("03-sunset.jpg",     Color.FromArgb(90, 40, 110),  Color.FromArgb(255, 140, 80),  Color.FromArgb(255, 230, 150), 0.55f, 0.58f, false, [Color.FromArgb(120, 50, 90), Color.FromArgb(70, 30, 70), Color.FromArgb(35, 20, 45)], 38.7116, -9.4098),
        new("04-forest.jpg",     Color.FromArgb(120, 190, 220), Color.FromArgb(220, 240, 200), Color.FromArgb(255, 255, 220), 0.20f, 0.22f, false, [Color.FromArgb(90, 150, 90), Color.FromArgb(50, 110, 70), Color.FromArgb(25, 70, 45)], 38.7712, -9.3830),
        new("05-mountains.jpg",  Color.FromArgb(60, 100, 170), Color.FromArgb(200, 215, 240), Color.FromArgb(255, 250, 235), 0.82f, 0.18f, false, [Color.FromArgb(150, 165, 190), Color.FromArgb(100, 115, 150), Color.FromArgb(55, 65, 95)], 38.7861, -9.3772),
        new(Path.Combine(SubFolder, "06-market.jpg"),   Color.FromArgb(255, 190, 120), Color.FromArgb(255, 235, 190), Color.FromArgb(255, 255, 230), 0.40f, 0.20f, false, [Color.FromArgb(200, 110, 80), Color.FromArgb(160, 80, 60), Color.FromArgb(110, 50, 45)], 38.7139, -9.1394),
        new(Path.Combine(SubFolder, "07-old-town.jpg"), Color.FromArgb(120, 160, 210), Color.FromArgb(235, 225, 210), Color.FromArgb(255, 245, 210), 0.68f, 0.25f, false, [Color.FromArgb(190, 150, 120), Color.FromArgb(150, 110, 90), Color.FromArgb(95, 70, 65)], 38.7110, -9.1303),
        new(Path.Combine(SubFolder, "08-river.jpg"),    Color.FromArgb(30, 60, 120),  Color.FromArgb(150, 190, 230), Color.FromArgb(255, 245, 200), 0.25f, 0.30f, false, [Color.FromArgb(60, 110, 150), Color.FromArgb(40, 85, 125), Color.FromArgb(25, 55, 90)], 38.6916, -9.2160),
        new("09-panorama.png",   Color.FromArgb(100, 170, 230), Color.FromArgb(235, 245, 255), Color.FromArgb(255, 255, 240), 0.50f, 0.15f, false, [Color.FromArgb(120, 170, 120), Color.FromArgb(80, 130, 100), Color.FromArgb(45, 90, 80)], 38.7436, -9.1602),
        new("10-night.png",      Color.FromArgb(8, 12, 35),    Color.FromArgb(45, 55, 105),   Color.FromArgb(240, 240, 255), 0.70f, 0.22f, true,  [Color.FromArgb(25, 35, 70), Color.FromArgb(15, 22, 48), Color.FromArgb(8, 12, 28)], 38.7071, -9.1365),
    ];

    /// <summary>Files of the folder itself (not of the subfolder) by extension, for the checks of the tour.</summary>
    public static int CountTopLevel(string extension) =>
        Scenes.Count(s => !s.File.Contains(Path.DirectorySeparatorChar) && s.File.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static int CountInSubFolder => Scenes.Count(s => s.File.Contains(Path.DirectorySeparatorChar));

    public static string[] FileNames => [.. Scenes.Select(s => Path.GetFileName(s.File))];

    /// <summary>
    /// Recreates the folder: photos, plus the settings file the first run would have left (dark, JPEG only).
    /// Throws if the folder exists and is not one this tool made.
    /// </summary>
    public static void Create(string folder, Rectangle window)
    {
        if (Directory.Exists(folder))
        {
            if (Directory.EnumerateFileSystemEntries(folder).Any() && !File.Exists(Path.Combine(folder, Marker)))
                throw new InvalidOperationException($"'{folder}' exists and was not made by this tool, so it is not touched. Pick another folder with --demo-folder.");

            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Marker), "Made by tools/SmokeTour; it recreates and deletes this folder.");

        for (var i = 0; i < Scenes.Length; i++)
            Render(Path.Combine(folder, Scenes[i].File), Scenes[i], i);

        new Settings
        {
            CurrentFolder = folder,
            Extensions = [".jpg"],
            IsDarkTheme = true,
            ThumbnailSize = 120,
            WindowLeft = window.Left,
            WindowTop = window.Top,
            WindowWidth = window.Width,
            WindowHeight = window.Height,
        }.Write(folder);
    }

    private static void Render(string path, Scene scene, int index)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        const int w = 1600, h = 1000;
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var random = new Random(1000 + index);

            using (var sky = new LinearGradientBrush(new Rectangle(0, 0, w, h), scene.SkyTop, scene.SkyBottom, 90f))
                g.FillRectangle(sky, 0, 0, w, h);

            if (scene.Night)
            {
                for (var s = 0; s < 220; s++)
                {
                    var size = random.Next(1, 4);
                    using var star = new SolidBrush(Color.FromArgb(random.Next(120, 255), Color.White));
                    g.FillEllipse(star, random.Next(w), random.Next(h / 2), size, size);
                }
            }

            // The sun (or moon) with a soft glow
            var cx = scene.SunX * w;
            var cy = scene.SunY * h;
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(cx - 260, cy - 260, 520, 520);
                using var glowBrush = new PathGradientBrush(glow)
                {
                    CenterColor = Color.FromArgb(150, scene.Sun),
                    SurroundColors = [Color.FromArgb(0, scene.Sun)],
                };
                g.FillPath(glowBrush, glow);
            }
            using (var disc = new SolidBrush(scene.Sun))
                g.FillEllipse(disc, cx - 62, cy - 62, 124, 124);

            // Layers of hills, the nearest one last
            for (var layer = 0; layer < scene.Hills.Length; layer++)
            {
                var baseline = h * (0.58f + 0.13f * layer);
                var amplitude = 70f - layer * 14f;
                var frequency = 0.004f + layer * 0.0021f;
                var phase = (float)(random.NextDouble() * 6.0);

                var points = new List<PointF> { new(0, h) };
                for (var x = 0; x <= w; x += 16)
                    points.Add(new PointF(x, baseline + amplitude * MathF.Sin(x * frequency + phase) + 0.45f * amplitude * MathF.Sin(x * frequency * 2.7f + phase * 1.7f)));
                points.Add(new PointF(w, h));

                using var hill = new SolidBrush(scene.Hills[layer]);
                g.FillPolygon(hill, points.ToArray());
            }
        }

        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            bmp.Save(path, ImageFormat.Png);
            return;
        }

        SetExif(bmp, scene, index);
        var jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
        bmp.Save(path, jpeg, parameters);
    }

    // ── EXIF ────────────────────────────────────────────────────────────────────────────────────

    private static void SetExif(Bitmap bmp, Scene scene, int index)
    {
        const short Byte = 1, Ascii = 2, Short = 3, Rational = 5;

        Set(bmp, 0x010F, Ascii, Text("PhotoLiker"));
        Set(bmp, 0x0110, Ascii, Text("Demo Cam X1"));
        Set(bmp, 0x0112, Short, BitConverter.GetBytes((ushort)1));
        Set(bmp, 0x829A, Rational, Rationals((1, 250)));
        Set(bmp, 0x829D, Rational, Rationals((28, 10)));
        Set(bmp, 0x8827, Short, BitConverter.GetBytes((ushort)200));
        Set(bmp, 0x9003, Ascii, Text($"2025:08:{14 + index / 5:00} {9 + index % 5 * 2:00}:{12 + index * 3:00}:07"));
        Set(bmp, 0x920A, Rational, Rationals((350, 10)));

        // GPS: N/E reference letters, then degrees, minutes, seconds as rationals
        Set(bmp, 0x0001, Ascii, Text(scene.Latitude >= 0 ? "N" : "S"));
        Set(bmp, 0x0002, Rational, Dms(Math.Abs(scene.Latitude)));
        Set(bmp, 0x0003, Ascii, Text(scene.Longitude >= 0 ? "E" : "W"));
        Set(bmp, 0x0004, Rational, Dms(Math.Abs(scene.Longitude)));
        Set(bmp, 0x0005, Byte, [0]);
        Set(bmp, 0x0006, Rational, Rationals(((uint)(12 + index * 7), 1)));

        static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + "\0");
    }

    private static byte[] Dms(double degrees)
    {
        var whole = (uint)degrees;
        var minutes = (uint)((degrees - whole) * 60);
        var seconds = ((degrees - whole) * 60 - minutes) * 60;
        return Rationals((whole, 1), (minutes, 1), ((uint)Math.Round(seconds * 100), 100));
    }

    private static byte[] Rationals(params (uint Numerator, uint Denominator)[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i].Numerator).CopyTo(bytes, i * 8);
            BitConverter.GetBytes(values[i].Denominator).CopyTo(bytes, i * 8 + 4);
        }
        return bytes;
    }

    // PropertyItem has no public constructor; this is the usual way to make one for SetPropertyItem
    private static void Set(Bitmap bmp, int id, short type, byte[] value)
    {
        var item = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
        item.Id = id;
        item.Type = type;
        item.Value = value;
        item.Len = value.Length;
        bmp.SetPropertyItem(item);
    }
}
