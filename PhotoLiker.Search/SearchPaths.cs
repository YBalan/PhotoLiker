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

        /// <summary>Name of the copy of the groups of people that a photo folder keeps, so that the groups travel with the photos.</summary>
        public const string GroupsFileName = "photo-liker-groups.json";

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

        /// <summary>The copy of the groups of people in a photo folder (the groups themselves are one set for all folders: <see cref="PeopleGroupsFile"/>).</summary>
        public static string GroupsFile(string photoFolder) => Path.Combine(Normalize(photoFolder), GroupsFileName);

        /// <summary>%LocalAppData%\PhotoLiker\people.json: what every photo folder knows about its people, so that a new folder knows them too.</summary>
        public static string SharedPeopleFile => Path.Combine(AppPaths.DataFolder, SharedPeopleFileName);

        /// <summary>
        /// %LocalAppData%\PhotoLiker\people-groups.json: the groups the user sorted the people into ("Family", "Friends"). One file for all photo
        /// folders, because a person is the same person in every folder; every folder with people in it keeps a copy (<see cref="GroupsFile"/>),
        /// and the one saved later is the set (see <see cref="PersonGroups"/>).
        /// </summary>
        public static string PeopleGroupsFile => Path.Combine(AppPaths.DataFolder, "people-groups.json");

        internal static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }
}
