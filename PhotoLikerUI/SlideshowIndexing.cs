using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// What the slideshow writes about an indexing that runs behind it (the window of the program is covered, so its status bar can not be seen): one line of text and how far it is.
    /// </summary>
    /// <param name="Text">The line, such as "Indexing: 1,234 / 16,000 (7%), 5 min left".</param>
    /// <param name="Fraction">How much is done, 0 to 1; null when that is not known yet (the folder is being looked through, the models are being loaded).</param>
    internal sealed record SlideshowIndexing(string Text, double? Fraction)
    {
        /// <summary>The line for what the indexing reports now. The counts are written only where they are counts of photos or videos (of videos the first phase counts seconds, and a download counts bytes).</summary>
        public static SlideshowIndexing Of(IndexProgress progress) => Of(progress.Phase, progress.Done, progress.Total, progress.Remaining);

        public static SlideshowIndexing Of(IndexPhase phase, int done, int total, TimeSpan? remaining)
        {
            if (phase is IndexPhase.Scanning or IndexPhase.LoadingModels || total <= 0)
                return new SlideshowIndexing(SlideshowStrings.IndexingPreparing, null);

            var fraction = Math.Clamp(done / (double)total, 0, 1);
            var percent = (int)Math.Floor(fraction * 100);
            var text = phase switch
            {
                IndexPhase.DownloadingModels => string.Format(SlideshowStrings.IndexingDownloadFormat, percent),
                IndexPhase.Videos => string.Format(SlideshowStrings.IndexingPercentFormat, percent),
                _ => string.Format(SlideshowStrings.IndexingCountFormat, Math.Min(done, total), total, percent),
            };

            if (remaining is { } left && phase != IndexPhase.DownloadingModels)
                text = string.Format(SlideshowStrings.IndexingLeftFormat, text, MainForm.FormatDuration(left));

            return new SlideshowIndexing(text, fraction);
        }
    }
}
