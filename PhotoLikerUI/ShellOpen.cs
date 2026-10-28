using System.ComponentModel;
using System.Diagnostics;

namespace PhotoLikerUI
{
    /// <summary>
    /// Hands a photo or a folder to Windows, like a double click in File Explorer: a photo opens in the program that is set for it, a folder in
    /// File Explorer. Every call says why it did not work (or null if it did), so that a dialog can tell the person instead of failing.
    /// </summary>
    internal static class ShellOpen
    {
        public const string OpenErrorFormat = "Could not open '{0}': {1}";
        public const string OpenMenu = "Open";
        public const string ShowMenu = "Show in File Explorer";

        private const string NotThere = "it is not there any more";

        /// <summary>Opens the photo in the viewer Windows has for it.</summary>
        public static string? Photo(string path) =>
            File.Exists(path) ? Start(new ProcessStartInfo(path) { UseShellExecute = true }) : NotThere;

        /// <summary>Opens the folder in File Explorer.</summary>
        public static string? Folder(string folder) =>
            Directory.Exists(folder) ? Start(new ProcessStartInfo(folder) { UseShellExecute = true }) : NotThere;

        /// <summary>Opens the folder of the file in File Explorer with the file selected (the folder alone if the file is gone).</summary>
        public static string? ShowInFolder(string file)
        {
            if (File.Exists(file))
                return Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = $"/select,\"{file}\"" });

            return Path.GetDirectoryName(file) is { Length: > 0 } folder ? Folder(folder) : NotThere;
        }

        /// <summary>The words for a status line, from the result of one of the calls above; empty if it worked.</summary>
        public static string Report(string path, string? error) =>
            error is null ? string.Empty : string.Format(OpenErrorFormat, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), error);

        private static string? Start(ProcessStartInfo info)
        {
            try
            {
                using var process = Process.Start(info); // not waited for: it is the other program's window now
                return null;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                return ex.Message;
            }
        }
    }
}
