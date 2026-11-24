namespace PhotoLikerUI
{
    /// <summary>What a book that was made is: where it is, how many pages and photos it has, and the photos that could not be read (they are not in it).</summary>
    internal sealed record BookResult(string Path, int Pages, int Photos, IReadOnlyList<string> Unreadable, long Bytes);

    /// <summary>
    /// Makes the book of the dialog: each photo that is in the book is read and made into an upright JPEG of the size the book needs (<see cref="BookPictures"/>, several at a time, in a temporary
    /// folder that is gone when the book is made), then the writer of the format puts the pictures and the descriptions into the file. A photo that cannot be read is left out and named in the
    /// result, the rest of the book is made.
    /// </summary>
    internal static class BookBuilder
    {
        /// <param name="entries">The photos in the order of the book; the ones that are left out, or are gone, are not in the book.</param>
        /// <param name="options">The book as a whole (the title, the paper...).</param>
        /// <param name="outputPath">The file that is made (written over if it is there).</param>
        /// <param name="preparing">Told, from any thread, how many photos are prepared of how many.</param>
        /// <param name="writing">Told, from any thread, when the file is being written.</param>
        /// <param name="percent">Percent of the whole work done, from any thread.</param>
        /// <param name="withPhotos">False: the book is only the text of its pages (no photo is read or put in the file); the text of a page is what is written above its photo. The pictures in a text are in the book where they stand.</param>
        public static async Task<BookResult> BuildAsync(
            IReadOnlyList<BookEntry> entries,
            BookFormat format,
            BookOptions options,
            PictureQuality quality,
            string outputPath,
            Action<int, int>? preparing,
            Action? writing,
            Action<int>? percent,
            CancellationToken cancellation,
            bool withPhotos = true)
        {
            var photos = entries.Where(entry => !entry.LeftOut).ToList();
            if (!withPhotos)
                return await BuildTextOnlyAsync(photos, format, options, outputPath, writing, percent, cancellation);

            var temp = Path.Combine(Path.GetTempPath(), "PhotoLikerBook_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var prepared = new JpegPicture?[photos.Count];
                var files = new string?[photos.Count];
                var done = 0;
                var maxSide = PictureQualities.MaxSide(quality);
                var jpegQuality = PictureQualities.JpegQuality(quality);

                await Parallel.ForEachAsync(
                    Enumerable.Range(0, photos.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 4), CancellationToken = cancellation },
                    (index, token) =>
                    {
                        try
                        {
                            var picture = BookPictures.ToJpeg(photos[index].Path, maxSide, jpegQuality, photos[index].Rotation); // turned as the book has it
                            var file = Path.Combine(temp, $"{index:D5}.jpg");
                            File.WriteAllBytes(file, picture.Bytes);
                            prepared[index] = picture with { Bytes = [] }; // the bytes are in the file: only the size is kept
                            files[index] = file;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
                        {
                            // left out of the book; named in the result
                        }

                        var finished = Interlocked.Increment(ref done);
                        preparing?.Invoke(finished, photos.Count);
                        percent?.Invoke(finished * 70 / Math.Max(1, photos.Count));
                        return ValueTask.CompletedTask;
                    });

                cancellation.ThrowIfCancellationRequested();

                var pages = new List<BookPage>();
                var unreadable = new List<string>();
                for (var index = 0; index < photos.Count; index++)
                {
                    if (prepared[index] is not { } picture || files[index] is not { } file)
                    {
                        unreadable.Add(photos[index].Path);
                        continue;
                    }

                    pages.Add(new BookPage(
                        file,
                        picture.Width,
                        picture.Height,
                        RtfParser.Parse(photos[index].AboveRtf),
                        RtfParser.Parse(photos[index].BelowRtf),
                        Path.GetFileNameWithoutExtension(photos[index].Path)));
                }

                writing?.Invoke();
                var progress = new WritingProgress(percent);
                var pageCount = await Task.Run(() =>
                {
                    if (format == BookFormat.Pdf)
                        return PdfBookWriter.Write(outputPath, options, pages, progress, cancellation);

                    EpubBookWriter.Write(outputPath, options, pages, progress, cancellation);
                    return pages.Count + (string.IsNullOrWhiteSpace(options.Title) ? 0 : 1);
                }, cancellation);

                return new BookResult(outputPath, pageCount, pages.Count, unreadable, new FileInfo(outputPath).Length);
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // a temporary folder that stays is cleaned up by Windows with the rest of the temp folder
                }
            }
        }

        /// <summary>A book of the text of its pages: nothing is read but the texts.</summary>
        private static async Task<BookResult> BuildTextOnlyAsync(
            List<BookEntry> photos, BookFormat format, BookOptions options, string outputPath, Action? writing, Action<int>? percent, CancellationToken cancellation)
        {
            var pages = photos
                .Select(photo => BookPage.TextOnly(
                    RtfParser.Parse(string.IsNullOrWhiteSpace(photo.AboveRtf) ? photo.BelowRtf : photo.AboveRtf),
                    Path.GetFileNameWithoutExtension(photo.Path)))
                .ToList();

            writing?.Invoke();
            var progress = new WritingProgress(percent, start: 0, span: 100);
            var pageCount = await Task.Run(() =>
            {
                if (format == BookFormat.Pdf)
                    return PdfBookWriter.Write(outputPath, options, pages, progress, cancellation);

                EpubBookWriter.Write(outputPath, options, pages, progress, cancellation);
                return pages.Count + (string.IsNullOrWhiteSpace(options.Title) ? 0 : 1);
            }, cancellation);

            return new BookResult(outputPath, pageCount, pages.Count, [], new FileInfo(outputPath).Length);
        }

        /// <summary>The writers report 0 to 100 for their part of the work, which is the last 30 percent of the whole (the whole of it for a book of text, which has no photos to prepare).</summary>
        private sealed class WritingProgress(Action<int>? percent, int start = 70, int span = 30) : IProgress<int>
        {
            public void Report(int value) => percent?.Invoke(start + value * span / 100);
        }
    }
}
