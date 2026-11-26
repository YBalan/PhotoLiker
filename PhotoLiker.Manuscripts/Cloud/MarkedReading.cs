using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>What an AI service wrote for one box of a photo whose lines were marked with numbered boxes: the number on the box (from 1) and the text in it.</summary>
    public sealed record MarkedLine(int Box, string Text);

    /// <summary>
    /// What an AI service read from a photo whose lines were marked with numbered boxes (see <see cref="LineMarker"/>): <see cref="Text"/> is the text of the page, and <see cref="Lines"/> the text of
    /// each box, in the order of the numbers. <see cref="Lines"/> is null when the answer could not be used box by box (it was plain text, or no entry had a number of a box that is on the photo): the
    /// text is still its reading, and the page has no boxes.
    /// </summary>
    public sealed record MarkedReading(string Text, IReadOnlyList<MarkedLine>? Lines);

    /// <summary>
    /// Makes a <see cref="MarkedReading"/> of the answer of a service that was asked for JSON: <c>{"lines":[{"box":1,"text":"..."},{"box":2,"text":"..."}]}</c>, an entry for each box that has text in it.
    /// Services do not always do as they are told, so only the least that is useful is taken: an answer that is not JSON is the text; one that was cut in the middle gives the entries that are whole.
    /// </summary>
    internal static partial class MarkedAnswer
    {
        private readonly record struct Entry(int Box, string Text);

        /// <summary>An object of the answer that has no object inside it: an entry (the answer may have been cut after any of them).</summary>
        [GeneratedRegex(@"\{[^{}]*\}", RegexOptions.CultureInvariant)]
        private static partial Regex WholeObject();

        /// <param name="boxCount">How many boxes are on the photo: the numbers of the entries are 1 to this.</param>
        public static MarkedReading Parse(string answer, int boxCount)
        {
            var raw = answer.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            var entries = FromJson(raw) ?? Salvage(raw);
            if (entries is null)
                return new MarkedReading(CloudReader.Clean(answer), null);

            // The entries that are for a box of the photo, one for each, in the order of the numbers; a text with a line break in it is one line
            static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').TrimEnd(' ');
            var lines = entries
                .Where(entry => entry.Box >= 1 && entry.Box <= boxCount && OneLine(entry.Text).Trim().Length > 0)
                .GroupBy(entry => entry.Box)
                .Select(group => new MarkedLine(group.Key, OneLine(group.First().Text)))
                .OrderBy(line => line.Box)
                .ToList();

            if (lines.Count == 0 && entries.Any(entry => OneLine(entry.Text).Trim().Length > 0))
            {
                // Texts, but not one for a box of the photo (the numbers are not the ones that were asked for): the text is all there is
                return new MarkedReading(CloudReader.Clean(string.Join('\n', entries.Select(entry => OneLine(entry.Text)))), null);
            }

            return new MarkedReading(CloudReader.Clean(string.Join('\n', lines.Select(line => line.Text))), lines);
        }

        /// <summary>The entries of the JSON in the answer (it may have words or a code block round it), or null when there is no JSON that can be read.</summary>
        private static List<Entry>? FromJson(string raw)
        {
            var start = raw.IndexOfAny(['{', '[']);
            var end = raw.LastIndexOfAny(['}', ']']);
            if (start < 0 || end <= start)
                return null;

            try
            {
                using var document = JsonDocument.Parse(raw[start..(end + 1)]);
                var root = document.RootElement;
                JsonElement array;
                if (root.ValueKind == JsonValueKind.Array)
                    array = root;
                else if (root.ValueKind == JsonValueKind.Object && Property(root, "lines") is { ValueKind: JsonValueKind.Array } lines)
                    array = lines;
                else
                    return null;

                var entries = new List<Entry>();
                foreach (var item in array.EnumerateArray())
                {
                    if (EntryOf(item) is { } entry)
                        entries.Add(entry);
                }

                // An array with nothing of an entry in it is not an answer in this form ("[1]" or "[?]" are words of a page, not JSON)
                return root.ValueKind == JsonValueKind.Array && entries.Count == 0 ? null : entries;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>An answer that was cut off in the middle still has the entries it got to: those that are whole.</summary>
        private static List<Entry>? Salvage(string raw)
        {
            var entries = new List<Entry>();
            foreach (Match match in WholeObject().Matches(raw))
            {
                try
                {
                    using var document = JsonDocument.Parse(match.Value);
                    if (EntryOf(document.RootElement) is { } entry)
                        entries.Add(entry);
                }
                catch (JsonException)
                {
                    // an object that is not JSON is not an entry
                }
            }

            return entries.Count > 0 ? entries : null;
        }

        /// <summary>An entry: an object with a number (<c>box</c>, or what a service may call it) and a text.</summary>
        private static Entry? EntryOf(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            var number = new[] { "box", "n", "number", "id", "index" }.Select(name => Property(item, name)).FirstOrDefault(value => value is not null);
            if (number is not { } value || Property(item, "text") is not { ValueKind: JsonValueKind.String } text)
                return null;

            var box = value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var whole) => whole,
                JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
                _ => 0,
            };
            return new Entry(box, text.GetString() ?? string.Empty);
        }

        /// <summary>The property of an object by its name, whatever the case of its letters.</summary>
        private static JsonElement? Property(JsonElement obj, string name)
        {
            foreach (var property in obj.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            }

            return null;
        }
    }
}
