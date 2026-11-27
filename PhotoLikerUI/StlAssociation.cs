using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PhotoLikerUI
{
    /// <summary>
    /// Photo Liker as one of the programs Windows offers for .stl files. Since Windows 8 a program cannot make itself the default for a kind of file (the choice is the person's, and
    /// Windows keeps it under a hash that only its own Settings window can write): what a program can do is to register what it opens, after which it is in the "Open with" list and has
    /// a page in Settings, Apps, Default apps, where one click makes it the default. This does the first and opens the second.
    ///
    /// Everything is written under the current user (HKCU), so no administrator is needed, and the setup of the program removes it again when the program is uninstalled
    /// (installer\PhotoLiker.iss). <see cref="Register"/> and <see cref="Unregister"/> write the same keys the setup removes; a test can give another place for them to be written (<c>softwareKey</c>).
    /// </summary>
    public sealed class StlAssociation
    {
        /// <summary>What Windows calls the kind of file for as long as Photo Liker is the one that opens it.</summary>
        public const string ProgId = "PhotoLiker.StlFile";

        /// <summary>The name Photo Liker is registered under: the value in RegisteredApplications and the name of the key of its capabilities.</summary>
        public const string RegisteredName = "PhotoLiker";

        public const string Extension = ".stl";

        private readonly string _exePath;
        private readonly string _software;
        private readonly bool _real;

        /// <param name="exePath">The program to start for the files.</param>
        /// <param name="softwareKey">Where under HKCU the keys are written: "Software" for real; a test uses a place of its own, which it deletes afterwards.</param>
        public StlAssociation(string exePath, string softwareKey = "Software")
        {
            _exePath = exePath;
            _software = softwareKey;
            _real = softwareKey == "Software";
        }

        /// <summary>
        /// The program Windows has to start for a file. That is the program that is running, except when it was started as "dotnet PhotoLikerUI.dll" (a build run from an editor or a
        /// command line, not the installed program): the running program is then dotnet.exe, which cannot open a file, so it is the .exe that is beside the .dll.
        /// </summary>
        public static string ProgramPath(string processPath, string baseDirectory)
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var apphost = Path.Combine(baseDirectory, "PhotoLikerUI.exe");
                if (File.Exists(apphost))
                    return apphost;
            }

            return processPath;
        }

        private string Command => $"\"{_exePath}\" \"%1\"";

        private string ClassesKey => _software + @"\Classes";
        private string ProgIdKey => ClassesKey + @"\" + ProgId;
        private string ApplicationKey => ClassesKey + @"\Applications\" + Path.GetFileName(_exePath);
        private string OpenWithKey => ClassesKey + @"\" + Extension + @"\OpenWithProgids";
        private string CapabilitiesKey => _software + @"\" + RegisteredName + @"\Capabilities";
        private string RegisteredApplicationsKey => _software + @"\RegisteredApplications";
        private string UserChoiceKey => _software + @"\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + Extension + @"\UserChoice";

        /// <summary>Photo Liker is in the list of programs for .stl files (it is not necessarily the one that opens them: see <see cref="IsDefault"/>).</summary>
        public bool IsRegistered()
        {
            using var progId = Registry.CurrentUser.OpenSubKey(ProgIdKey + @"\shell\open\command");
            using var openWith = Registry.CurrentUser.OpenSubKey(OpenWithKey);
            return progId is not null && openWith?.GetValue(ProgId) is not null;
        }

        /// <summary>It is registered, and to start this very program (a registration made by another copy of the program, or by an older one, starts that one).</summary>
        public bool IsCurrent()
        {
            using var key = Registry.CurrentUser.OpenSubKey(ProgIdKey + @"\shell\open\command");
            return string.Equals(key?.GetValue(null) as string, Command, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Windows opens .stl files with Photo Liker: the person's choice (in Settings, or "Always" in "Open with") is Photo Liker's.</summary>
        public bool IsDefault()
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserChoiceKey);
            return string.Equals(key?.GetValue("ProgId") as string, ProgId, StringComparison.OrdinalIgnoreCase);
        }

        /// <param name="typeName">What the kind of file is called (shown by Explorer in the column "Type"), in the language that is on.</param>
        /// <param name="description">What the program is, for the Default apps page of Settings.</param>
        public void Register(string typeName, string description)
        {
            var command = Command;
            RemoveStaleApplicationKey(command);

            using (var key = Registry.CurrentUser.CreateSubKey(ProgIdKey))
            {
                key.SetValue(null, typeName);
                using var icon = key.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"\"{_exePath}\",0");
                using var open = key.CreateSubKey(@"shell\open");
                open.SetValue("FriendlyAppName", "Photo Liker");
                using var openCommand = open.CreateSubKey("command");
                openCommand.SetValue(null, command);
            }

            // "Open with" lists the programs that have this ProgID under the extension; an empty value is all it needs
            using (var key = Registry.CurrentUser.CreateSubKey(OpenWithKey))
                key.SetValue(ProgId, string.Empty, RegistryValueKind.String);

            // Also by the name of the program, for the "Open with" list of a file that has another default already
            using (var key = Registry.CurrentUser.CreateSubKey(ApplicationKey))
            {
                key.SetValue("FriendlyAppName", "Photo Liker");
                using var openCommand = key.CreateSubKey(@"shell\open\command");
                openCommand.SetValue(null, command);
                using var supported = key.CreateSubKey("SupportedTypes");
                supported.SetValue(Extension, string.Empty, RegistryValueKind.String);
            }

            // The page of Photo Liker in Settings, Apps, Default apps
            using (var key = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
            {
                key.SetValue("ApplicationName", "Photo Liker");
                key.SetValue("ApplicationDescription", description);
                using var associations = key.CreateSubKey("FileAssociations");
                associations.SetValue(Extension, ProgId);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKey))
                key.SetValue(RegisteredName, _software + @"\" + RegisteredName + @"\Capabilities");

            Announce();
        }

        /// <summary>
        /// When the program was registered before by another file (see <see cref="ProgramPath"/>), the entry that was made by that file's name is taken away, so that the "Open with" list
        /// does not go on offering Photo Liker under the name of a program that cannot open the file. Only an entry that starts what this registered is touched.
        /// </summary>
        private void RemoveStaleApplicationKey(string newCommand)
        {
            string? old;
            using (var key = Registry.CurrentUser.OpenSubKey(ProgIdKey + @"\shell\open\command"))
                old = key?.GetValue(null) as string;

            if (old is null || string.Equals(old, newCommand, StringComparison.OrdinalIgnoreCase) || !old.StartsWith('"'))
                return;

            var end = old.IndexOf('"', 1);
            if (end < 2)
                return;

            var name = Path.GetFileName(old[1..end]);
            if (name.Length == 0 || string.Equals(name, Path.GetFileName(_exePath), StringComparison.OrdinalIgnoreCase))
                return;

            var stale = ClassesKey + @"\Applications\" + name;
            using (var key = Registry.CurrentUser.OpenSubKey(stale + @"\shell\open\command"))
            {
                if (!string.Equals(key?.GetValue(null) as string, old, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            Registry.CurrentUser.DeleteSubKeyTree(stale, throwOnMissingSubKey: false);
        }

        /// <summary>Takes Photo Liker out of the list again. Nothing else is touched: if it was the default, Windows asks again the next time an .stl file is opened.</summary>
        public void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(ProgIdKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(ApplicationKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesKey, throwOnMissingSubKey: false);

            // The key of the program itself goes when nothing else is in it
            using (var key = Registry.CurrentUser.OpenSubKey(_software + @"\" + RegisteredName))
            {
                if (key is { SubKeyCount: 0, ValueCount: 0 })
                {
                    key.Close();
                    Registry.CurrentUser.DeleteSubKey(_software + @"\" + RegisteredName, throwOnMissingSubKey: false);
                }
            }

            using (var key = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKey, writable: true))
                key?.DeleteValue(RegisteredName, throwOnMissingValue: false);
            using (var key = Registry.CurrentUser.OpenSubKey(OpenWithKey, writable: true))
                key?.DeleteValue(ProgId, throwOnMissingValue: false);

            Announce();
        }

        /// <summary>
        /// Opens the page of Photo Liker in Settings, Apps, Default apps (an older Windows opens the list of all of them). Returns null, or what went wrong.
        /// </summary>
        public static string? OpenDefaultAppsSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=" + RegisteredName) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                return ex.Message;
            }
        }

        /// <summary>Tells Explorer that the associations changed, so that its icons and its "Open with" lists are made again. Not for a test, which writes somewhere that is no association.</summary>
        private void Announce()
        {
            if (_real)
                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
