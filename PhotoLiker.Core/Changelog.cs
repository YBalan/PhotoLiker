using System.Text;

namespace PhotoLiker.Core
{
    /// <summary>What one release changed: its version (null for the section "Unreleased") and one sentence per feature or fix.</summary>
    public sealed record ChangelogSection(Version? Version, IReadOnlyList<string> Changes);

    /// <summary>
    /// CHANGELOG.md at the root of the repository, which is where the sentences of every release are written. The release workflow puts the section of the version on the GitHub
    /// release and attaches the whole file to it; the About box downloads that file (<see cref="UpdateChecker.GetChangelogAsync"/>) and shows what changed in the versions newer than
    /// the one that is running. The format is deliberately plain: a heading <c>## 2.1.0 - 2026-11-30</c> (or <c>## Unreleased</c>) and, under it, one line that starts with
    /// <c>- </c> for each change. Anything else in the file is prose for people and is ignored here.
    /// </summary>
    public static class Changelog
    {
        public const string FileName = "CHANGELOG.md";

        /// <summary>The heading of the section whose changes are not released yet.</summary>
        public const string UnreleasedHeading = "Unreleased";

        private const char Bullet = '•';
        private const char ByteOrderMark = (char)0xFEFF;

        /// <summary>The sections of the text, in the order they are written. A heading that is neither a version nor Unreleased holds nothing: its lines are skipped.</summary>
        public static IReadOnlyList<ChangelogSection> Parse(string? text)
        {
            var sections = new List<ChangelogSection>();
            var inSection = false;
            Version? version = null;
            List<string> changes = [];

            foreach (var raw in (text ?? string.Empty).TrimStart(ByteOrderMark).Split('\n'))
            {
                var line = raw.Trim();

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (inSection)
                        sections.Add(new ChangelogSection(version, changes));

                    inSection = TryReadHeading(line[3..], out version);
                    changes = [];
                }
                else if (inSection && TryReadBullet(line, out var change))
                {
                    changes.Add(change);
                }
            }

            if (inSection)
                sections.Add(new ChangelogSection(version, changes));

            return sections;
        }

        /// <summary>
        /// What changed after <paramref name="installed"/> up to and including <paramref name="latest"/>, newest version first: the sections of the versions in between that have a
        /// change. A copy that is no release (<paramref name="installed"/> is null) has no number to count from: it gets the latest version alone.
        /// </summary>
        public static IReadOnlyList<ChangelogSection> Between(IEnumerable<ChangelogSection> sections, Version? installed, Version latest) =>
        [
            .. sections
                .Where(section => section.Changes.Count > 0 && section.Version is { } version
                    && !UpdateChecker.IsNewer(version, latest)
                    && (installed is null ? !UpdateChecker.IsNewer(latest, version) : UpdateChecker.IsNewer(version, installed)))
                .OrderByDescending(section => section.Version)
        ];

        /// <summary>
        /// The sections as lines of text, a bullet in front of each change. With <paramref name="heading"/> every section starts with its line (the version it is for), and a blank
        /// line is between the sections; without it the changes are one list.
        /// </summary>
        public static string Format(IReadOnlyList<ChangelogSection> sections, Func<Version, string>? heading = null)
        {
            var text = new StringBuilder();
            foreach (var section in sections)
            {
                if (text.Length > 0 && heading is not null)
                    text.AppendLine();

                if (heading is not null && section.Version is not null)
                    text.AppendLine(heading(section.Version));

                foreach (var change in section.Changes)
                    text.Append(Bullet).Append(' ').AppendLine(change);
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>"2.1.0 - 2026-11-30", "v2.1.0", "[2.1.0]" and "Unreleased" are headings; what follows the first word is for people (the date).</summary>
        private static bool TryReadHeading(string heading, out Version? version)
        {
            version = null;
            var word = heading.Trim().Split(' ', '\t')[0].Trim('[', ']');
            if (word.Equals(UnreleasedHeading, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!UpdateChecker.TryParseVersion(word, out var parsed))
                return false;

            version = parsed;
            return true;
        }

        private static bool TryReadBullet(string line, out string change)
        {
            change = string.Empty;
            if (!line.StartsWith("- ", StringComparison.Ordinal) && !line.StartsWith("* ", StringComparison.Ordinal))
                return false;

            change = line[2..].Trim();
            return change.Length > 0;
        }
    }
}
