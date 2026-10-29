using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>
/// Saving many photos into a group at once (Alt + a group key on the search results): the plan that is shown before and the
/// copying itself, against a real temp file system.
/// </summary>
public class GroupAddAllTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PLGroupAddAllTests_" + Guid.NewGuid());
    private readonly string _photosDir;
    private readonly string _likedDir;

    public GroupAddAllTests()
    {
        _photosDir = Path.Combine(_tempDir, "Photos");
        _likedDir = Path.Combine(_photosDir, "Liked");
        Directory.CreateDirectory(_photosDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private PhotoFile MakePhoto(string relativePath, string content = "photo")
    {
        var path = Path.Combine(_photosDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        var info = new FileInfo(path);
        return new PhotoFile(path, string.Empty) { Length = info.Length, LastWriteTimeUtc = info.LastWriteTimeUtc };
    }

    private PhotoFile[] MakePhotos(int count) =>
        [.. Enumerable.Range(0, count).Select(i => MakePhoto($"p{i:D3}.jpg", new string('x', 100 + i)))];

    private string GroupCopy(string folderName, PhotoFile photo) => Path.Combine(_likedDir, folderName, CopyName.For(photo));

    private GroupLibrary Scan() => GroupLibrary.Scan(_likedDir);

    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // ── the plan ────────────────────────────────────────────────────────────

    [Fact]
    public void PlanAdd_CountsThePhotosAndTheirBytes_AndCreatesNothing()
    {
        var photos = MakePhotos(3);

        var plan = Scan().PlanAdd('a', photos);

        Assert.Equal(photos, plan.ToCopy);
        Assert.Equal(photos.Sum(p => p.Length), plan.Bytes);
        Assert.Equal(0, plan.AlreadyThere);
        Assert.Equal('A', plan.Key);
        Assert.Equal(Path.Combine(_likedDir, "A - Group"), plan.Folder);
        Assert.False(Directory.Exists(_likedDir));
    }

    [Fact]
    public void PlanAdd_LeavesOutThePhotosTheGroupHasAlready()
    {
        var photos = MakePhotos(3);
        var groups = Scan();
        groups.Update(groups.Toggle('A', photos[1]));

        var plan = groups.PlanAdd('A', photos);

        Assert.Equal([photos[0], photos[2]], plan.ToCopy);
        Assert.Equal(1, plan.AlreadyThere);
        Assert.Equal(photos[0].Length + photos[2].Length, plan.Bytes);
    }

    [Fact]
    public void PlanAdd_LooksAtTheFolderAsItIsNow_NotAtWhatTheLibraryKnew()
    {
        var photos = MakePhotos(2);
        var groups = Scan();
        Scan().Toggle('A', photos[0]); // another program, or another run, copied it after the scan

        var plan = groups.PlanAdd('A', photos);

        Assert.Equal([photos[1]], plan.ToCopy);
        Assert.Equal(1, plan.AlreadyThere);
    }

    [Fact]
    public void PlanAdd_NoFolderYet_PlansOneNamedAfterTheSearch_AndCreatesNothing()
    {
        var plan = Scan().PlanAdd('A', MakePhotos(2), "beach, September 2009");

        Assert.True(plan.FolderIsNew);
        Assert.Equal(Path.Combine(_likedDir, "A - Group beach, September 2009"), plan.Folder);
        Assert.False(Directory.Exists(_likedDir));
    }

    [Fact]
    public void PlanAdd_NoFolderAndNoSearch_PlansTheUsualName()
    {
        var plan = Scan().PlanAdd('A', MakePhotos(2));

        Assert.True(plan.FolderIsNew);
        Assert.Equal(Path.Combine(_likedDir, "A - Group"), plan.Folder);
    }

    [Fact]
    public void PlanAdd_TheFolderIsThere_IsUsedWhateverItIsCalled()
    {
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group Family"));

        var plan = Scan().PlanAdd('A', MakePhotos(2), "beach");

        Assert.False(plan.FolderIsNew);
        Assert.Equal(Path.Combine(_likedDir, "A - Group Family"), plan.Folder);
    }

    [Fact]
    public void PlanAdd_OtherGroupsDoNotCount()
    {
        var photos = MakePhotos(2);
        var groups = Scan();
        groups.Update(groups.Toggle('B', photos[0]));

        Assert.Equal(2, groups.PlanAdd('A', photos).ToCopy.Count);
    }

    [Fact]
    public void PlanAdd_PhotosThatWouldBeSavedUnderOneName_AreSavedOnce()
    {
        var first = MakePhoto("one/a.jpg", "same");
        var second = new PhotoFile(Path.Combine(_photosDir, "two", "a.jpg"), string.Empty) { Length = first.Length, LastWriteTimeUtc = first.LastWriteTimeUtc };

        var plan = Scan().PlanAdd('A', [first, second]);

        Assert.Equal([first], plan.ToCopy);
        Assert.Equal(1, plan.AlreadyThere);
    }

    [Fact]
    public void PlanAdd_ACopyThatLiesInTheGroupFolder_IsThere()
    {
        var photo = MakePhoto("Liked/A - Group/a [3f9a1c2e].jpg");

        var plan = Scan().PlanAdd('A', [photo]);

        Assert.Empty(plan.ToCopy);
        Assert.Equal(1, plan.AlreadyThere);
    }

    // ── copying ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddAll_CopiesEveryPhoto_MakesTheFolders_AndLeavesTheOriginals()
    {
        var photos = MakePhotos(5);
        var groups = Scan();

        var result = groups.AddAll(groups.PlanAdd('A', photos), null, CancellationToken.None);

        Assert.Equal(5, result.Copied);
        Assert.Equal(photos.Sum(p => p.Length), result.Bytes);
        Assert.Empty(result.Failures);
        Assert.False(result.Cancelled);
        Assert.False(result.GaveUp);
        Assert.Equal(Path.Combine(_likedDir, "A - Group"), result.Folder);
        foreach (var photo in photos)
        {
            Assert.Equal(File.ReadAllText(photo.OriginalFilePath), File.ReadAllText(GroupCopy("A - Group", photo)));
            Assert.True(File.Exists(photo.OriginalFilePath));
        }
    }

    [Fact]
    public void AddAll_AfterUpdate_TheLibraryKnowsTheCopies()
    {
        var photos = MakePhotos(3);
        var groups = Scan();

        groups.Update(groups.AddAll(groups.PlanAdd('3', photos), null, CancellationToken.None));

        Assert.All(photos, photo => Assert.Equal(["3 - Group"], groups.FoldersOf(photo).Select(folder => Path.GetFileName(folder))));
        Assert.Empty(groups.PlanAdd('3', photos).ToCopy);
    }

    [Fact]
    public void AddAll_NeverTakesOutAPhotoThatHasACopyAlready()
    {
        var photos = MakePhotos(2);
        var plan = Scan().PlanAdd('A', photos);
        Scan().Toggle('A', photos[0]); // copied between the question and the answer

        var result = Scan().AddAll(plan, null, CancellationToken.None);

        Assert.Equal(1, result.Copied);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(GroupCopy("A - Group", photos[0])));
        Assert.True(File.Exists(GroupCopy("A - Group", photos[1])));
    }

    [Fact]
    public void AddAll_RunTwice_AddsNothingTheSecondTime()
    {
        var photos = MakePhotos(3);
        Scan().AddAll(Scan().PlanAdd('A', photos), null, CancellationToken.None);

        var plan = Scan().PlanAdd('A', photos);

        Assert.Empty(plan.ToCopy);
        Assert.Equal(3, plan.AlreadyThere);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_likedDir, "A - Group")).Length);
    }

    [Fact]
    public void AddAll_RenamedFolder_IsUsed()
    {
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group Family"));
        var photos = MakePhotos(2);
        var groups = Scan();

        var result = groups.AddAll(groups.PlanAdd('A', photos), null, CancellationToken.None);

        Assert.Equal(Path.Combine(_likedDir, "A - Group Family"), result.Folder);
        Assert.True(File.Exists(GroupCopy("A - Group Family", photos[0])));
        Assert.False(Directory.Exists(Path.Combine(_likedDir, "A - Group")));
    }

    [Fact]
    public void AddAll_NoFolderYet_MakesOneNamedAfterTheSearch_AndTheNextRunUsesIt()
    {
        var photos = MakePhotos(3);
        var groups = Scan();

        var result = groups.AddAll(groups.PlanAdd('A', photos, "beach"), null, CancellationToken.None);
        groups.Update(result);

        var folder = Path.Combine(_likedDir, "A - Group beach");
        Assert.Equal(folder, result.Folder);
        Assert.True(File.Exists(Path.Combine(folder, CopyName.For(photos[0]))));
        Assert.Equal(3, result.Copied);

        // Another search into the same group goes to the same folder and does not make a second one
        var more = MakePhoto("more.jpg", "more");
        var second = groups.PlanAdd('A', [.. photos, more], "snow");
        Assert.False(second.FolderIsNew);
        Assert.Equal(folder, groups.AddAll(second, null, CancellationToken.None).Folder);
        Assert.Single(Directory.GetDirectories(_likedDir));
    }

    [Fact]
    public void AddAll_APhotoThatCannotBeCopied_IsNoted_AndTheOthersGoOn()
    {
        var photos = MakePhotos(3);
        File.Delete(photos[1].OriginalFilePath);
        var groups = Scan();

        var result = groups.AddAll(groups.PlanAdd('A', photos), null, CancellationToken.None);

        Assert.Equal(2, result.Copied);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(photos[1].OriginalFilePath, failure.Path);
        Assert.False(result.GaveUp);
        Assert.False(File.Exists(GroupCopy("A - Group", photos[1]))); // no half copy
        Assert.True(File.Exists(GroupCopy("A - Group", photos[2])));
    }

    [Fact]
    public void AddAll_GivesUpWhenPhotoAfterPhotoFails()
    {
        var gone = Enumerable.Range(0, GroupLibrary.MaxFailuresInARow + 2).Select(i => MakePhoto($"gone{i:D3}.jpg", new string('x', 10 + i))).ToList();
        var fine = MakePhoto("fine.jpg", "fine");
        gone.ForEach(p => File.Delete(p.OriginalFilePath));
        var groups = Scan();

        var result = groups.AddAll(groups.PlanAdd('A', [.. gone, fine]), null, CancellationToken.None);

        Assert.True(result.GaveUp);
        Assert.Equal(GroupLibrary.MaxFailuresInARow, result.Failures.Count);
        Assert.Equal(0, result.Copied);
        Assert.False(File.Exists(GroupCopy("A - Group", fine)));
    }

    [Fact]
    public void AddAll_AFailureInTheMiddle_DoesNotCountAgainstLaterPhotos()
    {
        // Failures only count when nothing is copied in between
        var photos = Enumerable.Range(0, GroupLibrary.MaxFailuresInARow * 3).Select(i => MakePhoto($"p{i:D3}.jpg", new string('x', 10 + i))).ToList();
        for (var i = 0; i < photos.Count; i += 2)
            File.Delete(photos[i].OriginalFilePath);
        var groups = Scan();

        var result = groups.AddAll(groups.PlanAdd('A', photos), null, CancellationToken.None);

        Assert.False(result.GaveUp);
        Assert.Equal(photos.Count / 2, result.Copied);
        Assert.Equal(photos.Count / 2, result.Failures.Count);
    }

    [Fact]
    public void AddAll_Cancelled_StopsBeforeTheNextPhoto_WithoutAnError()
    {
        var photos = MakePhotos(3);
        var groups = Scan();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = groups.AddAll(groups.PlanAdd('A', photos), null, cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Copied);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void AddAll_ReportsHowFarItIs_AndEndsAtTheTotal()
    {
        var photos = MakePhotos(4);
        var groups = Scan();
        var reports = new List<GroupAddProgress>();

        var plan = groups.PlanAdd('A', photos);
        groups.AddAll(plan, new DirectProgress<GroupAddProgress>(reports.Add), CancellationToken.None);

        var last = reports[^1];
        Assert.Equal(4, last.Done);
        Assert.Equal(4, last.Total);
        Assert.Equal(plan.Bytes, last.BytesDone);
        Assert.Equal(plan.Bytes, last.BytesTotal);
        Assert.All(reports, r => Assert.InRange(r.Done, 1, 4));
    }
}
