using Microsoft.Data.Sqlite;

namespace PhotoLiker.Search.Tests;

/// <summary>SQLite's online backup, with the progress of it (what the bar of an index export is made from).</summary>
public class SqliteBackupTests : IDisposable
{
    private const int Rows = 5000;
    private const int RowBytes = 4000; // about a page each: 5,000 pages, so the copy takes three steps of 2,000

    private readonly string _work = Path.Combine(Path.GetTempPath(), "PLBackup_" + Guid.NewGuid().ToString("N"));

    public SqliteBackupTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_work, recursive: true); } catch (IOException) { }
    }

    private sealed class Reports : IProgress<double>
    {
        private readonly Action<double>? _onReport;

        public Reports(Action<double>? onReport = null) => _onReport = onReport;

        public List<double> Values { get; } = [];

        public void Report(double value)
        {
            Values.Add(value);
            _onReport?.Invoke(value);
        }
    }

    private SqliteConnection MakeSource()
    {
        var db = new SqliteConnection($"Data Source={Path.Combine(_work, "source.db")};Pooling=False");
        db.Open();
        Run(db, "CREATE TABLE things (id INTEGER PRIMARY KEY, data BLOB NOT NULL)");

        using var transaction = db.BeginTransaction();
        using var insert = db.CreateCommand();
        insert.CommandText = "INSERT INTO things (data) VALUES ($data)";
        var data = new byte[RowBytes];
        new Random(7).NextBytes(data);
        insert.Parameters.AddWithValue("$data", data);
        for (var i = 0; i < Rows; i++)
            insert.ExecuteNonQuery();
        transaction.Commit();
        return db;
    }

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private SqliteConnection Destination(string name = "copy.db")
    {
        var db = new SqliteConnection($"Data Source={Path.Combine(_work, name)};Pooling=False");
        db.Open();
        return db;
    }

    [Fact]
    public void ACopyThatSaysHowFarItIs_GoesForwardInSteps_AndEndsAtOne()
    {
        using var source = MakeSource();
        using var copy = Destination();
        var reports = new Reports();

        SqliteBackup.Copy(source, copy, reports);

        Assert.Equal(Rows, Scalar(copy, "SELECT COUNT(*) FROM things"));
        using (var check = copy.CreateCommand())
        {
            check.CommandText = "PRAGMA integrity_check";
            Assert.Equal("ok", check.ExecuteScalar());
        }
        Assert.True(reports.Values.Count >= 3, "a report for every step, not only the last: " + string.Join(", ", reports.Values));
        Assert.Equal(reports.Values.Order().ToList(), reports.Values); // never back
        Assert.Equal(1.0, reports.Values[^1]);
        Assert.All(reports.Values.SkipLast(1), value => Assert.InRange(value, 0.0, 0.999));
        Assert.Contains(reports.Values, value => value is > 0.05 and < 0.95); // it was somewhere on the way
    }

    [Fact]
    public void WithNobodyLooking_ItIsTheOrdinaryBackup()
    {
        using var source = MakeSource();
        using var copy = Destination();

        SqliteBackup.Copy(source, copy, progress: null);

        Assert.Equal(Rows, Scalar(copy, "SELECT COUNT(*) FROM things"));
    }

    [Fact]
    public void ASourceThatIsWrittenToWhileItIsCopied_IsStillCopied_Whole()
    {
        using var source = MakeSource();
        using var writer = new SqliteConnection($"Data Source={Path.Combine(_work, "source.db")};Pooling=False");
        writer.Open();
        using var copy = Destination();

        // Another connection changes the source after every step: SQLite begins the copy again each time, and this must not go on for ever
        var changes = 0;
        var reports = new Reports(_ =>
        {
            Run(writer, $"UPDATE things SET data = zeroblob({RowBytes}) WHERE id = {++changes}");
        });

        SqliteBackup.Copy(source, copy, reports);

        Assert.Equal(Rows, Scalar(copy, "SELECT COUNT(*) FROM things"));
        using var check = copy.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", check.ExecuteScalar());
        Assert.Equal(1.0, reports.Values[^1]);
        Assert.InRange(changes, 1, 20); // a handful of restarts, then the rest in one go
    }

    [Fact]
    public void ASmallDatabase_IsOneStep_AndSaysDone()
    {
        using var source = new SqliteConnection($"Data Source={Path.Combine(_work, "small.db")};Pooling=False");
        source.Open();
        Run(source, "CREATE TABLE t (id INTEGER)");
        Run(source, "INSERT INTO t VALUES (1)");
        using var copy = Destination("small-copy.db");
        var reports = new Reports();

        SqliteBackup.Copy(source, copy, reports);

        Assert.Equal(1, Scalar(copy, "SELECT COUNT(*) FROM t"));
        Assert.Equal([1.0], reports.Values);
    }

    [Fact]
    public void ExportingAnIndex_SaysHowFarItIs_AndImportingItDoesToo()
    {
        var photos = Path.Combine(_work, "photos");
        var elsewhere = Path.Combine(_work, "elsewhere");
        Directory.CreateDirectory(photos);
        Directory.CreateDirectory(elsewhere);
        var file = Path.Combine(_work, "folder.plindex");
        try
        {
            using (var index = PhotoIndex.Open(photos))
            {
                index.Save([new IndexedPhoto("one.jpg", length: 1, modifiedTicks: 1) { Clips = Vectors.Clips(Vectors.Unit(1)) }]);
                var exported = new Reports();
                index.ExportTo(file, exported);
                Assert.Equal(1.0, exported.Values[^1]);
                Assert.Equal(exported.Values.Order().ToList(), exported.Values);
            }

            var imported = new Reports();
            PhotoIndex.Import(elsewhere, file, imported);

            Assert.Equal(1.0, imported.Values[^1]);
            using var opened = PhotoIndex.Open(elsewhere);
            Assert.Equal(1, opened.PhotoCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var folder in new[] { photos, elsewhere })
                try { File.Delete(SearchPaths.IndexFile(folder)); } catch (IOException) { }
        }
    }
}
