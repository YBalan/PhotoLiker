using System.Text.RegularExpressions;

namespace PhotoLiker.Core
{
    /// <summary>
    /// How much a folder says that a photo belongs there. The copy in the folder with the highest rank stays; copies that
    /// share the highest rank are left for a person to decide.
    /// </summary>
    public enum FolderRank
    {
        /// <summary>A folder that is meant to be emptied (Deleted), or one the person named as a place to take copies from.</summary>
        Avoid = 0,

        /// <summary>Only a camera's own folder name (107_2311, 100CANON): where a card was copied to, not a place somebody chose.</summary>
        Counter = 1,

        /// <summary>A folder with a date in its name, or a name that means something to a person.</summary>
        Named = 2,

        /// <summary>A folder the person named as a place to keep copies.</summary>
        Keep = 3,
    }

    /// <param name="Rank">What the folder says about the copy.</param>
    /// <param name="Name">What the name of the folder alone says (named, or a camera's): between copies of the same rank, the better name wins, so that in a Deleted folder too a folder of a date beats one of a card.</param>
    /// <param name="Clean">The file is not named like a copy ("IMG_1234 - копия", "IMG_1234 (2)"): among copies in equally good folders the one that is not a copy by its name stays.</param>
    public sealed record DuplicateCopy(string Path, FolderRank Rank, string Reason, FolderRank Name = FolderRank.Counter, long Length = 0, bool Clean = true);

    /// <summary>One group of copies and what to do with them: the copy at <paramref name="Keeper"/> stays and the others are moved away; null if the rules do not decide.</summary>
    public sealed record DuplicatePlan(DuplicateGroup Group, IReadOnlyList<DuplicateCopy> Copies, int? Keeper)
    {
        public bool Decided => Keeper is not null;

        public IEnumerable<DuplicateCopy> ToMove => Keeper is { } keeper ? Copies.Where((_, i) => i != keeper) : [];

        /// <summary>The same group with the copy of this path as the one that stays (a person's choice); this plan if it has no such copy.</summary>
        public DuplicatePlan KeepingCopy(string path)
        {
            var index = Copies.ToList().FindIndex(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? this : this with { Keeper = index };
        }
    }

    /// <summary>Parts of a folder path, separated by ';': a copy in a folder whose path has such a part is kept, or moved away. <c>*</c> and <c>?</c> stand for any characters, and a part without them is just text to find.</summary>
    public sealed record DuplicateOptions(IReadOnlyList<string> KeepPatterns, IReadOnlyList<string> MovePatterns)
    {
        public static readonly DuplicateOptions Default = new([], []);

