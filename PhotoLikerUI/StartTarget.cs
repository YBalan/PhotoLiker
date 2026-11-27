namespace PhotoLikerUI
{
    /// <summary>What the program was started on, from its command line (see <c>Program.Main</c>).</summary>
    internal static class StartTarget
    {
        /// <summary>
        /// A folder (what the "Open in Photo Liker" entry of the folder menu passes) is opened as it is. A file (what Windows passes when an .stl file, or any other file the program is
        /// registered for, is double-clicked or chosen in "Open with") means the folder it is in, shown at that file. Anything else, or nothing, is no target: the program opens the folder it was left on.
        /// </summary>
        public static (string? Folder, string? File) Of(IReadOnlyList<string> args)
        {
            if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                return (null, null);

            var argument = args[0];
            if (Directory.Exists(argument))
                return (argument, null);

            if (File.Exists(argument))
            {
                var full = Path.GetFullPath(argument);
                return (Path.GetDirectoryName(full), full);
            }

            return (null, null);
        }
    }
}
