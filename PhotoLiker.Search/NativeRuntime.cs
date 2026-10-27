using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhotoLiker.Search
{
    /// <summary>
    /// ONNX Runtime is built with a recent Visual C++ toolset, and when the C++ runtime that is loaded under the name
    /// MSVCP140.dll is an old one it does not fail with an exception: the whole process dies with an access violation.
    /// Old copies are carried around by other programs. The shell extension of TortoiseGit, for one, loads its own private
    /// MSVCP140.dll 14.30 into every program that opens a file or folder dialog. Windows binds imports by module name, so
    /// whichever copy is loaded first is the one ONNX Runtime gets.
    /// </summary>
    public static class NativeRuntime
    {
        /// <summary>The oldest C++ runtime ONNX Runtime can work with (14.40 came with Visual Studio 2022 17.10).</summary>
        public static readonly Version MinimumRuntime = new(14, 40);

        private const string StlLibrary = "MSVCP140.dll";
        private static readonly string[] RuntimeFiles = ["VCRUNTIME140.dll", "VCRUNTIME140_1.dll", StlLibrary, "MSVCP140_1.dll"];

        private static bool _verified;

        /// <summary>
        /// Loads the C++ runtime that Windows has (System32) into the process, so that it is the one every later load by
        /// name gets, whatever a shell extension brings along afterwards. Call it first thing when the program starts,
        /// before any dialog is opened. Harmless to call again; a runtime that is not installed is not an error here.
        /// </summary>
        public static void PreferWindowsRuntime()
        {
            foreach (var file in RuntimeFiles)
                NativeLibrary.TryLoad(file, typeof(NativeRuntime).Assembly, DllImportSearchPath.System32, out _);
        }

        /// <summary>
        /// Throws a readable error if ONNX Runtime would be given an old C++ runtime, which would crash the program.
        /// Call it before the first model is created.
        /// </summary>
        public static void EnsureOnnxRuntimeCanLoad()
        {
            if (_verified)
                return;

            if (FindOldRuntime(LoadedRuntimeModules()) is { } old)
            {
                throw new InvalidOperationException(
                    $"The search models cannot run in this window: an old copy of the Visual C++ runtime ({StlLibrary} {old.Version}, {old.Path}) " +
                    "was loaded into PhotoLiker before them, and ONNX Runtime would crash with it. Programs such as TortoiseGit do this when a file or " +
                    "folder dialog is opened. Close PhotoLiker and start it again, or install the current Microsoft Visual C++ Redistributable (x64), " +
                    "which makes this go away for good.");
            }

            _verified = true;
        }

        /// <summary>
        /// The copy of the C++ runtime that later loads by name get (the one loaded first), if it is too old for ONNX Runtime;
        /// null if it is good enough, or if none is loaded or its version is not known.
        /// </summary>
        public static (string Path, Version Version)? FindOldRuntime(IEnumerable<(string Name, string Path, Version? Version)> modulesInLoadOrder)
        {
            foreach (var (name, path, version) in modulesInLoadOrder)
            {
                if (!string.Equals(name, StlLibrary, StringComparison.OrdinalIgnoreCase))
                    continue;

                return version is not null && version < MinimumRuntime ? (path, version) : null;
            }
            return null;
        }

        private static List<(string Name, string Path, Version? Version)> LoadedRuntimeModules()
        {
            var found = new List<(string, string, Version?)>();
            try
            {
                foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                {
                    if (!string.Equals(module.ModuleName, StlLibrary, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var info = module.FileVersionInfo;
                    found.Add((module.ModuleName, module.FileName, new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart)));
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // the modules can not be listed: nothing to check against, go on
            }
            return found;
        }
    }
}
