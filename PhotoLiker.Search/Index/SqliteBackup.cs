using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace PhotoLiker.Search
{
    /// <summary>
    /// SQLite's online backup (a whole database copied into another, consistent even while it is written to), with the progress of it. <c>SqliteConnection.BackupDatabase</c> does the same in
    /// one call that can not say how far it is; this one takes the pages a few thousand at a time, which is what the bar of a copy that takes a minute is made from.
    /// </summary>
    public static class SqliteBackup
    {
        private const int PagesPerStep = 2000;           // 8 MB at the usual page size: a step is a blink, and the source is let go of between steps
        private const int MostRestarts = 3;              // a source that is written to by another connection starts the copy over; after this many times it is copied in one go
        private const int BusyWaitMs = 25;
        private const int MostBusyWaits = 400;           // 10 seconds

        /// <summary>
        /// Copies the database of <paramref name="from"/> into <paramref name="to"/> (which holds what was in it no longer). <paramref name="progress"/> gets the share that is copied, 0 to 1,
        /// and 1 when it is done; without it this is <c>BackupDatabase</c>.
        /// </summary>
        public static void Copy(SqliteConnection from, SqliteConnection to, IProgress<double>? progress)
        {
            if (progress is null)
            {
                from.BackupDatabase(to);
                return;
            }

            var source = from.Handle ?? throw new InvalidOperationException("The source database is not open.");
            var destination = to.Handle ?? throw new InvalidOperationException("The destination database is not open.");

            using var backup = raw.sqlite3_backup_init(destination, "main", source, "main");
            if (backup.IsInvalid)
                SqliteException.ThrowExceptionForRC(raw.sqlite3_errcode(destination), destination);

            var restarts = 0;
            var busy = 0;
            var lastTotal = 0;
            var lastRemaining = int.MaxValue;
            while (true)
            {
                var step = restarts >= MostRestarts ? -1 : PagesPerStep;
                var rc = raw.sqlite3_backup_step(backup, step);
                if (rc == raw.SQLITE_DONE)
                    break;

                if (rc is raw.SQLITE_BUSY or raw.SQLITE_LOCKED)
                {
                    if (++busy > MostBusyWaits)
                        SqliteException.ThrowExceptionForRC(rc, destination);
                    Thread.Sleep(BusyWaitMs);
                    continue;
                }

                if (rc != raw.SQLITE_OK)
                    SqliteException.ThrowExceptionForRC(rc, destination);

                var total = raw.sqlite3_backup_pagecount(backup);
                var remaining = raw.sqlite3_backup_remaining(backup);
                // A step that copied pages leaves fewer to copy than the one before; if not, the source was changed by another connection and SQLite began again from the first page
                if (remaining >= lastRemaining || total != lastTotal && lastTotal != 0)
                    restarts++;
                lastTotal = total;
                lastRemaining = remaining;

                if (total > 0)
                    progress.Report(Math.Clamp((total - remaining) / (double)total, 0, 0.999));
            }

            progress.Report(1);
        }
    }
}
