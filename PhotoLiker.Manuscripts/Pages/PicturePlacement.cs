using PhotoLiker.PhotoBook;

namespace PhotoLiker.Manuscripts.Pages
{
    /// <summary>
    /// Where a picture that was found on a page goes in the text of the page: between the rows of the text, where it is between the lines on the page; at the left, in the middle or at the
    /// right, as it is on the page; as wide as it is on the page.
    /// </summary>
    public static class PicturePlacement
    {
        /// <summary>The number of the row the picture comes before: the first whose middle is below the middle of the picture; the number of rows (after the last) when none is.</summary>
        public static int RowBefore(PagePicture picture, IReadOnlyList<LayoutRow> rows)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].CentreY > picture.Centre.Y)
                    return i;
            }

            return rows.Count;
        }

        /// <summary>At the left of the page, in the middle or at the right: by the third of the width of the photo the middle of the picture is in.</summary>
        public static TextAlign AlignOf(PagePicture picture, double photoWidth)
        {
            var third = photoWidth <= 0 ? 0.5 : picture.Centre.X / photoWidth;
            return third < 1.0 / 3 ? TextAlign.Left : third > 2.0 / 3 ? TextAlign.Right : TextAlign.Center;
        }

        /// <summary>How wide the picture is in the text as a share of the width of the text: as wide as it is on the page, no narrower than a seventh.</summary>
        public static double ShareOf(PagePicture picture, double photoWidth) =>
            photoWidth <= 0 ? 0.5 : Math.Clamp(picture.Bounds.Width / photoWidth, 0.15, 1.0);

        /// <summary>
        /// The document with the pictures in it: each as a paragraph before the row of the text it is placed before (see <see cref="RowBefore"/>). The pictures that were in the document are taken
        /// out first, so that finding the pictures again does not put them in twice. When the text is not the rows it was made of any more (it was edited) a row is the paragraph that is as far down the
        /// text as the row is down the rows.
        /// </summary>
        /// <param name="rowCount">How many rows the text was made of.</param>
        public static RichDocument Insert(RichDocument document, IReadOnlyList<(RichParagraph Picture, int Row)> pictures, int rowCount)
        {
            var text = document.WithoutPictures();
            var paragraphs = new List<RichParagraph>(text.Paragraphs);
            var rowParagraph = Enumerable.Range(0, paragraphs.Count).Where(i => !paragraphs[i].IsBlank).ToList();
            var end = paragraphs.Count; // "after the last row": the places are those the text had (the pictures put in go before them, from the last to the first)

            int PlaceOf(int row)
            {
                if (row <= 0 || rowParagraph.Count == 0 || rowCount <= 0)
                    return 0;
                if (row >= rowCount)
                    return end;

                var index = rowParagraph.Count == rowCount ? row : (int)Math.Round(row * (double)rowParagraph.Count / rowCount);
                return index >= rowParagraph.Count ? end : rowParagraph[index];
            }

            // From the last to the first, so that the places of the others do not move; the ones before the same row keep their order
            foreach (var (picture, order) in pictures.Select((item, order) => (item, order)).OrderByDescending(pair => pair.item.Row).ThenByDescending(pair => pair.order))
                paragraphs.Insert(PlaceOf(picture.Row), picture.Picture);

            return new RichDocument(paragraphs);
        }
    }
}
