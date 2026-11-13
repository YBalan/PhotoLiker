namespace PhotoLiker.Core
{
    /// <summary>
    /// One progress bar for work that goes in stages of different sizes (copying one file, then another, then packing both): each stage reports its own 0 to 1, and the
    /// weights (the bytes a stage has to move are a good one) say how much of the bar it takes. The bar only goes forward when the stages are run in order.
    /// </summary>
    public sealed class StagedProgress
    {
        private readonly IProgress<double>? _target;
        private readonly double[] _starts;
        private readonly double[] _shares;

        /// <param name="target">Where the progress of the whole goes (0 to 1); null when nobody is looking.</param>
        /// <param name="weights">The size of each stage. A stage of 0 takes no part of the bar; all 0 split it evenly.</param>
        public StagedProgress(IProgress<double>? target, params double[] weights)
        {
            _target = target;
            var clean = weights.Select(weight => double.IsFinite(weight) && weight > 0 ? weight : 0).ToArray();
            var total = clean.Sum();
            if (total <= 0)
            {
                total = clean.Length;
                clean = [.. clean.Select(_ => 1.0)];
            }

            _shares = [.. clean.Select(weight => weight / total)];
            _starts = new double[clean.Length];
            for (var i = 1; i < _starts.Length; i++)
                _starts[i] = _starts[i - 1] + _shares[i - 1];
        }

        /// <summary>The report of one stage (counted from 0): what it says is put on the bar of the whole. Reporting nothing back when nobody is looking.</summary>
        public IProgress<double> Stage(int index)
        {
            if (_target is null)
                return NoProgress.Instance;

            var start = _starts[index];
            var share = _shares[index];
            return new Mapped(fraction => _target.Report(start + share * Math.Clamp(fraction, 0, 1)));
        }

        /// <summary>The whole is done.</summary>
        public void Complete() => _target?.Report(1);

        private sealed class Mapped(Action<double> report) : IProgress<double>
        {
            public void Report(double value) => report(value);
        }

        private sealed class NoProgress : IProgress<double>
        {
            public static readonly NoProgress Instance = new();

            public void Report(double value)
            {
            }
        }
    }

    /// <summary>Copying that says how far it is, for files that are big enough for a person to wait for.</summary>
    public static class ProgressCopy
    {
        private const int BufferBytes = 1 << 20;        // a megabyte at a time: a network share is slow with small reads
        private const double ReportStep = 0.002;        // a report is not worth its cost for less than this much of the file

        /// <summary>Copies <paramref name="from"/> to <paramref name="to"/>, reporting the share of <paramref name="length"/> that is done (0 to 1; the last report is 1).</summary>
        public static void CopyStream(Stream from, Stream to, long length, IProgress<double>? progress)
        {
            var buffer = new byte[BufferBytes];
            long done = 0;
            var reported = -1.0;
            int read;
            while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
            {
                to.Write(buffer, 0, read);
                done += read;

                if (progress is null || length <= 0)
                    continue;

                var fraction = Math.Min(1.0, (double)done / length);
                if (fraction - reported >= ReportStep)
                {
                    reported = fraction;
                    progress.Report(fraction);
                }
            }

            progress?.Report(1);
        }

        /// <summary>Copies a file like <see cref="File.Copy(string, string, bool)"/> does, reporting how far it is.</summary>
        public static void CopyFile(string source, string destination, bool overwrite, IProgress<double>? progress)
        {
            using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
            using var to = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.SequentialScan);
            CopyStream(from, to, from.Length, progress);
        }
    }
}
