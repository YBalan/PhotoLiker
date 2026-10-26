namespace PhotoLiker.Core
{
    public static class AppPaths
    {
        /// <summary>
        /// Environment variable that moves <see cref="DataFolder"/> elsewhere. The tests set it, so that running them
        /// never touches the real global config of the person running them.
        /// </summary>
        public const string DataFolderVariable = "PHOTOLIKER_DATA_DIR";

        /// <summary>Where the app keeps its own files (global config, crash log): %LocalAppData%\PhotoLiker.</summary>
        public static readonly string DataFolder =
            Environment.GetEnvironmentVariable(DataFolderVariable) is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker");
    }
}
