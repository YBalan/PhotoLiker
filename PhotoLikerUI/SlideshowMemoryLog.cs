using PhotoLiker.Core;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// A line about the memory of the program once a minute while a slideshow runs, in <c>%LocalAppData%\PhotoLiker\slideshow-memory.log</c>: a show that runs for hours and ends up with gigabytes
    /// can then be told apart by what grew (the program's own memory, the managed heap, the handles of Windows, the ffmpeg processes the indexing starts) and when. Nothing is sent anywhere. The file
    /// is kept small: when it passes <see cref="MaxBytes"/> it becomes <c>slideshow-memory.old.log</c> and a new one is begun.
    /// </summary>
    internal sealed class SlideshowMemoryLog : IDisposable
    {
        internal const string FileName = "slideshow-memory.log";
        internal const long MaxBytes = 256 * 1024;
        internal const int IntervalMs = 60_000;

        private const uint GdiObjects = 0;
        private const uint UserObjects = 1;
        private const long Megabyte = 1024 * 1024;

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr process, uint flags);

        private readonly string _path;
        private readonly Func<string> _facts;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly System.Threading.Timer _timer;
        private int _writing;

        /// <param name="folder">Where the file is kept (the program's data folder).</param>
        /// <param name="description">What the show is (its settings), written once at the start.</param>
        /// <param name="facts">What the show says about itself at each line (photos and videos shown so far), called on a thread of the pool.</param>
        /// <param name="intervalMs">Time between two lines.</param>
        public SlideshowMemoryLog(string folder, string description, Func<string> facts, int intervalMs = IntervalMs)
        {
            _path = Path.Combine(folder, FileName);
            _facts = facts;
            Write($"{Stamp()} slideshow begins: {description}");
            Write(Line());
            _timer = new System.Threading.Timer(_ => Tick(), null, intervalMs, intervalMs);
        }

        /// <summary>The log of the program's data folder.</summary>
        public static SlideshowMemoryLog Start(string description, Func<string> facts) => new(AppPaths.DataFolder, description, facts);

        private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private void Tick()
        {
            // A line that is still being written (a slow disk) is not queued behind: the next one comes in a minute
            if (Interlocked.Exchange(ref _writing, 1) != 0)
                return;

            try
            {
                Write(Line());
            }
            finally
            {
                Volatile.Write(ref _writing, 0);
            }
        }

        /// <summary>One line: the time, how long the show has run, the memory of the program as Windows counts it and as the runtime does, the handles, the ffmpeg processes, and what the show says.</summary>
        internal string Line()
        {
            try
            {
                using var me = Process.GetCurrentProcess();
                var info = GC.GetGCMemoryInfo();
                long Generation(int i) => info.GenerationInfo.Length > i ? info.GenerationInfo[i].SizeAfterBytes / Megabyte : 0;

                long ffmpegBytes = 0;
                var ffmpegCount = 0;
                foreach (var other in Process.GetProcessesByName("ffmpeg"))
                {
                    using (other)
                    {
                        try
                        {
                            ffmpegBytes += other.PrivateMemorySize64;
                            ffmpegCount++;
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                        {
                            // it ended meanwhile
                        }
                    }
                }

                return string.Create(CultureInfo.InvariantCulture,
                    $"{Stamp()} +{(long)_clock.Elapsed.TotalMinutes} min private={me.PrivateMemorySize64 / Megabyte} MB workingset={me.WorkingSet64 / Megabyte} MB " +
                    $"managed={GC.GetTotalMemory(false) / Megabyte} MB (last GC: gen0 {Generation(0)}, gen1 {Generation(1)}, gen2 {Generation(2)}, loh {Generation(3)}, poh {Generation(4)}) gc2={GC.CollectionCount(2)} " +
                    $"handles={me.HandleCount} gdi={GetGuiResources(me.Handle, GdiObjects)} user={GetGuiResources(me.Handle, UserObjects)} ffmpeg={ffmpegCount} ({ffmpegBytes / Megabyte} MB) {SafeFacts()}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return $"{Stamp()} (no line: {ex.GetType().Name})";
            }
        }

        private string SafeFacts()
        {
            try
            {
                return _facts();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return string.Empty;
            }
        }

        private void Write(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                    File.Move(_path, Path.ChangeExtension(_path, ".old.log"), overwrite: true);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a log that can not be written is no reason to disturb the show
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
            Write($"{Stamp()} slideshow ends");
        }
    }
}
