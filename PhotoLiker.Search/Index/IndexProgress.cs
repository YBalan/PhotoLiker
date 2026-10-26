namespace PhotoLiker.Search
{
    public enum IndexPhase
    {
        Scanning,
        DownloadingModels,
        LoadingModels,
        Indexing,
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

    public sealed record IndexSummary(int Indexed, int Failed, int Faces, int Removed, TimeSpan Elapsed, bool Cancelled, StageTimes? Times = null);
}
