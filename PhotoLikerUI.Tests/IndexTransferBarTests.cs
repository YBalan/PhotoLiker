using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The bar beside the I/E button, made for real: it fills as an index is exported or imported, says the percent in the status line, is what the taskbar button follows, and goes
/// away when the work is over. The file dialogs are the part that is not run here (they wait for a person); the work after them is.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class IndexTransferBarTests : IDisposable
{
    private static readonly string ConfigFile = Path.Combine(AppPaths.DataFolder, "global-config.json");

    private readonly TempFolder _folder = new();   // the photo folder of the window
    private readonly TempFolder _other = new();    // the folder an index was made in, to be imported into the first
    private readonly TempFolder _carried = new();  // where an exported file travels

    public IndexTransferBarTests() => DeleteConfig();

    public void Dispose()
    {
        foreach (var folder in new[] { _folder, _other })
        {
            try { File.Delete(SearchPaths.IndexFile(folder.Path)); } catch (IOException) { }
        }
        _folder.Dispose();
        _other.Dispose();
        _carried.Dispose();
        DeleteConfig();
    }

    private static void DeleteConfig()
    {
        try { File.Delete(ConfigFile); } catch (IOException) { }
    }

    /// <summary>Takes the question the import asks, and says yes (or no) for the person.</summary>
    private sealed class AnsweringForm(string folder, bool answer) : MainForm(folder)
    {
        public string? Question { get; private set; }

        public string? Error { get; private set; }

        protected override bool ConfirmImport(string question)
        {
            Question = question;
            return answer;
        }

        protected override void ShowTransferError(string title, string message) => Error = message;
    }

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static object? MaybeField(object owner, string name) =>
        typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);

    private static ToolStripProgressBar Bar(Form form) => Field<ToolStripProgressBar>(form, "_transferProgress");

    private static string StatusOf(Form form) => Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text ?? string.Empty;

    private static FolderIndex? OpenIndex(Form form) => (FolderIndex?)MaybeField(form, "_folderSearch");

    private static async Task Settle() => await Task.Delay(150);

    private static T Call<T>(Form form, string name, params object[] arguments) =>
        (T)typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, arguments)!;

    private static void Call(Form form, string name, params object[] arguments) =>
        typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, arguments);

    /// <summary>A folder with a photo the window can show and an index of <paramref name="photos"/> photos for it.</summary>
    private static void MakeIndexedFolder(TempFolder folder, int photos)
    {
        using (var bitmap = new Bitmap(64, 48))
            bitmap.Save(folder.File("a.jpg"), ImageFormat.Jpeg);

        var vector = new float[ClipModels.Default.Dimensions];
        vector[0] = 1;
        using var index = PhotoIndex.Open(folder.Path);
        index.Save(Enumerable.Range(0, photos).Select(i => new IndexedPhoto($"p/{i}.jpg", length: 1000, modifiedTicks: 5000 + i)
        {
            Clips = new Dictionary<string, ClipVector> { [ClipModels.Default.Id] = new((float[])vector.Clone(), IndexingJob.ClipVersion) },
        }).ToList());
    }

    [Fact]
    public void TheBar_SitsBesideTheIEButton_AndIsNotThereWhileNothingIsMoved()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var bar = Bar(form);
            var strip = bar.Owner!;

            Assert.Equal("indexTransferToolStripProgressBar", bar.Name);
            Assert.Equal("indexFileToolStripButton", strip.Items[0].Name);
            Assert.Same(bar, strip.Items[1]); // right after the button that starts the work
            Assert.False(bar.Visible);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void WhatTheWorkReports_FillsTheBar_SaysThePercent_AndIsWhatTheTaskbarShows_UntilTheWorkEnds()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var bar = Bar(form);

            var progress = Call<IProgress<double>>(form, "BeginTransferStage", "Moving {0} %");
            Assert.True(bar.Visible);
            Assert.Equal(0, bar.Value);
            Assert.Equal("Moving 0 %", StatusOf(form));

            await Task.Run(() => progress.Report(0.5)); // from another thread, as the work does
            await Settle();

            Assert.Equal(50, bar.Value);
            Assert.Equal("Moving 50 %", StatusOf(form));
            var sample = TaskbarProgress.Read(bar)!.Value;
            Assert.Equal(TaskbarProgressView.Of(sample), new TaskbarProgressView(TaskbarProgressState.Normal, 500)); // the button of the program fills to the same place

            // The button on the taskbar is told: the window watches this bar like the others
            var taskbar = Field<TaskbarProgress>(form, "_taskbarProgress");
            taskbar.Update();
            Assert.Equal(new TaskbarProgressView(TaskbarProgressState.Normal, 500), taskbar.Applied);

            // The window redrawing its controls while the work goes on does not take the bar away
            Call(form, "UpdateSearchControls");
            Assert.True(bar.Visible);

            Call(form, "EndTransferStage");
            Assert.False(bar.Visible);
            Assert.Null(TaskbarProgress.Read(bar));
            taskbar.Update();
            Assert.Equal(TaskbarProgressView.None, taskbar.Applied); // and the button is clear again

            // A report that was still on its way when the work ended is not news
            progress.Report(0.9);
            await Settle();
            Assert.Equal(50, bar.Value);
            Assert.Equal("Moving 50 %", StatusOf(form));
        }, seconds: 60);
    }

    [Fact]
    public void ExportingTheIndex_RunsTheBarToTheEnd_WritesTheFile_AndTakesTheBarAway()
    {
        MakeIndexedFolder(_folder, photos: 40);
        var file = Path.Combine(_carried.Path, "folder.plindex");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => OpenIndex(form) is not null), "the index of the folder is open");
            Assert.False(Bar(form).Visible);

            await ((MainForm)form).ExportIndexToAsync(OpenIndex(form)!, file);

            Assert.Equal(40, PhotoIndex.Inspect(file).Photos);
            Assert.Equal(100, Field<int>(form, "_transferPercent")); // the reports of the work got to the window, up to the last
            Assert.False(Bar(form).Visible);
            Assert.False(Field<bool>(form, "_transferBusy"));
            Assert.StartsWith("Index exported to", StatusOf(form));
        }, seconds: 60);
    }

    [Fact]
    public void ImportingAnIndex_ReadsTheFileThenReplacesTheIndex_EachWithTheBar_AndOpensTheNewOne()
    {
        MakeIndexedFolder(_folder, photos: 2);
        MakeIndexedFolder(_other, photos: 30);
        var file = Path.Combine(_carried.Path, "other.plindex");
        using (var made = PhotoIndex.Open(_other.Path))
            made.ExportTo(file);

        Ui.Run(() => new AnsweringForm(_folder.Path, answer: true), async form =>
        {
            Assert.True(await Ui.Until(() => OpenIndex(form)?.Index.PhotoCount == 2), "the folder has its own index of 2 photos");

            await ((MainForm)form).ImportIndexFromAsync(_folder.Path, file);

            var asked = ((AnsweringForm)form).Question!;
            Assert.Contains("30", asked); // the question says what the file holds
            Assert.Equal(100, Field<int>(form, "_transferPercent")); // the last stage ran to the end
            Assert.False(Bar(form).Visible);
            Assert.False(Field<bool>(form, "_transferBusy"));
            Assert.StartsWith("Index imported", StatusOf(form));
            Assert.True(await Ui.Until(() => OpenIndex(form)?.Index.PhotoCount == 30), "the new index is open");
        }, seconds: 60);
    }

    [Fact]
    public void ImportingAnIndex_ThatThePersonDoesNotWant_ChangesNothing_AndLeavesNoBar()
    {
        MakeIndexedFolder(_folder, photos: 2);
        MakeIndexedFolder(_other, photos: 30);
        var file = Path.Combine(_carried.Path, "other.plindex");
        using (var made = PhotoIndex.Open(_other.Path))
            made.ExportTo(file);

        Ui.Run(() => new AnsweringForm(_folder.Path, answer: false), async form =>
        {
            Assert.True(await Ui.Until(() => OpenIndex(form)?.Index.PhotoCount == 2), "the folder has its own index of 2 photos");

            await ((MainForm)form).ImportIndexFromAsync(_folder.Path, file);

            Assert.Equal(2, OpenIndex(form)!.Index.PhotoCount);
            Assert.False(Bar(form).Visible);
            Assert.False(Field<bool>(form, "_transferBusy"));
            Assert.Equal(string.Empty, StatusOf(form)); // the "reading" is not left on the screen
        }, seconds: 60);
    }

    [Fact]
    public void AFileThatIsNoIndex_IsReportedAsAnError_AndLeavesNoBar()
    {
        MakeIndexedFolder(_folder, photos: 2);
        var file = Path.Combine(_carried.Path, "junk.plindex");
        File.WriteAllBytes(file, [1, 2, 3, 4, 5, 6, 7, 8]);

        Ui.Run(() => new AnsweringForm(_folder.Path, answer: true), async form =>
        {
            Assert.True(await Ui.Until(() => OpenIndex(form)?.Index.PhotoCount == 2), "the folder has its own index of 2 photos");

            await ((MainForm)form).ImportIndexFromAsync(_folder.Path, file);

            Assert.False(Bar(form).Visible);
            Assert.False(Field<bool>(form, "_transferBusy"));
            Assert.StartsWith("Import failed", StatusOf(form));
            Assert.StartsWith("Import failed", ((AnsweringForm)form).Error); // and the person is told
            Assert.Equal(2, OpenIndex(form)!.Index.PhotoCount); // the index the folder had is still there
        }, seconds: 60);
    }
}
