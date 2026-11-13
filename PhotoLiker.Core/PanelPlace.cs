namespace PhotoLiker.Core
{
    /// <summary>
    /// The places the panel with the property grids can be in: docked to the left, right or bottom edge of the window, or floating in
    /// a window of its own. Kept as text in the config of the program (<see cref="GlobalConfig.PropertiesPlace"/>), so the file can be read
    /// and edited by a person, and a value that is not one of these reads as <see cref="Left"/>.
    /// </summary>
    public static class PanelPlace
    {
        public const string Left = "Left";
        public const string Right = "Right";
        public const string Bottom = "Bottom";
        public const string Float = "Float";

        public const int DefaultWidth = 320;
        public const int DefaultHeight = 280;

        /// <summary>The smallest width / height the panel can be dragged to, so that the grids stay usable.</summary>
        public const int MinWidth = 160;
        public const int MinHeight = 120;

        /// <summary>The name written in the config, for any text somebody typed there: unknown ones, and any letter case, become the nearest valid one.</summary>
        public static string Normalize(string? place) => place?.Trim().ToLowerInvariant() switch
        {
            "right" => Right,
            "bottom" => Bottom,
            "float" => Float,
            _ => Left,
        };
    }
}
