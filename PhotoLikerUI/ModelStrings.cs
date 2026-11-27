using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// The texts of the 3D models (STL files): in the viewer, in the rows of the metadata grid, and in Windows' list of programs for .stl files. A name ending in Format is a format string;
    /// a name that begins with Desc is what a row of the grid means (shown in the help pane under it).
    /// </summary>
    internal static class ModelStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(ModelStrings), key);

        // The viewer
        public static string StatusShownFormat => T();
        public static string StatusNotAPhoto => T();
        public static string ViewIso => T();
        public static string ViewFront => T();
        public static string ViewBack => T();
        public static string ViewLeft => T();
        public static string ViewRight => T();
        public static string ViewTop => T();
        public static string ViewBottom => T();
        public static string ViewWireframe => T();

        // The values and the meaning of the rows of the grid
        public static string FormatBinary => T();
        public static string FormatText => T();
        public static string DescTriangles => T();
        public static string DescSize => T();
        public static string DescVolume => T();
        public static string DescArea => T();
        public static string DescFormat => T();
        public static string DescName => T();
        public static string DescSkipped => T();
        public static string DescFileSize => T();
        public static string DescFileDates => T();

        // Windows' list of programs for .stl files
        public static string FileTypeName => T();
        public static string AppDescription => T();
        public static string ButtonText => T();
        public static string TooltipOff => T();
        public static string TooltipRegistered => T();
        public static string TooltipDefault => T();
        public static string DialogTitle => T();
        public static string QuestionRegistered => T();
        public static string QuestionDefault => T();
        public static string StatusRegisteredOpened => T();
        public static string StatusRegisteredNoSettings => T();
        public static string StatusSettingsOpened => T();
        public static string StatusUnregistered => T();
        public static string StatusAssociationErrorFormat => T();
    }
}
