using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// Shows the progress of whatever is running on the taskbar button of the main window, the green bar that fills up under the icon (as a browser does
    /// for a download), so it can be followed with the window minimized or behind another one.
    ///
    /// The progress bars of the program are not told to do anything: each registers itself once with <see cref="Watch(ProgressBar, Func{bool}?)"/>, and the
    /// window asks them, a few times a second, what they say (a bar that is showing is a bar that is working; the style says whether it has an end). Of the
    /// bars that are showing the one registered last wins, which is the dialog in front of the window rather than the bar of the window (see
    /// <see cref="TaskbarProgressView.Choose"/>). Everything is on the UI thread.
    /// </summary>
    internal sealed class TaskbarProgress : IDisposable
    {
        private const int PollMs = 200;

        private static readonly List<Func<ProgressSample?>> Sources = [];

        /// <summary>
        /// The message Windows sends a window when its taskbar button has been made (it is not there before the window is first shown, and it is made
        /// again after the taskbar crashed): the progress is then put on it again.
        /// </summary>
        public static readonly int ButtonCreatedMessage = (int)RegisterWindowMessage("TaskbarButtonCreated");

        private readonly Form _window;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = PollMs };
        private ITaskbarList3? _list;
        private bool _unavailable;
        private TaskbarProgressView _applied = TaskbarProgressView.None;

        public TaskbarProgress(Form window)
        {
            _window = window;
            _timer.Tick += (_, _) => Update();
            _timer.Start();
        }

        /// <summary>What is on the button now, as far as Windows took it.</summary>
        internal TaskbarProgressView Applied => _applied;

        // ── what is running ────────────────────────────────────────────────

        /// <summary>
        /// Lets the taskbar follow a progress bar for as long as the bar exists. It counts while it is showing and, if given, <paramref name="working"/> says so:
        /// a bar that stays on the screen after the work (the video dialog's) must not keep the button at 100 %.
        /// </summary>
        public static IDisposable Watch(ProgressBar bar, Func<bool>? working = null)
        {
            var watch = Watch(() => Read(bar, working));
            bar.Disposed += (_, _) => watch.Dispose();
            return watch;
        }

        /// <summary>Lets the taskbar follow whatever <paramref name="read"/> says (null: nothing is running). Disposing the result stops it.</summary>
        public static IDisposable Watch(Func<ProgressSample?> read)
        {
            Sources.Add(read);
            return new Watching(read);
        }

        public static ProgressSample? Read(ProgressBar bar, Func<bool>? working = null)
        {
            if (bar.IsDisposed || !bar.Visible || working?.Invoke() == false)
                return null;

            return bar.Style == ProgressBarStyle.Marquee
                ? new ProgressSample(Indeterminate: true)
                : new ProgressSample(Indeterminate: false, bar.Value - bar.Minimum, bar.Maximum - bar.Minimum);
        }

        /// <summary>The bar of the status strip (the indexing of the photos).</summary>
        public static ProgressSample? Read(ToolStripProgressBar bar)
        {
            if (bar.IsDisposed || !bar.Available || bar.Owner is not { Visible: true })
                return null;

            return bar.Style == ProgressBarStyle.Marquee
                ? new ProgressSample(Indeterminate: true)
                : new ProgressSample(Indeterminate: false, bar.Value - bar.Minimum, bar.Maximum - bar.Minimum);
        }

        private sealed class Watching(Func<ProgressSample?> read) : IDisposable
        {
            public void Dispose() => Sources.Remove(read);
        }

        // ── the button ─────────────────────────────────────────────────────

        /// <summary>Looks at the bars and, if what the button shows has to change, changes it. Called by the timer.</summary>
        internal void Update()
        {
            var view = TaskbarProgressView.Choose(Sources.AsEnumerable().Reverse().Select(read => read()));
            if (view == _applied)
                return;

            // A change Windows did not take (the button is not there yet) is tried again at the next call
            if (Apply(view))
                _applied = view;
        }

        /// <summary>The button was made (again): it is empty, so what was on it goes on it once more.</summary>
        public void ButtonCreated()
        {
            _applied = TaskbarProgressView.None;
            Update();
        }

        private bool Apply(TaskbarProgressView view)
        {
            if (_unavailable || !_window.IsHandleCreated)
                return false;

            try
            {
                _list ??= Create();
                var handle = _window.Handle;

                var ok = _list.SetProgressState(handle, view.State switch
                {
                    TaskbarProgressState.Indeterminate => Indeterminate,
                    TaskbarProgressState.Normal => Normal,
                    _ => NoProgress,
                }) >= 0;

                if (ok && view.State == TaskbarProgressState.Normal)
                    ok = _list.SetProgressValue(handle, (ulong)view.Permille, TaskbarProgressView.Scale) >= 0;

                return ok;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or PlatformNotSupportedException or NotSupportedException)
            {
                // No taskbar to talk to (another shell): the program works the same without the bar on the button
                _unavailable = true;
                return false;
            }
        }

        private static ITaskbarList3 Create()
        {
            var list = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), throwOnError: true)!)!;
            list.HrInit();
            return list;
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();

            if (_list is not null)
            {
                try
                {
                    if (_window.IsHandleCreated)
                        _list.SetProgressState(_window.Handle, NoProgress);
                    Marshal.FinalReleaseComObject(_list);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException)
                {
                    // the window is going away anyway
                }
                _list = null;
            }
        }

        // ── Windows ────────────────────────────────────────────────────────

        private const int NoProgress = 0, Indeterminate = 1, Normal = 2; // TBPFLAG

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);

        /// <summary>The first eight methods of ITaskbarList3 (shobjidl_core.h), in the order of its table; the rest are not used.</summary>
        [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hwnd);
            [PreserveSig] int DeleteTab(IntPtr hwnd);
            [PreserveSig] int ActivateTab(IntPtr hwnd);
            [PreserveSig] int SetActiveAlt(IntPtr hwnd);

            // ITaskbarList2
            [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

            // ITaskbarList3
            [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
        }
    }
}