        public static IReadOnlyList<string> Split(string? text) =>
            [.. (text ?? string.Empty).Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>
    /// Decides which copy of a photo stays, from the folders they are in, the way a person who sorted the photos by hand would: a folder
    /// named for a date, or for something (a person, a trip), is where somebody put the photo on purpose; a folder that is only what
    /// the camera called the card (<c>107_2311</c>, <c>314CANON</c>) is where it landed, and a Deleted folder is where it was put to be thrown away.
    /// </summary>
    public static class DuplicateRules
    {
        private static readonly string[] DiscardNames = ["Deleted", "Duplicated", "Trash"];

        // Folders that say nothing: every photo is in one, and a new one is made by Windows
        private static readonly string[] GenericNames = ["photo", "photos", "pictures", "images", "dcim", "camera", "camera roll", "new folder", "untitled folder", "temp", "tmp", "новая папка"];

        private static readonly Regex[] DateNames =
        [
            new(@"(?<!\d)(19|20)\d{2}[-._ ]?(0[1-9]|1[0-2])([-._ ]?(0[1-9]|[12]\d|3[01]))?(?!\d)", RegexOptions.CultureInvariant),        // 2014-11-23, 2014_11, 20141123
            new(@"(?<!\d)(0[1-9]|[12]\d|3[01])[-._ ](0[1-9]|1[0-2])[-._ ](19|20)\d{2}(?!\d)", RegexOptions.CultureInvariant),            // 23.11.2014
            new(@"^(19|20)\d{2}$", RegexOptions.CultureInvariant),                                                                      // 2014
        ];

        private static readonly Regex[] CounterNames =
        [
            new(@"^\d{3}_{1,3}\d{2,4}$", RegexOptions.CultureInvariant),                      // 107_2311, 135___07
            new(@"^\d{2,4}_?[A-Za-z]{3,10}\d{0,4}$", RegexOptions.CultureInvariant),          // 314CANON, 100NIKON, 100MSDCF
            new(@"^\d{1,4}$", RegexOptions.CultureInvariant),                                 // 102
            new(@"^(IMG|DSC|DSCN|DSCF|PICT|MVI|VID)[-_]?\d*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        ];

        /// <summary>One plan for every group, in the order of the groups.</summary>
        /// <param name="root">The photo folder: only the path below it counts, and the part of it that all the copies share counts for nothing.</param>
        public static IReadOnlyList<DuplicatePlan> Plan(IReadOnlyList<DuplicateGroup> groups, string root, DuplicateOptions options)
        {
            var keep = Patterns(options.KeepPatterns);
            var move = Patterns(options.MovePatterns);

            // One folder is classified once, however many copies it holds
            var folders = groups.SelectMany(g => g.Paths).Select(p => Path.GetDirectoryName(p) ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(f => f, f => Segments(root, f), StringComparer.OrdinalIgnoreCase);
            var shared = SharedPrefix([.. folders.Values]);
            var ranks = folders.ToDictionary(
                f => f.Key,
                f => RankOf(f.Value[shared..], Path.GetRelativePath(root, f.Key), keep, move),
                StringComparer.OrdinalIgnoreCase);

            var plans = new List<DuplicatePlan>(groups.Count);
            foreach (var group in groups)
            {
                var copies = group.Paths.Select((p, index) =>
                {
                    var (rank, name, reason) = ranks[Path.GetDirectoryName(p) ?? string.Empty];
                    var clean = !IsCopyName(Path.GetFileNameWithoutExtension(p));
                    return new DuplicateCopy(p, rank, clean ? reason : reason + "; named like a copy", name, group.Lengths[index], clean);
                }).ToList();
                var best = copies.Max(c => (c.Rank, c.Name, c.Clean));
                var winners = copies.Where(c => (c.Rank, c.Name, c.Clean) == best).ToList();
                plans.Add(new DuplicatePlan(group, copies, winners.Count == 1 ? copies.IndexOf(winners[0]) : null));
            }
            return plans;
        }

        /// <summary>
        /// A photo in a Liked folder or a group folder ("A - Group Family") is a copy made on purpose, a photo somebody picked: it is no clutter to clean up,
        /// whatever the folder it was copied from holds. Found by the names of the folders, so it also holds when the Liked folder of the photo folder is another one.
        /// </summary>
        public static bool IsDeliberateCopy(string root, string photoPath)
        {
            return Segments(root, Path.GetDirectoryName(photoPath) ?? string.Empty)
                .Any(s => string.Equals(s, AppStrings.DefaultLikedFolderName, StringComparison.OrdinalIgnoreCase) || PhotoGroups.TryGetKey(s, out _));
        }

        /// <summary>The rank of the folder with these parts of path below the photo folder, and why.</summary>
        public static (FolderRank Rank, FolderRank Name, string Reason) RankOf(IReadOnlyList<string> segments, string relativeFolder, IReadOnlyList<Regex> keepPatterns, IReadOnlyList<Regex> movePatterns)
        {
            var (name, nameReason) = NameRank(segments);

            if (keepPatterns.Any(p => p.IsMatch(relativeFolder)))
                return (FolderRank.Keep, name, "the folder is one to keep copies in");
            if (movePatterns.Any(p => p.IsMatch(relativeFolder)))
                return (FolderRank.Avoid, name, "the folder is one to move copies out of");
            if (segments.FirstOrDefault(s => DiscardNames.Contains(s, StringComparer.OrdinalIgnoreCase)) is { } discard)
                return (FolderRank.Avoid, name, $"inside the folder '{discard}' ({nameReason})");

            return (name, name, nameReason);
        }

        private static (FolderRank Rank, string Reason) NameRank(IReadOnlyList<string> allSegments)
        {
            // "Deleted" is no name that somebody chose for the photos in it
            var segments = allSegments.Where(s => !DiscardNames.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();

            if (segments.FirstOrDefault(IsDateName) is { } dated)
                return (FolderRank.Named, $"folder named for a date: '{dated}'");
            if (segments.FirstOrDefault(IsMeaningful) is { } named)
                return (FolderRank.Named, $"folder with a name of its own: '{named}'");
            if (segments.FirstOrDefault(IsCounterName) is { } counter)
                return (FolderRank.Counter, $"only a camera's folder name: '{counter}'");

            return (FolderRank.Counter, "no folder of its own");
        }

        // What Windows and other programs add to the name of a file that was copied into the folder it is in
        private static readonly Regex[] CopyNames =
        [
            new(@"\s-\s(copy|копия)(\s\(\d+\))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),   // "IMG_1 - Copy", "IMG_1 - копия (2)"
            new(@"\s\(\d+\)$", RegexOptions.CultureInvariant),                                                                           // "IMG_1 (2)"
            new(@"^(copy of|копия)\s", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),                // "Copy of IMG_1", "Копия IMG_1"
            new(@"[\s_-]copy\d*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),                                             // "IMG_1_copy", "IMG_1 copy2"
        ];

        /// <summary>A file name (without its extension) that says the file is a copy of another one.</summary>
        public static bool IsCopyName(string name) => CopyNames.Any(r => r.IsMatch(name));

        public static bool IsDateName(string name) => DateNames.Any(r => r.IsMatch(name));

        public static bool IsCounterName(string name) => CounterNames.Any(r => r.IsMatch(name));

        /// <summary>A folder name that means something to a person: not a date, not a camera's counter, not one every photo is in.</summary>
        public static bool IsMeaningful(string name) =>
            !IsDateName(name) && !IsCounterName(name)
            && !GenericNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            && name.Any(char.IsLetter);

        /// <summary>Regular expressions for patterns written with * and ? (anywhere in the path); a pattern without them is text to find.</summary>
        public static IReadOnlyList<Regex> Patterns(IReadOnlyList<string> patterns) =>
        [
            .. patterns.Where(p => p.Length > 0).Select(p => new Regex(
                Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", "."),
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)),
        ];

        private static string[] Segments(string root, string folder)
        {
            var relative = Path.GetRelativePath(root, folder);
            return relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? []
                : relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>How many parts of path all the folders start with.</summary>
        private static int SharedPrefix(IReadOnlyList<string[]> folders)
        {
            if (folders.Count == 0)
                return 0;

            var shared = folders.Min(f => f.Length);
            for (var i = 0; i < shared; i++)
            {
                if (folders.Any(f => !string.Equals(f[i], folders[0][i], StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
            return shared;
        }
    }
}
