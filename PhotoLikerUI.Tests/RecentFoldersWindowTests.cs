using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The folders that were opened last, in the real window: a right-click on the Open button lists them (newest first, the open one marked) and a click opens one; the list is
/// kept in the config file and read from it each time, so that a second window does not lose what the first one added; and the Jump List of the taskbar button is written
/// with it (the tests write to a recorder: a real list is tried in <see cref="RecentJumpListTests"/>).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class RecentFoldersWindowTests : IDisposable
{
    private static readonly string ConfigFile = Path.Combine(AppPaths.DataFolder, "global-config.json");

    private readonly TempFolder _root = new();
    private readonly JumpListRecorder _jumpList = new();
    private readonly string _alpha, _beta, _gamma;

    public RecentFoldersWindowTests()
    {
        DeleteConfig(); // every test starts from the defaults
        _alpha = MakePhotoFolder("Alpha");
        _beta = MakePhotoFolder("Beta");
        _gamma = MakePhotoFolder("Gamma & Delta"); // an ampersand in a name must not become an accelerator in the menu
    }

    public void Dispose()
    {
        _jumpList.Dispose();
        _root.Dispose();
        DeleteConfig();
    }

    private static void DeleteConfig()
    {
        try { File.Delete(ConfigFile); } catch (IOException) { }
    }

    private string MakePhotoFolder(string name)
    {
        var folder = _root.File(name);
        Directory.CreateDirectory(folder);
        using var bitmap = new Bitmap(40, 30);
        bitmap.Save(Path.Combine(folder, name.Replace(' ', '_').Replace('&', '_') + ".jpg"), ImageFormat.Jpeg);
        return folder;
    }

    /// <summary>The config file as the program that was used before left it: the last folder and the recent ones.</summary>
    private static void Start(string? last, params string[] recent) =>
        new GlobalConfig { LastFolder = last ?? string.Empty, RecentFolders = [.. recent] }.Save();

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static ToolStripButton OpenButton(Form form) => (ToolStripButton)Field<ToolStrip>(form, "toolStrip1").Items["openToolStripButton"]!;

    private static string CurrentFolder(Form form) => Field<Settings>(form, "_currentConfig").CurrentFolder;

    /// <summary>What a right-click does to the button: the right button goes down on it, and up (the toolbar tells the button under the pointer, as it does for a person's mouse).</summary>
    private static ContextMenuStrip RightClickOpen(Form form)
    {
        var strip = Field<ToolStrip>(form, "toolStrip1");
        var bounds = OpenButton(form).Bounds;
        var click = new MouseEventArgs(MouseButtons.Right, 1, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2, 0);

        typeof(ToolStrip).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(strip, [click]);
        typeof(ToolStrip).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(strip, [click]);
        return Field<ContextMenuStrip>(form, "_recentMenu");
    }

    private static List<ToolStripMenuItem> Folders(ContextMenuStrip menu) =>
        menu.Items.OfType<ToolStripMenuItem>().Where(item => item.Name?.StartsWith("recentFolderMenuItem", StringComparison.Ordinal) == true).ToList();

    private static Task<bool> ShowsPhotosOf(Form form, string folder) =>
        Ui.Until(() => Field<Settings>(form, "_currentConfig").Files.Any(file => file.OriginalFilePath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), 45000);

    // ── the menu ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARightClickOnOpen_ListsTheFoldersOpenedLast_NewestFirst_WithTheOpenOneMarked()
    {
        Start(_alpha, _beta, _gamma);

        Ui.Run(() => new MainForm(), async form =>
        {
            var menu = RightClickOpen(form);
            await Task.CompletedTask;

            var folders = Folders(menu);
            Assert.Equal([_alpha, _beta, _gamma], folders.Select(item => (string)item.Tag!)); // the folder the program started on is the newest
            Assert.Equal([true, false, false], folders.Select(item => item.Checked));
            Assert.StartsWith("&1  ", folders[0].Text);
            Assert.StartsWith("&2  ", folders[1].Text);
            Assert.Contains("Gamma && Delta", folders[2].Text); // an & is written twice in a menu text, or it is read as an accelerator
            Assert.Equal(_beta, folders[1].ToolTipText); // the whole path, when the pointer rests on it
            Assert.Contains(menu.Items.OfType<ToolStripMenuItem>(), item => item.Name == "recentClearMenuItem" && item.Enabled && item.Text == RecentStrings.MenuClear);
            // Not asked: whether the pop-up is still on the screen. It closes itself as soon as the window is not the active one, which a runner without a person at the desktop may not make it
        }, seconds: 60);
    }

    [Fact]
    public void WithNothingOpenedYet_TheMenuSaysSo_AndHasNothingToClear()
    {
        Start(null);

        Ui.Run(() => new MainForm(), async form =>
        {
            var menu = RightClickOpen(form);
            await Task.CompletedTask;

            Assert.Empty(Folders(menu));
            var none = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "recentNoneMenuItem");
            Assert.Equal(RecentStrings.MenuNone, none.Text);
            Assert.False(none.Enabled);
            Assert.False(menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "recentClearMenuItem").Enabled);
        }, seconds: 60);
    }

    [Fact]
    public void AClickOnAFolderOfTheList_OpensIt_AndPutsItFirst_AndKeepsItForTheNextStart()
    {
        Start(_alpha, _beta, _gamma);

        Ui.Run(() => new MainForm(), async form =>
        {
            var menu = RightClickOpen(form);
            Folders(menu)[2].PerformClick(); // Gamma, the oldest

            Assert.Equal(_gamma, CurrentFolder(form));
            Assert.True(await ShowsPhotosOf(form, _gamma), "the photos of the folder are listed");

            var saved = GlobalConfig.Load();
            Assert.Equal([_gamma, _alpha, _beta], saved.RecentFolders);
            Assert.Equal(_gamma, saved.LastFolder);
            Assert.Equal(_gamma, Field<GlobalConfig>(form, "_globalConfig").LastFolder);

            // The next right-click: the order is the new one, and the open folder is marked
            var again = Folders(RightClickOpen(form));
            Assert.Equal([_gamma, _alpha, _beta], again.Select(item => (string)item.Tag!));
            Assert.True(again[0].Checked);
        }, seconds: 90);
    }

    [Fact]
    public void AFolderThatIsNotThereAnyMore_IsTakenOffTheList_AndTheStatusSaysSo()
    {
        Start(_alpha, _beta, _gamma);
        Directory.Delete(_beta, recursive: true);

        Ui.Run(() => new MainForm(), async form =>
        {
            Folders(RightClickOpen(form))[1].PerformClick();
            await Task.CompletedTask;

            Assert.Equal(_alpha, CurrentFolder(form)); // nothing was opened
            Assert.Equal([_alpha, _gamma], GlobalConfig.Load().RecentFolders);
            var status = Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text;
            Assert.Equal(string.Format(RecentStrings.StatusFolderGoneFormat, _beta), status);
        }, seconds: 60);
    }

    [Fact]
    public void ClearTheList_EmptiesTheMenu_TheFile_AndTheJumpList()
    {
        Start(_alpha, _beta);

        Ui.Run(() => new MainForm(), async form =>
        {
            var clear = RightClickOpen(form).Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "recentClearMenuItem");
            clear.PerformClick();
            await Task.CompletedTask;

            Assert.Empty(GlobalConfig.Load().RecentFolders);
            Assert.Empty(_jumpList.Last.Folders);
            Assert.Empty(Folders(RightClickOpen(form)));
        }, seconds: 60);
    }

    // ── what puts a folder on the list ───────────────────────────────────────────────────────

    [Fact]
    public void TheFolderTheProgramIsStartedOn_IsOnTheList()
    {
        Start(null);

        Ui.Run(() => new MainForm(_beta), async form =>
        {
            await Task.CompletedTask;

            Assert.Equal([_beta], GlobalConfig.Load().RecentFolders);
            Assert.Equal([_beta], Folders(RightClickOpen(form)).Select(item => (string)item.Tag!));
        }, seconds: 60);
    }

    [Fact]
    public void TheFolderTheProgramWasLeftOn_IsOnTheListOfAFileThatHasNone()
    {
        // The config file of a version that did not keep the list: only the last folder
        Start(_alpha);

        Ui.Run(() => new MainForm(), async form =>
        {
            await Task.CompletedTask;

            Assert.Equal([_alpha], GlobalConfig.Load().RecentFolders);
        }, seconds: 60);
    }

    [Fact]
    public void AFolderThatIsOpenedTwice_IsOnTheListOnce()
    {
        Start(_alpha, _beta);

        Ui.Run(() => new MainForm(), async form =>
        {
            var main = (MainForm)form;
            main.OpenRecentFolder(_beta);
            main.OpenRecentFolder(_alpha);
            main.OpenRecentFolder(_alpha.ToUpperInvariant() + @"\"); // the same folder written another way: Windows does not tell them apart
            await Task.CompletedTask;

            var list = GlobalConfig.Load().RecentFolders;
            Assert.Equal(2, list.Count);
            Assert.Equal(_alpha, list[0], ignoreCase: true);
            Assert.Equal(_beta, list[1]);
        }, seconds: 60);
    }

    // ── the file, with two windows ───────────────────────────────────────────────────────────

    [Fact]
    public void WhatAnotherWindowAddedToTheFile_IsNotLost_WhenThisOneOpensAFolder_OrCloses()
    {
        Start(_alpha, _beta);

        Ui.Run(() => new MainForm(), async form =>
        {
            // The other window (the Jump List starts one) opened Gamma while this one was open
            var other = GlobalConfig.Load();
            other.RecentFolders = RecentFolders.Remember(other.RecentFolders, _gamma);
            other.Save();

            ((MainForm)form).OpenRecentFolder(_beta);
            await Task.CompletedTask;

            Assert.Equal([_beta, _gamma, _alpha], GlobalConfig.Load().RecentFolders);
        }, seconds: 60);

        // This window has closed and saved its own config: Gamma is still there
        Assert.Equal([_beta, _gamma, _alpha], GlobalConfig.Load().RecentFolders);
    }

    [Fact]
    public void AFolderAnotherWindowAddedAfterTheLastOpen_SurvivesThisWindowClosing()
    {
        Start(_alpha);

        Ui.Run(() => new MainForm(), async form =>
        {
            // This window has done nothing since it started; another one then opened Beta
            var other = GlobalConfig.Load();
            other.RecentFolders = RecentFolders.Remember(other.RecentFolders, _beta);
            other.Save();
            await Task.CompletedTask;
        }, seconds: 60);

        Assert.Equal([_beta, _alpha], GlobalConfig.Load().RecentFolders);
    }

    // ── the Jump List ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheJumpList_IsWritten_WhenTheWindowIsShown_AndAgainWhenAFolderIsOpened()
    {
        Start(_alpha, _beta);

        Ui.Run(() => new MainForm(), async form =>
        {
            Assert.True(await Ui.Until(() => _jumpList.Calls.Count >= 1, 30000), "written once the window is on the screen");
            Assert.Equal([_alpha, _beta], _jumpList.Last.Folders);
            Assert.Equal(RecentStrings.JumpListCategory, _jumpList.Last.Category);

            ((MainForm)form).OpenRecentFolder(_beta);

            Assert.Equal([_beta, _alpha], _jumpList.Last.Folders);
        }, seconds: 60);
    }

    [Fact]
    public void AFolderTheUserRemovedFromTheJumpList_IsTakenOffTheListOfTheProgramToo()
    {
        Start(_alpha, _beta, _gamma);
        _jumpList.Removed = [_beta]; // "Remove from this list" in Windows: it is told at the next write

        Ui.Run(() => new MainForm(), async form =>
        {
            Assert.True(await Ui.Until(() => _jumpList.Calls.Count >= 1, 30000));

            Assert.Equal([_alpha, _gamma], GlobalConfig.Load().RecentFolders);
            Assert.Equal([_alpha, _gamma], Folders(RightClickOpen(form)).Select(item => (string)item.Tag!));
        }, seconds: 60);
    }

    [Fact]
    public void TheHeadingOfTheJumpList_FollowsTheLanguage()
    {
        Start(_alpha);

        Ui.Run(() => new MainForm(), async form =>
        {
            Assert.True(await Ui.Until(() => _jumpList.Calls.Count >= 1, 30000));
            Assert.Equal("Recent folders", _jumpList.Last.Category);

            using (Loc.Use(AppLanguage.Find("ua")))
            {
                ((MainForm)form).SyncRecentJumpList();

                Assert.Equal("Нещодавні папки", _jumpList.Last.Category);
                Assert.Equal("Нещодавніх папок ще немає", RecentStrings.MenuNone);
            }
        }, seconds: 60);
    }

    [Fact]
    public void TheOpenButton_SaysInItsTooltipThatARightClickListsTheFolders()
    {
        Start(null);

        Ui.Run(() => new MainForm(), async form =>
        {
            await Task.CompletedTask;

            Assert.Equal(ToolbarStrings.OpenTooltip, OpenButton(form).ToolTipText);
            Assert.Contains("right-click", OpenButton(form).ToolTipText);
        }, seconds: 60);
    }

    /// <summary>Takes the place of the writer of the Jump List, and keeps what it was given.</summary>
    private sealed class JumpListRecorder : IDisposable
    {
        private readonly Func<IReadOnlyList<string>, string, JumpListResult> _before = RecentJumpList.Writer;

        public List<(string[] Folders, string Category)> Calls { get; } = [];

        /// <summary>The folders Windows says the person took off the list; told once.</summary>
        public string[] Removed { get; set; } = [];

        public (string[] Folders, string Category) Last => Calls[^1];

        public JumpListRecorder() => RecentJumpList.Writer = (folders, category) =>
        {
            var removed = Removed.Where(folders.Contains).ToList();
            Removed = [];
            Calls.Add(([.. folders.Where(folder => !removed.Contains(folder))], category));
            return new JumpListResult(null, removed);
        };

        public void Dispose() => RecentJumpList.Writer = _before;
    }
}
