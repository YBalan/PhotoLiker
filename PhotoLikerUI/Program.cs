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
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportUiException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                CrashLog.Write("Unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                CrashLog.Write("Unobserved task exception", e.Exception);
                e.SetObserved();
            };

            string? folderArg = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : null;
            Application.Run(new MainForm(folderArg));
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
                    $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Details were saved to:{Environment.NewLine}{CrashLog.FilePath}",
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
