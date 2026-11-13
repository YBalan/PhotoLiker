using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the toolbar at the left edge (the hamburger button) and of the panel with the property grids that can be docked.</summary>
    internal static class LayoutStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(LayoutStrings), key);

        // The toolbar
        public static string RailShowNames => T();
        public static string RailHideNames => T();
        public static string PropertiesButton => T();
        public static string PropertiesButtonTooltip => T();

        // The panel
        public static string PanelTitle => T();
        public static string PanelMenuTooltip => T();
        public static string PanelDockLeft => T();
        public static string PanelDockRight => T();
        public static string PanelDockBottom => T();
        public static string PanelFloat => T();
        public static string PanelCollapse => T();
        public static string PanelCollapseTooltip => T();
        public static string PanelExpandTooltip => T();
        public static string PanelTitleTooltip => T();
    }
}
