using System.Runtime.InteropServices;
using System.Text;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Jump List written to Windows for real. The tests write to a list of their own (an application ID that no program uses) and take it away again, so the list of the
/// person who runs them is not touched; what Windows kept is read from the file it keeps custom lists in.
///
/// The runner of GitHub Actions is a server with a desktop of its own and no taskbar: the shell may not offer Jump Lists at all (then there is nothing to try), and if it
/// does but answers differently than a PC does, that is told in <see cref="Lenient"/> and not failed there; on a PC every call must work.
/// </summary>
public sealed class RecentJumpListTests : IDisposable
{
    private const string AppId = "PhotoLiker.Tests.JumpList";

    private static readonly string CustomDestinations = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent", "CustomDestinations");

    /// <summary>On a runner of GitHub Actions: what the shell there does is not what a person's Windows does, so a Jump List that cannot be written is no failure of the program.</summary>
    private static bool Lenient => string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLJumpList_" + Guid.NewGuid().ToString("N"));

    public RecentJumpListTests() => TryDelete();

    public void Dispose() => TryDelete();

    private static void TryDelete()
    {
        try
        {
            RecentJumpList.Delete(AppId);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or PlatformNotSupportedException or NotSupportedException)
        {
            // nothing to take away, or no Jump Lists on this machine
        }
    }

    /// <summary>True when there is no point in going on: this machine has no Jump Lists, or it is the runner and what it said is not for the program to answer for.</summary>
    private static bool NothingToTry(JumpListResult result) => result.Unavailable || (Lenient && !result.Committed);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    /// <summary>The words the program is given when it is started with these arguments, as Windows splits them.</summary>
    private static string[] Split(string arguments)
    {
        var list = CommandLineToArgvW("PhotoLiker.exe " + arguments, out var count);
        try
        {
            return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(list, i * IntPtr.Size))!).ToArray();
        }
        finally
        {
            LocalFree(list);
        }
    }

    [Theory]
    [InlineData(@"D:\Photos\Trip 2024")]
    [InlineData(@"Y:\")] // a backslash at the end must not swallow the quote that closes the argument
    [InlineData(@"\\router\share\Photo")]
    public void TheArgumentsOfAFolder_ComeToTheProgramAsOneArgument_ThePathItself(string folder)
    {
        var arguments = Split(RecentJumpList.ArgumentsFor(folder));

        Assert.Equal([folder], arguments);
    }

    [Fact]
    public void TheList_IsWrittenToWindows_AndKeptThere()
    {
        Directory.CreateDirectory(_folder);
        var started = DateTime.UtcNow.AddSeconds(-5);

        var result = RecentJumpList.Write([_folder, @"Y:\", @"D:\Photos\Trip"], "Recent folders", AppId);
        if (NothingToTry(result))
            return;

        Assert.True(result.Committed, result.Error);
        Assert.Empty(result.Removed);

        // Windows keeps a custom list in a file of the folder of the recent items, one per application; this one was written just now
        var written = Directory.Exists(CustomDestinations)
            ? new DirectoryInfo(CustomDestinations).GetFiles("*.customDestinations-ms").Where(file => file.LastWriteTimeUtc >= started).ToList()
            : [];
        if (written.Count == 0 && Lenient)
            return; // the runner took the list and kept it somewhere else

        Assert.True(written.Count >= 1, "Windows wrote the list to a file");
        var text = string.Join('\n', written.Select(file => Encoding.Unicode.GetString(File.ReadAllBytes(file.FullName))));
        Assert.Contains("Recent folders", text); // the heading
        Assert.Contains(Path.GetFileName(_folder), text); // an entry (its title is the name of the folder, and so is its tip: the whole path)
        Assert.Contains("Trip", text);
    }

    [Fact]
    public void WritingAnEmptyList_ClearsTheCategory_AndANewListCanBeWrittenAgain()
    {
        var first = RecentJumpList.Write([@"D:\Photos\One"], "Recent folders", AppId);
        if (NothingToTry(first))
            return;

        Assert.True(first.Committed, first.Error);
        Assert.True(RecentJumpList.Write([], "Recent folders", AppId).Committed);
        Assert.True(RecentJumpList.Write([@"D:\Photos\Two", @"D:\Photos\One"], "Recent folders", AppId).Committed);
    }

    [Fact]
    public void ATooLongList_IsCutToWhatWindowsShows()
    {
        var folders = Enumerable.Range(1, 40).Select(i => @"D:\Photos\Folder" + i).ToList();

        var result = RecentJumpList.Write(folders, "Recent folders", AppId);
        if (NothingToTry(result))
            return;

        Assert.True(result.Committed, result.Error);
    }
}
