namespace PhotoLiker.Core
{
    using System.Text.Json;

    /// <summary>Lightweight user-level config stored in %LocalAppData%\PhotoLiker\.</summary>
    public class GlobalConfig
    {
        private static readonly string FilePath = Path.Combine(AppPaths.DataFolder, "global-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public string LastFolder { get; set; } = string.Empty;

        /// <summary>The language of the program, by <see cref="AppLanguage.Code"/> ("en", "ua"). One for the whole program, so it is kept here and not in the settings of a photo folder. A language whose file is gone is English.</summary>
        public string Language { get; set; } = AppLanguage.EnglishCode;

        /// <summary>Whether the bar for colorizing floats over a black and white photo that is shown. Closing the bar turns it off; the Colorize button of the toolbar turns it on again.</summary>
        public bool ColorizeBar { get; set; } = true;

        /// <summary>The strength of the colours on the bar, in percent (100 is as the colorizing model gives them): the same for the next photo.</summary>
        public int ColorizeStrength { get; set; } = 100;

        /// <summary>The warmth of the colours on the bar, from -100 (cooler) to 100 (warmer).</summary>
        public int ColorizeWarmth { get; set; }

        /// <summary>How strong the blur is on the Refocus bar, 0 to 100 (see <c>FocusLook.Blur</c>): the same for the next photo, and the next time the program runs.</summary>
        public int RefocusBlur { get; set; } = 50;

        /// <summary>How deep the zone round the focus is that stays sharp on the Refocus bar, 0 to 100 (see <c>FocusLook.Range</c>).</summary>
        public int RefocusRange { get; set; } = 30;

        /// <summary>How much haze lies over what is far on the Refocus bar, 0 to 100 (see <c>FocusLook.Haze</c>).</summary>
        public int RefocusHaze { get; set; }

        /// <summary>Whether the toolbar at the left edge shows the names of its buttons next to the icons (the hamburger button on top turns it on and off).</summary>
        public bool RailExpanded { get; set; }

        /// <summary>Where the panel with the two property grids is: "Left", "Right", "Bottom" or "Float" (its own window). Anything else reads as Left; see <see cref="PanelPlace"/>.</summary>
        public string PropertiesPlace { get; set; } = PanelPlace.Left;

        /// <summary>The panel is folded into a thin tab at its edge (the Properties button of the toolbar, or the tab itself, opens it again). Ignored while it floats.</summary>
        public bool PropertiesCollapsed { get; set; }

        /// <summary>The width of the panel when it is docked left or right, in pixels at 96 dpi.</summary>
        public int PropertiesWidth { get; set; } = PanelPlace.DefaultWidth;

        /// <summary>The height of the panel when it is docked at the bottom, in pixels at 96 dpi.</summary>
        public int PropertiesHeight { get; set; } = PanelPlace.DefaultHeight;

        /// <summary>Where the window of the panel was when it floated last (left, top, width, height); 0 wide means never.</summary>
        public int FloatLeft { get; set; }
        public int FloatTop { get; set; }
        public int FloatWidth { get; set; }
        public int FloatHeight { get; set; }

        public static GlobalConfig Load()
        {
            if (!File.Exists(FilePath)) return new GlobalConfig();
            try
            {
                return JsonSerializer.Deserialize<GlobalConfig>(File.ReadAllText(FilePath)) ?? new GlobalConfig();
            }
            catch { return new GlobalConfig(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch { /* best-effort */ }
        }
    }
}
