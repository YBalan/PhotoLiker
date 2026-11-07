namespace PhotoLikerUI
{
    /// <summary>
    /// The lines of the list of keys in the About box, in a font where every letter is as wide as the others: the keys in a column on the left, what they do on the right. A key list or a
    /// text that does not fit in the room (the box must not be wider than the monitor) goes on to the next line, at a space, and stays in its own column; a word longer than the column is cut.
    /// It has no window in it, so it is tested alone.
    /// </summary>
    internal static class ShortcutLines
    {
        /// <summary>Spaces between the column of keys and the column of what they do.</summary>
        public const int ColumnGap = 2;

        /// <summary>The column of keys is as wide as the longest key, but not wider than this many characters (a long list of keys goes on to more lines in it), or it would push every other row away.</summary>
        public const int MaxKeyChars = 24;

        /// <summary>What the keys do is never squeezed into less than this many characters, however little room there is.</summary>
        public const int MinActionChars = 24;

        /// <summary>
        /// The rows of the list, none longer than <paramref name="maxChars"/> characters unless the room is smaller than the narrowest the list can be made
        /// (<see cref="MaxKeyChars"/> + <see cref="ColumnGap"/> + <see cref="MinActionChars"/> at most). A row of a key that is wrapped has the rest of its keys under them, and the rest of its text under its text.
        /// </summary>
        public static string[] Layout(IReadOnlyList<(string Input, string Action)> shortcuts, int maxChars)
        {
            if (shortcuts.Count == 0)
                return [];

            var keyChars = Math.Min(shortcuts.Max(shortcut => shortcut.Input.Length), MaxKeyChars);
            var column = keyChars + ColumnGap;
            var room = Math.Max(MinActionChars, maxChars - column);

            var rows = new List<string>();
            foreach (var (input, action) in shortcuts)
            {
                var keys = Wrap(input, keyChars);
                var text = Wrap(action, room);
                for (var line = 0; line < Math.Max(keys.Count, text.Count); line++)
                {
                    var key = line < keys.Count ? keys[line] : string.Empty;
                    var what = line < text.Count ? text[line] : string.Empty;
                    rows.Add(what.Length == 0 ? key : key.PadRight(column) + what);
                }
            }
            return [.. rows];
        }

        /// <summary>The width in characters of the longest row.</summary>
        public static int WidestRow(IEnumerable<string> rows) => rows.Select(row => row.Length).DefaultIfEmpty(0).Max();

        /// <summary>Breaks <paramref name="text"/> into lines of at most <paramref name="width"/> characters at its spaces; a word that is longer than a line is cut. Always at least one line.</summary>
        public static List<string> Wrap(string text, int width)
        {
            width = Math.Max(1, width);
            var lines = new List<string>();
            var current = string.Empty;

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var rest = word;
                while (rest.Length > width)
                {
                    // A word too long for any line: it fills the line it comes to, and what is left goes on
                    if (current.Length > 0)
                    {
                        lines.Add(current);
                        current = string.Empty;
                    }
                    lines.Add(rest[..width]);
                    rest = rest[width..];
                }

                if (current.Length == 0)
                    current = rest;
                else if (current.Length + 1 + rest.Length <= width)
                    current += " " + rest;
                else
                {
                    lines.Add(current);
                    current = rest;
                }
            }

            if (current.Length > 0 || lines.Count == 0)
                lines.Add(current);
            return lines;
        }
    }
}
