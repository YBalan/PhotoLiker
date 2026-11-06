namespace PhotoLiker.Search
{
    public enum IndexPhase
    {
        Scanning,
        DownloadingModels,
        LoadingModels,
        Indexing,

        /// <summary>Only the dates of photos that are indexed already are read: quick, and no models are needed.</summary>
        ReadingDates,

        /// <summary>The videos of the folder are read and their pictures analyzed (PhotoLiker.Video); <c>Done</c> and <c>Total</c> are seconds of video.</summary>
        Videos,

        /// <summary>What is said in the videos of the folder is turned into text (PhotoLiker.Video); <c>Done</c> and <c>Total</c> are videos, the speed is seconds of speech read in a second.</summary>
        Speech,
    }

    /// <summary>What the indexing is doing right now, for a progress bar and a line of text.</summary>
    public sealed record IndexProgress(
        IndexPhase Phase,
        string Message,
        int Done,
        int Total,
        int Failed,
        int Faces,
        double PhotosPerSecond,
        TimeSpan? Remaining);

    /// <summary>
    /// How long each stage of the pipeline was busy, added up over its threads. Compared with the elapsed time this shows
    /// what limits the speed: a stage that is busy all the time is the bottleneck.
    /// </summary>
    public sealed record StageTimes(TimeSpan Read, TimeSpan Decode, TimeSpan Clip, TimeSpan Faces, int DecoderThreads, int FaceWorkers);

    /// <param name="DatesRead">Photos that were indexed before and only had their date read now.</param>
    /// <param name="ModelsDownloaded">Model files were downloaded in this run (also when there was nothing to index: an index brought from another computer needs the search model to search by words).</param>
    /// <param name="DownloadDeclined">The run needed model files that are not on this computer and the user said no to downloading them, so nothing was indexed.</param>
    public sealed record IndexSummary(
        int Indexed, int Failed, int Faces, int Removed, TimeSpan Elapsed, bool Cancelled, StageTimes? Times = null, int DatesRead = 0,
        bool ModelsDownloaded = false, bool DownloadDeclined = false);

    /// <summary>What a run is about to download, to ask the user first.</summary>
    /// <param name="Clip">The model for searching by words whose files are missing; null when they are here or not needed.</param>
    /// <param name="Faces">The face models are missing and needed.</param>
    /// <param name="Megabytes">About how much is downloaded in all.</param>
    /// <param name="WithIndexing">Photos are indexed after the download; false when the files are only wanted for searching an index that is complete.</param>
    public sealed record ModelDownload(ClipModelInfo? Clip, bool Faces, int Megabytes, bool WithIndexing);
}
