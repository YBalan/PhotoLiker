namespace PhotoLikerUI
{
    /// <summary>
    /// What the About box needs from outside the program to update it: GitHub, the folder of the download, the setup, the questions asked.
    /// Each is a property so that a test (the smoke tour) can put its own in place: no test then goes to the network or starts a setup.
    /// </summary>
    internal static class UpdateLauncher
    {
        /// <summary>
        /// Silent: the setup shows its progress and nothing to click, in the language of Windows. /Relaunch=1 makes it start the program again
        /// when it is done (see PhotoLiker.iss), /NORESTARTAPPLICATIONS leaves that to this one entry so that it does not start twice.
        /// </summary>
        public const string SetupArguments = "/SILENT /SP- /NORESTARTAPPLICATIONS /Relaunch=1";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) }; // for the answer of GitHub; the download is limited by the About box closing

        /// <summary>Asks GitHub for the latest release and downloads its setup.</summary>
        public static UpdateChecker Checker { get; set; } = new(Http);

        /// <summary>The release this program is; null for a build that is no release (one run from the source), which is never offered a setup.</summary>
        public static Version? InstalledVersion { get; set; } = AppVersionInfo.ReleaseVersion;

        /// <summary>
        /// Whether the setup put this copy here: it leaves unins000.exe beside the program. Only such a copy is replaced by a new setup; for any
        /// other (the zip, a build from the source) the setup puts a second copy elsewhere, so the About box asks before it does that.
        /// </summary>
        public static bool InstalledBySetup { get; set; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

        /// <summary>Where the setup of a new version is downloaded to.</summary>
        public static string DownloadFolder { get; set; } = Path.Combine(Path.GetTempPath(), "PhotoLiker-update");

        /// <summary>Starts the setup (its path); the reason it did not start, or null.</summary>
        public static Func<string, string?> RunSetup { get; set; } = setup => ShellOpen.Run(setup, SetupArguments);

        /// <summary>Opens a web page (the release); the reason it did not open, or null.</summary>
        public static Func<string, string?> OpenPage { get; set; } = ShellOpen.Url;

        /// <summary>Asks a yes or no question over the About box.</summary>
        public static Func<IWin32Window, string, bool> Confirm { get; set; } = (owner, text) =>
            MessageBox.Show(owner, text, MainFormStrings.AboutTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }
}
