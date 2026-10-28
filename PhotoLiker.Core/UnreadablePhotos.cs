namespace PhotoLiker.Core
{
    /// <summary>A photo that can not be read: the search index could not decode it (damaged, cut off), or it is an empty file.</summary>
    public sealed record UnreadablePhoto(string Path, long Length, string Reason);

    public sealed record MoveFailure(string Path, string Message);

    /// <param name="Moved">The photos that were moved, by their old path.</param>
    public sealed record UnreadableMoveResult(IReadOnlyList<string> Moved, IReadOnlyList<MoveFailure> Failures, bool Cancelled);

    /// <summary>
    /// Puts photos that can not be read out of the way: into <c>Deleted\Unreadable</c> of the photo folder with the path they had, the way every deleted
    /// photo goes, so nothing is erased and a photo that turns out to be fine (another program may read what this one could not) goes back by moving it up again.
    /// </summary>
    public static class UnreadablePhotos
    {
        public const string EmptyFileReason = "The file is empty (0 bytes).";

        /// <summary>A stop comes between two photos.</summary>
        /// <param name="progress">The number of photos handled so far.</param>
        public static UnreadableMoveResult MoveAway(string root, IReadOnlyList<string> paths, IProgress<int>? progress, CancellationToken token)
        {
            var moved = new List<string>();
            var failures = new List<MoveFailure>();
            var cancelled = false;

            foreach (var path in paths)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                try
                {
                    if (!File.Exists(path))
                        throw new FileNotFoundException("It is not there any more.");

                    DeletedPhotos.Move(root, path, DeletedPhotos.UnreadableFolderName);
                    moved.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(new MoveFailure(path, ex.Message));
                }
                progress?.Report(moved.Count + failures.Count);
            }

            return new UnreadableMoveResult(moved, failures, cancelled);
        }
    }
}
