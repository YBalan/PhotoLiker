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

        /// <summary>
        /// The release this build is: the release workflow stamps the build with the version of its tag (<c>-p:Version=2.1.0</c>), which ends up in
        /// the informational version. Null for any other build (the project's default is 0.0.0), which is not a release.
        /// </summary>
        public static Version? ReleaseVersion { get; } = UpdateChecker.ReleaseVersionOf(
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

        /// <summary>What the About box calls the version: the release (2.1.0) or, for a build that is no release, the assembly version.</summary>
        public static string DisplayVersion { get; } = ReleaseVersion?.ToString(3) ?? Version.ToString();
    }
}
