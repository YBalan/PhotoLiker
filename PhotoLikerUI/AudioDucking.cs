using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// Turns the sound of the other programs down while a video of the slideshow plays with its sound, and puts it back after. Windows keeps a volume for every program that
    /// plays sound (the mixer: one "session" for each); this lowers each of them except this program's own (the video plays through it) to a fraction of what it was, and restores
    /// what it changed. A volume that somebody moved meanwhile (with the mixer, a key) is left as they set it. If the program ends without restoring (a crash), the exit handler does.
    /// Nothing here can fail loudly: with no sound device, or a Windows that answers otherwise, nothing is lowered.
    /// </summary>
    internal sealed class AudioDucking : IDisposable
    {
        private const float Tolerance = 0.015f; // a volume this close to what was set is still the one that was set

        private static readonly List<AudioDucking> Active = [];
        private static bool _exitHooked;

        private readonly List<Lowered> _lowered;
        private bool _restored;

        private AudioDucking(List<Lowered> lowered) => _lowered = lowered;

        /// <summary>How many programs were turned down.</summary>
        public int Count => _lowered.Count;

        /// <summary>
        /// Turns the sound of every program but this one down to <paramref name="level"/> (0 to 1) of what it is. Null when nothing was lowered (the level is 1, no one else plays
        /// sound, or the sound system could not be asked).
        /// </summary>
        public static AudioDucking? Lower(float level)
        {
            if (level >= 1f)
                return null;

            try
            {
                var lowered = new List<Lowered>();
                var self = Environment.ProcessId;
                foreach (var session in Sessions())
                {
                    if (session.ProcessId == self || session.IsSystemSounds || session.Volume is not { } volume)
                        continue;

                    var before = session.Level;
                    var after = Math.Clamp(before * Math.Clamp(level, 0f, 1f), 0f, 1f);
                    var context = Guid.Empty;
                    if (volume.SetMasterVolume(after, ref context) == 0)
                        lowered.Add(new Lowered(volume, before, after));
                }

                if (lowered.Count == 0)
                    return null;

                var ducking = new AudioDucking(lowered);
                lock (Active)
                {
                    Active.Add(ducking);
                    if (!_exitHooked)
                    {
                        _exitHooked = true;
                        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreAll();
                        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreAll(); // a crash does not raise ProcessExit
                    }
                }
                return ducking;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or IOException)
            {
                return null; // no sound device, or one that does not answer
            }
        }

        /// <summary>Puts the volumes back (those that are still as they were set).</summary>
        public void Dispose()
        {
            if (_restored)
                return;

            _restored = true;
            lock (Active)
                Active.Remove(this);

            foreach (var item in _lowered)
            {
                try
                {
                    var context = Guid.Empty;
                    if (item.Volume.GetMasterVolume(out var now) == 0 && Math.Abs(now - item.After) <= Tolerance)
                        item.Volume.SetMasterVolume(item.Before, ref context);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException)
                {
                    // the program that played has gone: nothing to put back
                }
            }
        }

        private static void RestoreAll()
        {
            AudioDucking[] open;
            lock (Active)
                open = [.. Active];

            foreach (var ducking in open)
                ducking.Dispose();
        }

        private sealed record Lowered(ISimpleAudioVolume Volume, float Before, float After);

        // ── looking at the sessions ────────────────────────────────────────

        /// <summary>What a program that plays sound (or did a moment ago) is like in the mixer. For lowering, and for a test to see the volumes.</summary>
        internal sealed record SessionInfo(uint ProcessId, bool IsSystemSounds, float Level, ISimpleAudioVolume? Volume);

        /// <summary>The programs that play sound on the default device, the ones that have stopped for good (expired) left out.</summary>
        internal static IReadOnlyList<SessionInfo> Sessions()
        {
            var sessions = new List<SessionInfo>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleMultimedia, out var device) != 0 || device is null)
                return sessions;

            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var managerObject) != 0 || managerObject is not IAudioSessionManager2 manager)
                return sessions;
            if (manager.GetSessionEnumerator(out var list) != 0 || list.GetCount(out var count) != 0)
                return sessions;

            for (var i = 0; i < count; i++)
            {
                if (list.GetSession(i, out var control) != 0 || control is not IAudioSessionControl2 control2)
                    continue;
                if (control2.GetState(out var state) != 0 || state == StateExpired)
                    continue;

                control2.GetProcessId(out var processId);
                var system = control2.IsSystemSoundsSession() == 0;
                var volume = control as ISimpleAudioVolume;
                var level = 1f;
                if (volume is not null && volume.GetMasterVolume(out var current) == 0)
                    level = current;
                sessions.Add(new SessionInfo(processId, system, level, volume));
            }
            return sessions;
        }

        private const int DataFlowRender = 0;
        private const int RoleMultimedia = 1;
        private const int ClsctxAll = 23;
        private const int StateExpired = 2;

        // ── Windows Core Audio (mmdeviceapi.h, audiopolicy.h); the methods are in the order of the vtables ──

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr groupingParam, int streamFlags, out IAudioSessionControl sessionControl);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr groupingParam, int streamFlags, out ISimpleAudioVolume audioVolume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
            [PreserveSig] int RegisterSessionNotification(IntPtr sessionNotification);
            [PreserveSig] int UnregisterSessionNotification(IntPtr sessionNotification);
            [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
            [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int sessionCount);
            [PreserveSig] int GetSession(int sessionCount, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingParam);
            [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // the methods of IAudioSessionControl, which this one is built on
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingParam);
            [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);

            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint processId);
            [PreserveSig] int IsSystemSoundsSession(); // 0 (S_OK) for the system sounds, 1 (S_FALSE) for any other
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
        }
    }
}
