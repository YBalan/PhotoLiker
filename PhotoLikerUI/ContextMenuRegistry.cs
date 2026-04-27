using Microsoft.Win32;

namespace PhotoLikerUI
{
    internal static class ContextMenuRegistry
    {
        private const string MenuLabel = "Open in Photo Liker";
        private const string KeyName = "OpenInPhotoLiker";

        // HKCU paths — no admin rights required
        private static readonly string[] ShellKeyPaths =
        [
            @"Software\Classes\Directory\shell\" + KeyName,
            @"Software\Classes\Directory\Background\shell\" + KeyName,
        ];

        public static bool IsRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellKeyPaths[0]);
            return key is not null;
        }

        public static void Register(string exePath)
        {
            foreach (var shellPath in ShellKeyPaths)
            {
                using var key = Registry.CurrentUser.CreateSubKey(shellPath);
                key.SetValue(null, MenuLabel);
                key.SetValue("Icon", $"\"{exePath}\"");

                // Directory\shell uses %1, Background\shell uses %V
                bool isBackground = shellPath.Contains("Background");
                string folderArg = isBackground ? "%V" : "%1";

                using var cmdKey = key.CreateSubKey("command");
                cmdKey.SetValue(null, $"\"{exePath}\" \"{folderArg}\"");
            }
        }

        public static void Unregister()
        {
            foreach (var shellPath in ShellKeyPaths)
            {
                Registry.CurrentUser.DeleteSubKeyTree(shellPath, throwOnMissingSubKey: false);
            }
        }
    }
}
