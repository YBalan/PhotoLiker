namespace PhotoLiker.Search
{
    /// <summary>The names of the people in a photo as one line of text, for a caption (the slideshow writes it on the photo).</summary>
    public static class FaceNames
    {
        /// <summary>
        /// The people who are in the photo, left to right, separated by commas: "Anna, Oleh, Mom". A person the face only looks like (nobody said so) is written the same
        /// as one the user named: no question mark. A person with several faces is written once, at the place of the left-most one. Faces nobody is known by add nothing.
        /// Null when there is nothing to write.
        /// </summary>
        public static string? Caption(IReadOnlyList<FaceLabel> labels)
        {
            var people = new List<(string Name, float Left)>();
            foreach (var label in labels)
            {
                if (label.Name is not { Length: > 0 } name)
                    continue;

                var left = label.Face.X + label.Face.Width / 2; // the middle of the face: a big face does not jump ahead of a small one beside it
                var place = people.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (place < 0)
                {
                    people.Add((name, left));
                }
                else
                {
                    var known = people[place];
                    people[place] = (known.Name, Math.Min(known.Left, left));
                }
            }

            if (people.Count == 0)
                return null;

            // OrderBy is stable: faces at the same place keep the order the detector found them in
            return string.Join(", ", people.OrderBy(p => p.Left).Select(p => p.Name));
        }
    }
}
