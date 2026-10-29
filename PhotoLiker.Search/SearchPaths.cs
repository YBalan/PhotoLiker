using System.Security.Cryptography;
using System.Text;
using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>Where the search feature keeps its files.</summary>
    public static class SearchPaths
    {
        /// <summary>Name of the file with the named faces, kept in the photo folder so it travels with the photos.</summary>
        public const string PeopleFileName = "photo-liker-people.json";

        /// <summary>Name of the file that collects the named faces of all photo folders, kept in the app data folder.</summary>
        public const string SharedPeopleFileName = "people.json";

        /// <summary>The downloaded models: %LocalAppData%\PhotoLiker\models.</summary>
        public static string ModelsFolder => Path.Combine(AppPaths.DataFolder, "models");

        /// <summary>
        /// The index of a photo folder is a cache that can always be rebuilt, so it lives on this machine
        /// (SQLite does not get along with network shares), one file per folder.
        /// </summary>
        public static string IndexFile(string photoFolder)
        {
            // Windows paths do not care about case, and neither does the index of a folder
            var full = Normalize(photoFolder).ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..10];

            var name = new string(Path.GetFileName(full).Where(char.IsLetterOrDigit).Take(24).ToArray());
            return Path.Combine(AppPaths.DataFolder, "index", $"{(name.Length > 0 ? name : "root")}-{hash}.db");
        }

        public static string PeopleFile(string photoFolder) => Path.Combine(Normalize(photoFolder), PeopleFileName);

        /// <summary>%LocalAppData%\PhotoLiker\people.json: what every photo folder knows about its people, so that a new folder knows them too.</summary>
        public static string SharedPeopleFile => Path.Combine(AppPaths.DataFolder, SharedPeopleFileName);

        internal static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }
}
