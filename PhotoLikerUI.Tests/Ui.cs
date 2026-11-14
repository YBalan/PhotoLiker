using System.Runtime.ExceptionServices;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Everything that opens real windows runs one test at a time: the open windows are one list for the whole program, and a test looks into it for the window it expects.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsCollection
{
    public const string Name = "Windows";
}

/// <summary>Runs a test body against a real window, on a thread of its own with a message loop, and looks around in the windows that are open.</summary>
internal static class Ui
{
    /// <summary>
    /// Opens the window made by <paramref name="create"/> on a new STA thread and runs <paramref name="body"/> once it is shown; the body may wait for things with <see cref="Until"/>
    /// and has the message loop going while it does. Whatever it throws (a failed assertion too) is thrown again on the caller's thread, after every window that is still open is closed.
    /// A body that does not finish in <paramref name="seconds"/> is a failure, so that a window that never answers does not hang the run.
    /// </summary>
    public static void Run(Func<Form> create, Func<Form, Task> body, int seconds = 120)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            var form = create();
            using var watchdog = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
            watchdog.Tick += (_, _) =>
            {
                failure ??= ExceptionDispatchInfo.Capture(new TimeoutException($"The test did not finish in {seconds} seconds."));
                CloseAll();
            };
            form.Shown += async (_, _) =>
            {
                try
                {
                    await body(form);
                }
                catch (Exception ex)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    CloseAll();
                }
            };

            watchdog.Start();
            try
            {
                Application.Run(form);
            }
            finally
            {
                form.Dispose();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    // The newest window first: a dialog that is open over another one has to go before it
    private static void CloseAll()
    {
        foreach (var form in Application.OpenForms.Cast<Form>().Reverse().ToList())
            form.Close();
    }

    /// <summary>
    /// True once the file is there and nobody has it open any more. A file that the program writes on another thread exists before it is whole (and a copy has its full length at once), so a test that
    /// reads it as soon as <c>File.Exists</c> says yes finds it half written or "being used by another process", which only a slow machine shows.
    /// </summary>
    public static Task<bool> FileIsWritten(string path, int milliseconds = 30000) =>
        Until(() =>
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false; // not there yet, or still open
            }
        }, milliseconds);

    public static async Task<bool> Until(Func<bool> condition, int milliseconds = 30000)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < end)
        {
            if (condition())
                return true;
            await Task.Delay(40);
        }
        return condition();
    }

    public static IEnumerable<T> Controls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T found)
                yield return found;
            foreach (var inner in Controls<T>(child))
                yield return inner;
        }
    }

    /// <summary>The open window of a kind (by the name of its class, which is internal to the app), or null.</summary>
    public static Form? Find(string className) => Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == className);

    public static Button ButtonStartingWith(Control root, string text) => Controls<Button>(root).First(b => b.Text.StartsWith(text));

    public static RadioButton RadioStartingWith(Control root, string text) => Controls<RadioButton>(root).First(r => r.Text.StartsWith(text));

    public static bool HasRadio(Control root, string text) => Controls<RadioButton>(root).Any(r => r.Text == text);

    /// <summary>The text of the first label that starts with this, or an empty text.</summary>
    public static string LabelStartingWith(Control root, string text) => Controls<Label>(root).Select(l => l.Text).FirstOrDefault(t => t.StartsWith(text)) ?? string.Empty;
}

/// <summary>A folder of photos under the temp folder of the machine, taken away again.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PLUiTests_" + Guid.NewGuid().ToString("N"));

    public TempFolder()
    {
        Directory.CreateDirectory(Path);
        Directory.CreateDirectory(Liked);
    }

    public string Liked => System.IO.Path.Combine(Path, "Liked");

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>A file with these bytes and these dates (UTC) for when it was created and last changed.</summary>
    public string Write(string name, byte[] bytes, DateTime fileDateUtc)
    {
        var path = File(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, bytes);
        System.IO.File.SetCreationTimeUtc(path, fileDateUtc);
        System.IO.File.SetLastWriteTimeUtc(path, fileDateUtc);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
