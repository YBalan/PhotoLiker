using System.Globalization;
using System.Text;

namespace LutGen;

/// <summary>
/// Writes the looks as .cube files. A look is a function from a colour (red, green, blue, each 0 to 1) to a colour; the file is that function at 33 points of each channel, red changing fastest,
/// which is what the format says and what every grading program reads (PhotoLiker's own reader honours it too, see CubeLut).
/// </summary>
internal static class Program
{
    private const int Size = 33;

    private delegate (float R, float G, float B) Look(float r, float g, float b);

    private static int Main(string[] args)
    {
        var folder = args.Length > 0 ? args[0] : Path.Combine(RepositoryRoot(), "luts");
        Directory.CreateDirectory(folder);

        foreach (var (name, description, look) in Looks)
        {
            var path = Path.Combine(folder, name + ".cube");
            Write(path, name, description, look);
            Console.WriteLine($"{name}.cube  {new FileInfo(path).Length / 1024} KB");
        }

        File.WriteAllText(Path.Combine(folder, "README.txt"), Readme());
        return 0;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PhotoLiker.sln")))
                return directory.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    private static void Write(string path, string title, string description, Look look)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(description).Append('\n');
        text.Append("# Made for PhotoLiker by tools/LutGen (MIT licence, like the program). Free to use in any program that reads .cube files.\n");
        text.Append("TITLE \"").Append(title).Append("\"\n");
        text.Append("LUT_3D_SIZE ").Append(Size).Append('\n');
        for (var b = 0; b < Size; b++)
        for (var g = 0; g < Size; g++)
        for (var r = 0; r < Size; r++)
        {
            var (outR, outG, outB) = look(r / (Size - 1f), g / (Size - 1f), b / (Size - 1f));
            text.Append(Number(outR)).Append(' ').Append(Number(outG)).Append(' ').Append(Number(outB)).Append('\n');
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>A number 0 to 1 with four decimals, the zeros at the end left off: a table is 36,000 lines.</summary>
    private static string Number(float value) =>
        Math.Clamp(value, 0f, 1f).ToString("0.####", CultureInfo.InvariantCulture);

    private static string Readme() =>
        "Colour lookup tables (.cube) for PhotoLiker\n" +
        "===========================================\n\n" +
        "Twelve looks in the styles people use most. Choose one for the slideshow: set Look to Lut in the settings (6. Slideshow) and click the ... button of the LookFile row;\n" +
        "the file dialog opens in this folder. They are standard 33 x 33 x 33 tables, so any program that reads .cube files (DaVinci Resolve, Premiere, Photoshop, OBS ...) can use them too.\n\n" +
        "They were made for PhotoLiker by tools/LutGen (a few lines of arithmetic for each look; see the repository) and are MIT licensed like the program: free to use, change and\n" +
        "pass on. They are in the style of the looks they are named after, not copies of anybody's pack. To add your own, put more .cube files here or anywhere else and choose them in LookFile.\n";

    // ── the looks ───────────────────────────────────────────────────────────

    private static readonly (string Name, string Description, Look Look)[] Looks =
    [
        ("Teal and Orange", "Teal in the shadows, orange in the lights, more contrast: the blockbuster look", TealAndOrange),
        ("Warm Vintage", "An old colour print: lifted blacks, paler warm colours", WarmVintage),
        ("Matte Fade", "Faded matte film: the black lifted, the white lowered, colours a little paler", MatteFade),
        ("Soft Portrait Warm", "A soft warm look for people: gentle contrast, warm skin, calm highlights", SoftPortraitWarm),
        ("Cool Green Film", "Cool shadows and a green cast, slightly stronger colours: a green-leaning film", CoolGreenFilm),
        ("Bleach Bypass", "Silver retained: strong contrast and half the colour", BleachBypass),
        ("Cross Process", "Slide film developed as negative: contrasty red, lifted blue, strong colours", CrossProcess),
        ("Golden Hour", "Warm low sun: golden lights, soft contrast", GoldenHour),
        ("Cold Night", "A cool, darker look with blue shadows", ColdNight),
        ("Vivid Pop", "Strong colours (more on the dull ones), more contrast", VividPop),
        ("Black and White Contrast", "Black and white with a strong curve and a slight red filter", BlackAndWhiteContrast),
        ("Black and White Soft Film", "Black and white with lifted blacks and a gentle curve, like an old film", BlackAndWhiteSoftFilm),
    ];

    private static float Luma(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    /// <summary>A curve through the middle: <paramref name="amount"/> 0 leaves it, 1 is the smoothstep (the shadows darker, the lights lighter), negative flattens it.</summary>
    private static float Curve(float x, float amount)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x + amount * (Smooth(x) - x);
    }

    private static (float, float, float) Saturate(float r, float g, float b, float amount)
    {
        var l = Luma(r, g, b);
        return (l + (r - l) * amount, l + (g - l) * amount, l + (b - l) * amount);
    }

    /// <summary>The usual steps of a grade: colour strength, a curve, a tint in the shadows and another in the lights, a gain on each colour, then the black lifted and the white lowered.</summary>
    private static (float R, float G, float B) Grade(
        float r, float g, float b, float saturation, float curve,
        (float R, float G, float B) shadows, (float R, float G, float B) lights, (float R, float G, float B) gain, float lift, float drop)
    {
        (r, g, b) = Saturate(r, g, b, saturation);
        r = Curve(r, curve);
        g = Curve(g, curve);
        b = Curve(b, curve);

        var tone = Math.Clamp(Luma(r, g, b), 0f, 1f);
        var shadow = (1 - tone) * (1 - tone);
        var light = tone * tone;
        r = (r + shadows.R * shadow + lights.R * light) * gain.R;
        g = (g + shadows.G * shadow + lights.G * light) * gain.G;
        b = (b + shadows.B * shadow + lights.B * light) * gain.B;

        var range = 1 - lift - drop;
        return (lift + Math.Clamp(r, 0f, 1f) * range, lift + Math.Clamp(g, 0f, 1f) * range, lift + Math.Clamp(b, 0f, 1f) * range);
    }

    private static readonly (float, float, float) None = (0, 0, 0);
    private static readonly (float, float, float) Even = (1, 1, 1);

    private static (float R, float G, float B) TealAndOrange(float r, float g, float b) =>
        Grade(r, g, b, 1.08f, 0.30f, (-0.070f, 0.015f, 0.075f), (0.085f, 0.030f, -0.085f), Even, 0.01f, 0.01f);

    private static (float R, float G, float B) WarmVintage(float r, float g, float b) =>
        Grade(r, g, b, 0.78f, 0.10f, (0.030f, 0.010f, -0.020f), (0.050f, 0.025f, -0.050f), Even, 0.07f, 0.06f);

    private static (float R, float G, float B) MatteFade(float r, float g, float b) =>
        Grade(r, g, b, 0.92f, 0.12f, None, None, Even, 0.09f, 0.05f);

    private static (float R, float G, float B) SoftPortraitWarm(float r, float g, float b) =>
        Grade(r, g, b, 0.96f, 0.12f, (0.015f, 0.005f, -0.005f), (0.035f, 0.015f, -0.030f), (1.02f, 1.0f, 0.97f), 0.02f, 0.03f);

    private static (float R, float G, float B) CoolGreenFilm(float r, float g, float b) =>
        Grade(r, g, b, 1.05f, 0.25f, (-0.030f, 0.020f, 0.020f), (0.0f, 0.015f, -0.010f), (0.98f, 1.02f, 1.0f), 0.02f, 0.02f);

    private static (float R, float G, float B) BleachBypass(float r, float g, float b)
    {
        // Half the colour, and the contrast of the grey laid over it
        (r, g, b) = Saturate(r, g, b, 0.45f);
        return Grade(r, g, b, 1f, 0.85f, None, None, Even, 0.01f, 0.01f);
    }

    private static (float R, float G, float B) CrossProcess(float r, float g, float b)
    {
        // Each colour has a curve of its own: the red hard, the green a little, the blue lifted and flat
        var red = Curve(r, 1.2f);
        var green = Curve(g, 0.6f) + 0.02f;
        var blue = 0.10f + 0.80f * Curve(b, -0.3f);
        (red, green, blue) = Saturate(red, green, blue, 1.15f);
        return (red, green, blue);
    }

    private static (float R, float G, float B) GoldenHour(float r, float g, float b) =>
        Grade(r, g, b, 1.10f, 0.15f, None, (0.040f, 0.020f, -0.030f), (1.06f, 1.0f, 0.88f), 0.01f, 0.01f);

    private static (float R, float G, float B) ColdNight(float r, float g, float b) =>
        Grade(r, g, b, 0.90f, 0.25f, (-0.020f, 0.0f, 0.050f), None, (0.90f * 0.94f, 0.97f * 0.94f, 1.08f * 0.94f), 0.01f, 0.01f);

    private static (float R, float G, float B) VividPop(float r, float g, float b)
    {
        // The duller a colour is, the more it is strengthened: a vibrance, so that what is strong already does not clip
        var strength = (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / Math.Max(Math.Max(r, Math.Max(g, b)), 0.001f);
        return Grade(r, g, b, 1.15f + 0.35f * (1 - strength), 0.30f, None, None, Even, 0f, 0f);
    }

    private static (float R, float G, float B) BlackAndWhiteContrast(float r, float g, float b)
    {
        var grey = Curve(0.35f * r + 0.50f * g + 0.15f * b, 0.9f);
        var value = 0.01f + grey * 0.98f;
        return (value, value, value);
    }

    private static (float R, float G, float B) BlackAndWhiteSoftFilm(float r, float g, float b)
    {
        var grey = Curve(Luma(r, g, b), 0.2f);
        var value = 0.06f + grey * 0.90f;
        return (value, value, value);
    }
}
