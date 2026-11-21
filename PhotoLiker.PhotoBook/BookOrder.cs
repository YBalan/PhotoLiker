namespace PhotoLiker.PhotoBook
{
    /// <summary>
    /// The order of the photos of a book, and the ways to change it: where the photos start (the order kept with the folder, with new photos added at the end), and the moves of the thumbnail
    /// view (drag a block of photos to a place, one step earlier or later, to the start or the end, reverse, sort). Each move gives the places the moved photos are at afterwards, so the view
    /// can keep them selected.
    /// </summary>
    public static class BookOrder
    {
        /// <summary>
        /// The photos of a folder as the book has them: the ones the saved book lists (and that are still in the folder) in the saved order, with what was written for them; then the photos that are
        /// new to the folder, in <paramref name="order"/>. With no saved book, all of them in <paramref name="order"/>. A photo the saved book names that is gone is left out (and its text with it).
        /// </summary>
        /// <param name="listed">The photos of the source.</param>
        /// <param name="saved">The entries of the book as it was saved (their paths made full), or null.</param>
        public static List<BookEntry> Arrange(IReadOnlyList<PhotoFile> listed, IReadOnlyList<BookEntry>? saved, PhotoSortOrder order)
        {
            var sorted = listed.Where(photo => !photo.IsVideo).ToList();
            PhotoScanner.Sort(sorted, order);

            var byPath = new Dictionary<string, PhotoFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var photo in sorted)
                byPath.TryAdd(photo.OriginalFilePath, photo);

            var result = new List<BookEntry>(sorted.Count);
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in saved ?? [])
            {
                if (byPath.TryGetValue(entry.Path, out var photo) && placed.Add(photo.OriginalFilePath))
                {
                    entry.Path = photo.OriginalFilePath; // the path as the listing spells it
                    result.Add(entry);
                }
            }

            foreach (var photo in sorted)
            {
                if (placed.Add(photo.OriginalFilePath))
                    result.Add(new BookEntry(photo.OriginalFilePath));
            }

            return result;
        }

        /// <summary>
        /// Puts the photos at <paramref name="selected"/> where <paramref name="insertBefore"/> is (a place in the list as it is now; the count of the list is the end), in the order they have, and
        /// returns the places they are at after. Dropping them where they already are changes nothing.
        /// </summary>
        public static int[] Move<T>(List<T> items, IReadOnlyCollection<int> selected, int insertBefore)
        {
            var chosen = selected.Where(index => index >= 0 && index < items.Count).Distinct().Order().ToList();
            if (chosen.Count == 0)
                return [];

            insertBefore = Math.Clamp(insertBefore, 0, items.Count);

            // How many of the moved photos are in front of the place: the place shifts back by that many once they are taken out
            var before = chosen.Count(index => index < insertBefore);
            var moved = chosen.Select(index => items[index]).ToList();
            for (var i = chosen.Count - 1; i >= 0; i--)
                items.RemoveAt(chosen[i]);

            var at = insertBefore - before;
            items.InsertRange(at, moved);
            return [.. Enumerable.Range(at, moved.Count)];
        }

        /// <summary>
        /// Moves each photo at <paramref name="selected"/> by <paramref name="places"/> (negative: earlier, positive: later), one place at a time: a photo that has another photo of the selection
        /// or the end of the list in front of it stays, so a block that is at the end stays and the others close up behind it. Returns the places the photos are at after.
        /// </summary>
        public static int[] Nudge<T>(List<T> items, IReadOnlyCollection<int> selected, int places)
        {
            var flags = new bool[items.Count];
            foreach (var index in selected.Where(index => index >= 0 && index < items.Count))
                flags[index] = true;

            for (var step = 0; step < Math.Abs(places); step++)
            {
                if (places < 0)
                {
                    for (var i = 1; i < items.Count; i++)
                    {
                        if (flags[i] && !flags[i - 1])
                            Swap(items, flags, i, i - 1);
                    }
                }
                else
                {
                    for (var i = items.Count - 2; i >= 0; i--)
                    {
                        if (flags[i] && !flags[i + 1])
                            Swap(items, flags, i, i + 1);
                    }
                }
            }

            return [.. Enumerable.Range(0, items.Count).Where(index => flags[index])];
        }

        private static void Swap<T>(List<T> items, bool[] flags, int a, int b)
        {
            (items[a], items[b]) = (items[b], items[a]);
            (flags[a], flags[b]) = (flags[b], flags[a]);
        }

        public static int[] ToStart<T>(List<T> items, IReadOnlyCollection<int> selected) => Move(items, selected, 0);

        public static int[] ToEnd<T>(List<T> items, IReadOnlyCollection<int> selected) => Move(items, selected, items.Count);

        /// <summary>The photos as they are at <paramref name="selected"/> go to place <paramref name="number"/> (counting from 1 as the person does): the first of them is at that place afterwards.</summary>
        public static int[] ToPlace<T>(List<T> items, IReadOnlyCollection<int> selected, int number)
        {
            var chosen = selected.Where(index => index >= 0 && index < items.Count).Distinct().Order().ToList();
            if (chosen.Count == 0)
                return [];

            var first = Math.Clamp(number - 1, 0, items.Count - chosen.Count);

            // Taken out, the others are in the list without them; the block is put in so that its first photo is at the place asked for
            var others = items.Where((_, index) => !chosen.Contains(index)).ToList();
            var moved = chosen.Select(index => items[index]).ToList();
            others.InsertRange(first, moved);
            items.Clear();
            items.AddRange(others);
            return [.. Enumerable.Range(first, moved.Count)];
        }

        public static void Reverse<T>(List<T> items) => items.Reverse();

        /// <summary>Puts the photos in an order by their files (the dates and the names the files have now). <see cref="PhotoSortOrder.None"/> leaves them.</summary>
        public static void Sort(List<BookEntry> entries, PhotoSortOrder order)
        {
            if (order == PhotoSortOrder.None)
                return;

            var files = entries.Select(entry =>
            {
                var info = new FileInfo(entry.Path);
                return new PhotoFile(entry.Path, string.Empty)
                {
                    Length = info.Exists ? info.Length : 0,
                    LastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : default,
                    CreationTimeUtc = info.Exists ? info.CreationTimeUtc : default,
                };
            }).ToList();

            PhotoScanner.Sort(files, order);

            var byPath = entries.GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => new Queue<BookEntry>(group), StringComparer.OrdinalIgnoreCase);
            entries.Clear();
            foreach (var file in files)
                entries.Add(byPath[file.OriginalFilePath].Dequeue());
        }
    }
}
