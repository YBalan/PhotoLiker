using System.Runtime.InteropServices;
using System.Text;

namespace PhotoLikerUI
{
    /// <summary>What came of writing the Jump List: why it was not written (null: it was), and the folders the person took off the list in Windows since the last time.</summary>
    internal sealed record JumpListResult(string? Error, IReadOnlyList<string> Removed)
    {
        public bool Committed => Error is null;

        /// <summary>This machine has no Jump Lists to write to (the shell does not offer them, as on a server without the desktop experience): it is not a call that failed.</summary>
        public bool Unavailable { get; init; }

        public static JumpListResult Nothing { get; } = new(null, []);
    }

    /// <summary>
    /// The list that Windows shows when the taskbar button of the program (or its pinned icon) is right-clicked: the folders that were opened last, as a category of its own,
    /// each one starting the program on that folder. Windows lets the person pin an entry (it moves up to Pinned and stays, whatever is opened later) and remove one
    /// ("Remove from this list"), which is what this class hears about: the folders removed are given back, so that the program takes them off its own list too.
    ///
    /// It is a custom list (ICustomDestinationList) and not the automatic Recent one, which is fed by files that a program is registered for: a folder is opened by Explorer,
    /// so it would not reach the list. The program has no AppUserModelID of its own set, so the list belongs to the one Windows makes from the program's path: that is the
    /// one the taskbar button has however the program was started, and a shortcut that is pinned to the taskbar needs no matching ID to keep it.
    /// </summary>
    internal static class RecentJumpList
    {
        /// <summary>What writes the list; the tests put their own here, so that no test changes the Jump List of the person who runs it.</summary>
        internal static Func<IReadOnlyList<string>, string, JumpListResult> Writer { get; set; } = (folders, category) => Write(folders, category, appId: null);

        /// <summary>The list with the given folders (newest first) under the given heading. Never throws: no Jump List to write to (another shell) is a result with an error.</summary>
        public static JumpListResult Update(IReadOnlyList<string> folders, string category) => Writer(folders, category);

        /// <summary>The arguments that start the program on a folder: its path in quotes, the way the entry of the Explorer context menu does.</summary>
        internal static string ArgumentsFor(string folder)
        {
            // A path that ends with a backslash (a drive: "Y:\") would escape the quote that closes it; two backslashes are one, then the quote ends the argument
            var trailing = folder.Length - folder.TrimEnd('\\').Length;
            return "\"" + folder + new string('\\', trailing) + "\"";
        }

        /// <summary>Writes the list to Windows. <paramref name="appId"/> is for the tests: they write to a list of their own and not to the one of the program that runs them.</summary>
        internal static JumpListResult Write(IReadOnlyList<string> folders, string category, string? appId)
        {
            ICustomDestinationList? list = null;
            IObjectCollection? collection = null;
            var begun = false;
            try
            {
                list = (ICustomDestinationList)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidDestinationList, throwOnError: true)!)!;
                if (appId is not null)
                    Check(list.SetAppID(appId), "SetAppID");

                var arrayId = IidObjectArray;
                Check(list.BeginList(out var slots, ref arrayId, out var removedObject), "BeginList");
                begun = true;

                // What the person removed from the list must not be put back (Windows refuses the whole list if it is)
                var removedArguments = ArgumentsOf((IObjectArray)removedObject);
                var removed = new List<string>();
                var kept = new List<string>();
                foreach (var folder in folders)
                {
                    if (removedArguments.Contains(ArgumentsFor(folder), StringComparer.OrdinalIgnoreCase))
                        removed.Add(folder);
                    else if (kept.Count < slots)
                        kept.Add(folder);
                }

                if (kept.Count > 0)
                {
                    collection = (IObjectCollection)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidObjectCollection, throwOnError: true)!)!;
                    var titles = PhotoLiker.Core.RecentFolders.Titles(kept);
                    for (var i = 0; i < kept.Count; i++)
                    {
                        var link = MakeLink(kept[i], titles[i]);
                        try { Check(collection.AddObject(link), "AddObject"); }
                        finally { Marshal.FinalReleaseComObject(link); }
                    }

