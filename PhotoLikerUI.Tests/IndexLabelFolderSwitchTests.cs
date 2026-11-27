using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The "Update index" button and the label beside it (how much of the folder is indexed) follow the folder that is shown: when another folder is opened they say what is known of
/// that one, also the videos of it (their catalog is opened while the folder is loaded, which is after the label was first written). The window is made with a real video from the
/// ffmpeg of the program (the test returns early without it).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class IndexLabelFolderSwitchTests : IDisposable
{
    private const string Clip = "clip-vit-b32";
    private static readonly string ConfigFile = Path.Combine(AppPaths.DataFolder, "global-config.json");

    private readonly TempFolder _root = new();

    public IndexLabelFolderSwitchTests() => DeleteConfig();

    public void Dispose()
    {
        _root.Dispose();
        DeleteConfig();
    }

    private static void DeleteConfig()
    {
        try { File.Delete(ConfigFile); } catch (IOException) { }
    }

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static string IndexLabel(Form form) => Field<ToolStripLabel>(form, "_indexLabel").Text ?? string.Empty;

    private static string IndexButton(Form form) => Field<ToolStripButton>(form, "_indexButton").Text ?? string.Empty;

    private static Task<bool> ShowsPhotosOf(Form form, string folder) =>
        Ui.Until(() => Field<Settings>(form, "_currentConfig").Files.Any(file => file.OriginalFilePath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), 45000);

    [Fact]
    public void OpeningAnotherFolder_ChangesTheIndexButtonAndLabel_ToWhatIsKnownOfThatFolder()
    {
        if (TestFfmpeg.Path is null)
            return;
        using var _ = TestFfmpeg.Use();

        // One folder whose video has been analyzed, one with a photo and nothing indexed
        var withVideo = _root.File("WithVideo");
        var plain = _root.File("Plain");
        Directory.CreateDirectory(withVideo);
        Directory.CreateDirectory(plain);
        var video = TestFfmpeg.MakeMp4(withVideo, "clip.mp4", "2003-01-31T15:06:00Z");
        using (var bitmap = new Bitmap(40, 30))
            bitmap.Save(Path.Combine(plain, "p.jpg"), ImageFormat.Jpeg);
        new Settings { CurrentFolder = withVideo, SupportVideo = true }.Write();
        new Settings { CurrentFolder = plain, SupportVideo = true }.Write();

        Ui.Run(() => new MainForm(withVideo), async form =>
        {
            Assert.True(await ShowsPhotosOf(form, withVideo), "the folder with the video is shown");
            Assert.True(await Ui.Until(() => VideoLibrary.Current is not null), "its catalog is open");

            var catalog = VideoLibrary.Current!;
            var known = await catalog.GetAsync(video);
            catalog.SaveAnalysis(known.Id, Clip, [new MomentData(0, [1, 0, 0], [0xFF, 0xD8, 0xFF, 0xD9])]);
            typeof(MainForm).GetMethod("UpdateSearchControls", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);

            var videosText = string.Format(VideoSearchStrings.IndexedVideosFormat, 1, 1);
            Assert.Equal(videosText, IndexLabel(form));
            Assert.Equal(SearchStrings.UpdateIndexButton, IndexButton(form));

            // Another folder: nothing of it is indexed, so the label must not keep counting the videos of the first one
            ((MainForm)form).OpenRecentFolder(plain);
            Assert.True(await ShowsPhotosOf(form, plain), "the other folder is shown");
            Assert.Equal(SearchStrings.NotIndexed, IndexLabel(form));
            Assert.Equal(SearchStrings.IndexButton, IndexButton(form));

            // And back: the videos known of the first folder are counted again
            ((MainForm)form).OpenRecentFolder(withVideo);
            Assert.True(await ShowsPhotosOf(form, withVideo), "the first folder is shown again");
            Assert.Equal(videosText, IndexLabel(form));
            Assert.Equal(SearchStrings.UpdateIndexButton, IndexButton(form));
        }, seconds: 120);
    }
}
