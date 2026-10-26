using System.Reflection;

namespace PhotoLikerUI
{
    /// <summary>
    /// Version and build date of the running application. The assembly version is a wildcard (see the csproj), so the
    /// compiler stamps it on every build, and the build date is read back from it.
    /// </summary>
    internal static class AppVersionInfo
    {
        public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

        // For a "1.0.*" version the compiler sets Build to the days since 2000-01-01 and Revision to the seconds
        // since local midnight divided by two.
        public static DateTime BuildDate { get; } = new DateTime(2000, 1, 1)
            .AddDays(Version.Build)
            .AddSeconds(Version.Revision * 2);
    }
}