                    Check(list.AppendCategory(category, (IObjectArray)collection), "AppendCategory");
                }

                Check(list.CommitList(), "CommitList");
                begun = false;
                return new JumpListResult(null, removed);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or PlatformNotSupportedException or NotSupportedException or IOException)
            {
                if (begun)
                {
                    try { list!.AbortList(); } catch (COMException) { }
                }

                return new JumpListResult(ex.Message, []) { Unavailable = list is null }; // the list could not even be made
            }
            finally
            {
                if (collection is not null)
                    Marshal.FinalReleaseComObject(collection);
                if (list is not null)
                    Marshal.FinalReleaseComObject(list);
            }
        }

        /// <summary>Takes the whole list of the given application away (the tests clean up with it).</summary>
        internal static void Delete(string appId)
        {
            var list = (ICustomDestinationList)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidDestinationList, throwOnError: true)!)!;
            try { list.DeleteList(appId); }
            finally { Marshal.FinalReleaseComObject(list); }
        }

        private static IShellLinkW MakeLink(string folder, string title)
        {
            var exe = Application.ExecutablePath;
            var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellLink, throwOnError: true)!)!;
            try
            {
                Check(link.SetPath(exe), "SetPath");
                Check(link.SetArguments(ArgumentsFor(folder)), "SetArguments");
                Check(link.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? string.Empty), "SetWorkingDirectory");
                Check(link.SetDescription(folder), "SetDescription"); // the tip, when the pointer rests on the entry: the whole path
                Check(link.SetIconLocation(Path.Combine(Environment.SystemDirectory, "imageres.dll"), FolderIcon), "SetIconLocation");

                // What the list writes in the entry is its title (a link has none: it is a property)
                var store = (IPropertyStore)link;
                var key = TitleKey;
                var value = new PropVariant { Type = VtLpwstr, Value = Marshal.StringToCoTaskMemUni(title) }; // cleared below: Windows frees the string
                try
                {
                    Check(store.SetValue(ref key, ref value), "SetValue(title)");
                    Check(store.Commit(), "Commit");
                }
                finally
                {
                    PropVariantClear(ref value);
                }

                return link;
            }
            catch
            {
                Marshal.FinalReleaseComObject(link);
                throw;
            }
        }

        private static List<string> ArgumentsOf(IObjectArray array)
        {
            var result = new List<string>();
            try
            {
                if (array.GetCount(out var count) < 0)
                    return result;

                var linkId = IidShellLink;
                for (uint i = 0; i < count; i++)
                {
                    if (array.GetAt(i, ref linkId, out var item) < 0)
                        continue;

                    try
                    {
                        var text = new StringBuilder(1024);
                        if (((IShellLinkW)item).GetArguments(text, text.Capacity) >= 0)
                            result.Add(text.ToString());
                    }
                    catch (InvalidCastException)
                    {
                        // not a link (we made nothing else): nothing to compare it with
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(item);
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(array);
            }

            return result;
        }

        private static void Check(int hresult, string what)
        {
            if (hresult < 0)
                throw new COMException($"{what} failed (0x{hresult:X8})", hresult);
        }

        // ── Windows ────────────────────────────────────────────────────────

        private const int FolderIcon = 3; // the picture of a folder in imageres.dll, which is the one Explorer shows

        private static readonly Guid ClsidDestinationList = new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
        private static readonly Guid ClsidObjectCollection = new("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
        private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
        private static readonly Guid IidObjectArray = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
        private static readonly Guid IidShellLink = new("000214F9-0000-0000-C000-000000000046");
        private static readonly PropertyKey TitleKey = new() { Format = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), Id = 2 }; // PKEY_Title

        private const ushort VtLpwstr = 31; // VT_LPWSTR: the value is a pointer to a string made with the task allocator

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
        }

        /// <summary>A PROPVARIANT: the type, and eight bytes (two pointers on a 32 bit system) of value; a string is made and let go of by Windows.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort Type;
            public ushort Reserved1, Reserved2, Reserved3;
            public IntPtr Value;
            public IntPtr Value2;
        }

        [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICustomDestinationList
        {
            [PreserveSig] int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
            [PreserveSig] int BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
            [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, [MarshalAs(UnmanagedType.Interface)] IObjectArray items);
            [PreserveSig] int AppendKnownCategory(int category);
            [PreserveSig] int AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray tasks);
            [PreserveSig] int CommitList();
            [PreserveSig] int GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
            [PreserveSig] int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
            [PreserveSig] int AbortList();
        }

        [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectArray
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
        }

        [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectCollection : IObjectArray
        {
            // IObjectArray
            [PreserveSig] new int GetCount(out uint count);
            [PreserveSig] new int GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

            // IObjectCollection
            [PreserveSig] int AddObject([MarshalAs(UnmanagedType.Interface)] object item);
            [PreserveSig] int AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray source);
            [PreserveSig] int RemoveObjectAt(uint index);
            [PreserveSig] int Clear();
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
            [PreserveSig] int GetIDList(out IntPtr idList);
            [PreserveSig] int SetIDList(IntPtr idList);
            [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int count);
            [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
            [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
            [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            [PreserveSig] int GetHotkey(out ushort hotkey);
            [PreserveSig] int SetHotkey(ushort hotkey);
            [PreserveSig] int GetShowCmd(out int showCmd);
            [PreserveSig] int SetShowCmd(int showCmd);
            [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int count, out int icon);
            [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
            [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRelative, uint reserved);
            [PreserveSig] int Resolve(IntPtr window, uint flags);
            [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }
    }
}
