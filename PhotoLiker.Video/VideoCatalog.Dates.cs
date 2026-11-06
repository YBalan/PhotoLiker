using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    public sealed partial class VideoCatalog
    {
        /// <summary>
        /// The dates of some video files were changed by the program itself (Align dates), not the videos: the catalog goes on knowing them by their new date of change, so that
        /// they are not read again and, above all, not analyzed again (a video whose file changed is taken for another video and loses its moments and its speech). The date a
        /// video was recorded is worked out again from the dates the file has now: it is the date of the file when the video says none itself. A video whose date of change is not
        /// <paramref name="stamps"/>' previous one has been changed by someone else since, and is left to be read again.
        /// </summary>
        public void UpdateStamps(IReadOnlyCollection<(string FullPath, long PreviousTicks, long ModifiedTicks)> stamps)
        {
            if (stamps.Count == 0)
                return;

            var now = DateTime.Now;
            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                var changed = false;
                foreach (var (fullPath, previous, modified) in stamps)
                {
                    if (Lookup(RelativeOf(fullPath)) is not { } kept || kept.ModifiedTicks != previous)
                        continue;

                    var date = kept.Record.Date;
                    if (kept.Record.Info is { } info && File.Exists(fullPath))
                    {
                        var file = new FileInfo(fullPath);
                        date = VideoDates.Resolve(info, file.CreationTimeUtc, file.LastWriteTimeUtc, now);
                    }

                    using var command = _db.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE videos SET modified = $modified, taken = $taken, taken_source = $source, date_version = $version
                        WHERE path = $path AND modified = $previous
                        """;
                    command.Parameters.AddWithValue("$modified", modified);
                    command.Parameters.AddWithValue("$taken", date.Taken is { } taken ? taken.Ticks : DBNull.Value);
                    command.Parameters.AddWithValue("$source", (int)date.Source);
                    command.Parameters.AddWithValue("$version", date.Version);
                    command.Parameters.AddWithValue("$path", RelativeOf(fullPath));
                    command.Parameters.AddWithValue("$previous", previous);
                    changed |= command.ExecuteNonQuery() > 0;
                }

                transaction.Commit();
                if (changed)
                {
                    // What a search keeps in memory holds the dates of the videos
                    BumpMomentsVersion();
                    BumpSpeechVersion();
                }
            }
        }
    }
}
