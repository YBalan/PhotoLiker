namespace PhotoLikerUI
{
    internal static class Program
    {
        private static bool _errorDialogOpen;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Before anything can load an old copy of it (a shell extension does, when a dialog opens): the search models crash with that
            PhotoLiker.Search.NativeRuntime.PreferWindowsRuntime();

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            MainForm.SetLanguage(AppLanguage.Find(GlobalConfig.Load().Language)); // before the first text is looked up
            SettingsPresentation.Register();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportUiException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                CrashLog.Write("Unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                CrashLog.Write("Unobserved task exception", e.Exception);
                e.SetObserved();
            };

            // A folder (the "Open in Photo Liker" entry of the context menu) opens as it is; a file (double-clicked in Explorer, or chosen in "Open with") opens the folder it is in, at that file
            var (folderArg, fileArg) = StartTarget.Of(args);
            Application.Run(new MainForm(folderArg, fileArg));
        }

        private static void ReportUiException(Exception exception)
        {
            CrashLog.Write("UI thread exception", exception);

            // A failing paint can raise this repeatedly; don't stack dialogs.
            if (_errorDialogOpen) return;
            _errorDialogOpen = true;
            try
            {
                MessageBox.Show(
                    string.Format(DialogStrings.CrashDetailsFormat, exception.Message, CrashLog.FilePath),
                    "PhotoLiker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _errorDialogOpen = false;
            }
        }
    }
}
