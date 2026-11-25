using System.Drawing.Imaging;
using PhotoLiker.Core;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The "Photos from" box of the Scan manuscripts window, as the Make a book window has it: Current (the loaded photos) is picked, the liked photos and the groups can be picked, and each place has a
/// manuscript of its own, kept in its folder. The folders are real ones under the temp folder of the machine; nothing is asked of the person, and the keys of the AI services are a temporary file.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptSourcesTests : IDisposable
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png"];

    private readonly TempFolder _folder = new();

    public ManuscriptSourcesTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string Liked => _folder.Liked;

    private string Group => Path.Combine(Liked, "A - Group family");

    private static string Photo(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        using var bitmap = new Bitmap(320, 240);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(240, 236, 220));

        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    /// <summary>Two loaded photos in the folder of the program, three liked ones, and one in a group.</summary>
    private (string[] Loaded, string[] Liked, string[] Group) Photos()
    {
        string[] loaded = [Photo(_folder.Path, "a.jpg"), Photo(_folder.Path, "b.jpg")];
        string[] liked = [Photo(Liked, "l1.jpg"), Photo(Liked, "l2.jpg"), Photo(Liked, "l3.jpg")];
        string[] group = [Photo(Group, "g1.jpg")];
        return (loaded, liked, group);
    }

    private ManuscriptDialog Dialog(IReadOnlyList<string> loaded, string? liked = "", AiServiceSettings? settings = null)
    {
        var dialog = new ManuscriptDialog(
            _folder.Path, loaded.Take(1).ToList(), new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true), loadedPhotos: loaded,
            aiServices: settings ?? AiServiceSettings.Load(_folder.File("ai.json"), new MaskingProtector()), likedFolder: liked == "" ? Liked : liked, extensions: Extensions);
        dialog.AskPagePaths = () => null;
        dialog.AskRemovePage = _ => true;
        dialog.AskReplaceText = () => true;
        dialog.AskBookPath = () => null;
        return dialog;
    }

    private static string[] Items(ManuscriptDialog dialog) => [.. dialog.SourceCombo.Items.Cast<object>().Select(item => item.ToString()!)];

    private static string[] Names(ManuscriptDialog dialog) => [.. dialog.Pages.Select(page => Path.GetFileName(page.File)!)];

    /// <summary>Picks a place in the box, as a person does, and waits until it is open.</summary>
    private static async Task PickAsync(ManuscriptDialog dialog, string name)
    {
        var index = dialog.SourceCombo.Items.Cast<ManuscriptDialog.PageSource>().ToList().FindIndex(source => source.Name == name);
        Assert.True(index >= 0, $"{name} is in the box");
        dialog.SourceCombo.SelectedIndex = index;
        Assert.True(await Ui.Until(() => dialog.OpenSourceName == name && !dialog.IsSwitchingSource), $"{name} was opened");
    }

    // ── The box ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheBoxIsOverTheListOfPages_WithCurrentPicked_AndTheLikedPhotosToPickToo()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(BookStrings.LabelPhotosFrom, dialog.SourceLabel.Text);
            Assert.Equal(["Current (2)", "Liked (...)"], Items(dialog)); // the loaded photos are counted; the folder is counted in the background
            Assert.Equal(0, dialog.SourceCombo.SelectedIndex);
            Assert.Equal(BookStrings.SourceCurrent, dialog.OpenSourceName);
            Assert.Equal(_folder.Path, dialog.ManuscriptFolder);
            Assert.Equal(ComboBoxStyle.DropDownList, DropDownStyleOf(dialog));
            Assert.Equal(["a.jpg", "b.jpg"], Names(dialog)); // the pages are the loaded photos, as they were before the box
            Assert.Same(dialog.SourceCombo.Parent!.Parent, dialog.PageList.Parent); // it is in the panel of the list, over it
            await Task.CompletedTask;
        });
    }

    private static ComboBoxStyle DropDownStyleOf(ManuscriptDialog dialog) => dialog.SourceCombo.DropDownStyle;

    [Fact]
    public void WithoutALikedFolder_TheBoxHasTheLoadedPhotosOnly()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded, liked: null), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(["Current (2)"], Items(dialog));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheLikedFolderAndItsGroups_AreCounted_InTheBackground_WhenTheWindowIsShown()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.True(await Ui.Until(() => Items(dialog).SequenceEqual(["Current (2)", "Liked (3)", "A - Group family (1)"])), string.Join(" | ", Items(dialog)));
        });
    }

    [Fact]
    public void ALikedFolderThatIsNotThere_IsInTheBox_WithNoPhotos()
    {
        var (loaded, _, _) = Photos();
        Directory.Delete(Liked, recursive: true);

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.True(await Ui.Until(() => Items(dialog).SequenceEqual(["Current (2)", "Liked (0)"])), string.Join(" | ", Items(dialog)));
        });
    }

    [Fact]
    public void ThePhotosOfAFolder_AreOnlyThoseWithTheExtensionsThatAreAskedFor_NotVideosNorOtherFiles()
    {
        var (loaded, _, _) = Photos();
        File.WriteAllBytes(Path.Combine(Liked, "clip.mp4"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(Liked, "notes.txt"), "not a photo");

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.True(await Ui.Until(() => Items(dialog).Contains("Liked (3)")), string.Join(" | ", Items(dialog)));
            await Task.CompletedTask;
        });
    }

    // ── Opening another place ───────────────────────────────────────────────

    [Fact]
    public void PickingLiked_OpensItsManuscript_ItsPhotosAreThePages_InTheOrderOfTheirNames()
    {
        var (loaded, liked, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            await PickAsync(dialog, BookStrings.SourceLiked);

            Assert.Equal(["l1.jpg", "l2.jpg", "l3.jpg"], Names(dialog));
            Assert.Equal(Liked, dialog.ManuscriptFolder);
            Assert.Equal(0, dialog.CurrentIndex);
            Assert.Equal(3, dialog.PageList.Items.Count);
            Assert.Equal(string.Format(ManuscriptStrings.StatusSourceOpenedFormat, BookStrings.SourceLiked, 3), dialog.StatusText);
            Assert.Equal(new DirectoryInfo(Liked).Name, dialog.TitleBox.Text); // a manuscript that has no title is called after its folder
            Assert.True(await Ui.Until(() => dialog.View.HasImage), "the photo of the first page is shown");
            Assert.All(liked, path => Assert.Contains(dialog.Pages, page => string.Equals(page.File, path, StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Fact]
    public void PickingAGroup_OpensItsPhotos()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => Items(dialog).Count() == 3));

            await PickAsync(dialog, "A - Group family");

            Assert.Equal(["g1.jpg"], Names(dialog));
            Assert.Equal(Group, dialog.ManuscriptFolder);
        });
    }

    [Fact]
    public void TheManuscriptThatIsOpen_IsSavedToItsFolder_BeforeAnotherIsShown_AndComesBackWithItsText()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "text of the first loaded page";

            await PickAsync(dialog, BookStrings.SourceLiked);

            // It went to its own folder, and what is in the window now is the other manuscript
            var kept = ManuscriptFile.Load(_folder.Path)!;
            Assert.Equal("text of the first loaded page", ManuscriptText.PlainOf(kept.Pages![0].Rtf));
            Assert.Equal(string.Empty, dialog.Editor.PlainText);
            Assert.False(File.Exists(ManuscriptFile.GetPath(Liked)), "nothing was done in the liked folder yet");

            // Something written there goes to the liked folder, not to the first
            dialog.Editor.Box.Text = "text of a liked page";
            await PickAsync(dialog, BookStrings.SourceCurrent);

            Assert.Equal("text of a liked page", ManuscriptText.PlainOf(ManuscriptFile.Load(Liked)!.Pages![0].Rtf));
            Assert.Equal("text of the first loaded page", dialog.Editor.PlainText); // the first is back, as it was left
            Assert.Equal(["a.jpg", "b.jpg"], Names(dialog));
            Assert.Equal(_folder.Path, dialog.ManuscriptFolder);
        });
    }

    [Fact]
    public void EachPlace_HasItsOwnSettings_TitleAuthorAndTheBoxesOfTheBook()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.TitleBox.Text = "My diary";
            dialog.AuthorBox.Text = "Olha";
            dialog.IncludePhotosCheck.Checked = true;
            dialog.UseContextCheck.Checked = true;
            dialog.ScanPicturesCheck.Checked = false;

            await PickAsync(dialog, BookStrings.SourceLiked);

            Assert.Equal(new DirectoryInfo(Liked).Name, dialog.TitleBox.Text);
            Assert.Equal(string.Empty, dialog.AuthorBox.Text);
            Assert.False(dialog.IncludePhotosCheck.Checked);
            Assert.False(dialog.CutPagesCheck.Enabled);
            Assert.False(dialog.UseContextCheck.Checked);
            Assert.True(dialog.ScanPicturesCheck.Checked); // the defaults of a manuscript that was not kept yet
            dialog.TitleBox.Text = "Liked scans";

            await PickAsync(dialog, BookStrings.SourceCurrent);

            Assert.Equal("My diary", dialog.TitleBox.Text);
            Assert.Equal("Olha", dialog.AuthorBox.Text);
            Assert.True(dialog.IncludePhotosCheck.Checked);
            Assert.True(dialog.UseContextCheck.Checked);
            Assert.False(dialog.ScanPicturesCheck.Checked);
            Assert.Equal("Liked scans", ManuscriptFile.Load(Liked)!.Title);
        });
    }

    [Fact]
    public void LookingAtAPlace_WritesNothingInItsFolder_NorInTheOneThatWasLeft()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            await PickAsync(dialog, BookStrings.SourceLiked);
            await PickAsync(dialog, BookStrings.SourceCurrent);
            await PickAsync(dialog, BookStrings.SourceLiked);
        });

        Assert.False(File.Exists(ManuscriptFile.GetPath(_folder.Path)));
        Assert.False(File.Exists(ManuscriptFile.GetPath(Liked)));
    }

    [Fact]
    public void ThePageThatWasOpen_IsOpenAgain_WhenAPlaceIsReopened()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await PickAsync(dialog, BookStrings.SourceLiked);
            dialog.SelectPage(2);
            dialog.Editor.Box.Text = "on the third";
            await PickAsync(dialog, BookStrings.SourceCurrent);

            await PickAsync(dialog, BookStrings.SourceLiked);

            Assert.Equal(2, dialog.CurrentIndex);
            Assert.Equal("on the third", dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void APhotoTakenOutInOnePlace_StaysOutThere_AndIsStillAPageOfTheOther()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await PickAsync(dialog, BookStrings.SourceLiked);
            dialog.RemoveCurrentPage(); // l1.jpg

            await PickAsync(dialog, BookStrings.SourceCurrent);
            Assert.Equal(["a.jpg", "b.jpg"], Names(dialog));

            await PickAsync(dialog, BookStrings.SourceLiked);
            Assert.Equal(["l2.jpg", "l3.jpg"], Names(dialog)); // it did not come back with the photos of the folder
        });
    }

    [Fact]
    public void WhatWasTakenOutInOnePlace_IsNotWrittenToTheManuscriptOfTheOther()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await PickAsync(dialog, BookStrings.SourceLiked);
            dialog.RemoveCurrentPage(); // l1.jpg goes out of the liked manuscript

            await PickAsync(dialog, BookStrings.SourceCurrent);
            dialog.Editor.Box.Text = "something typed, so that this manuscript is kept too";
            dialog.SaveWork();

            Assert.Null(ManuscriptFile.Load(_folder.Path)!.RemovedPages); // the list of the liked one stayed with it
        });

        Assert.Single(ManuscriptFile.Load(Liked)!.RemovedPages!);
    }

    [Fact]
    public void APhotoThatIsNewInAFolder_IsAddedToItsManuscript_AtTheEnd()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await PickAsync(dialog, BookStrings.SourceLiked);
            dialog.Editor.Box.Text = "kept";
            await PickAsync(dialog, BookStrings.SourceCurrent);

            Photo(Liked, "l0.jpg");
            // The folder was listed already: a photo that comes later is found the next time the window is opened, not in this window
            await PickAsync(dialog, BookStrings.SourceLiked);

            Assert.Equal(["l1.jpg", "l2.jpg", "l3.jpg"], Names(dialog));
        });

        // A window opened afterwards lists the folder again
        Ui.Run(() => Dialog([Photo(_folder.Path, "a.jpg")]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await PickAsync(dialog, BookStrings.SourceLiked);

            Assert.Equal(["l1.jpg", "l2.jpg", "l3.jpg", "l0.jpg"], Names(dialog)); // the pages that were kept come first, the new photo after them
        });
    }

    [Fact]
    public void AddPages_AddsToTheManuscriptThatIsOpen_AndItIsKeptThere()
    {
        var (loaded, _, _) = Photos();
        var elsewhere = Photo(Path.Combine(_folder.Path, "elsewhere"), "x.jpg");

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await PickAsync(dialog, BookStrings.SourceLiked);

            dialog.AddPagePaths([elsewhere]);
            await PickAsync(dialog, BookStrings.SourceCurrent);

            Assert.Equal(["a.jpg", "b.jpg"], Names(dialog)); // not in the other manuscript
        });

        var kept = ManuscriptFile.Load(Liked)!;
        Assert.Contains(kept.Pages!, page => string.Equals(page.File, elsewhere, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APlaceWithNoPhotos_HasNoPages_AndSaysSo_AndWhatWasOpenBeforeIsNotLeftOnTheScreen()
    {
        var (loaded, _, _) = Photos();
        Directory.CreateDirectory(Path.Combine(Liked, "B - Group empty"));

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "text of a loaded page";
            Assert.True(await Ui.Until(() => Items(dialog).Length == 4));

            await PickAsync(dialog, "B - Group empty");

            Assert.Empty(dialog.Pages);
            Assert.Equal(-1, dialog.CurrentIndex);
            Assert.Equal(string.Empty, dialog.Editor.PlainText);
            Assert.False(dialog.View.HasImage);
            Assert.Equal(string.Format(ManuscriptStrings.StatusSourceOpenedFormat, "B - Group empty", 0), dialog.StatusText);
            Assert.False(dialog.ScanPageButton.Enabled);
        });
    }

    [Fact]
    public void PickingThePlaceThatIsOpen_AgainChangesNothing()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "typed";

            dialog.SourceCombo.SelectedIndex = 0;

            Assert.Equal("typed", dialog.Editor.PlainText);
            Assert.False(dialog.IsSwitchingSource);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhileAScanWorks_TheBoxIsOff_AndAPickIsPutBack_SoTheScanAndTheManuscriptAreNotMixed()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            using var started = new ManualResetEventSlim();
            dialog.ScanPicturesCheck.Checked = false;
            dialog.ScanOverride = (_, _, _, cancel, _) =>
            {
                started.Set();
                cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                cancel.ThrowIfCancellationRequested();
                return new ScanResult([]);
            };

            var scanning = dialog.ScanPageAsync();
            Assert.True(await Ui.Until(() => started.IsSet));
            Assert.False(dialog.SourceCombo.Enabled);

            dialog.SourceCombo.SelectedIndex = 1; // asked for anyway

            Assert.Equal(0, dialog.SourceCombo.SelectedIndex); // put back
            Assert.Equal(BookStrings.SourceCurrent, dialog.OpenSourceName);
            Assert.Equal(_folder.Path, dialog.ManuscriptFolder);

            dialog.StopButton.PerformClick();
            await scanning;
            Assert.True(dialog.SourceCombo.Enabled);
        });
    }

    [Fact]
    public void WhileAPlaceIsOpened_TheBoxIsOff_AndItIsOnAgainWhenDone()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => Items(dialog).Contains("Liked (3)")));

            await PickAsync(dialog, BookStrings.SourceLiked);

            Assert.True(dialog.SourceCombo.Enabled);
            Assert.False(dialog.IsBusy);
            Assert.True(dialog.Editor.Box.ReadOnly == false);
        });
    }

    [Fact]
    public void TheTextTypedInOnePlace_CannotBeUndoneIntoTheOther()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Select(0, 0);
            dialog.Editor.Box.SelectedText = "typed in the first";
            Assert.True(dialog.Editor.UndoButton.Enabled);

            await PickAsync(dialog, BookStrings.SourceLiked);

            Assert.False(dialog.Editor.UndoButton.Enabled);
            Assert.False(dialog.Editor.RedoButton.Enabled);
        });
    }

    [Fact]
    public void ThePhotosOfALikedPlace_CanBeScanned_AndTheTextGoesToItsManuscript()
    {
        var (loaded, _, _) = Photos();

        Ui.Run(() => Dialog(loaded), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await PickAsync(dialog, BookStrings.SourceLiked);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.ScanPicturesCheck.Checked = false;
            dialog.ScanOverride = (_, _, _, _, _) => new ScanResult([new TextLine("read in liked", 0.9f, TextRegion.FromCentre(new PointF(160, 40), 200, 30, 0))]);

            await dialog.ScanPageAsync();
        });

        Assert.Equal("read in liked", ManuscriptText.PlainOf(ManuscriptFile.Load(Liked)!.Pages![0].Rtf));
        Assert.Null(ManuscriptFile.Load(_folder.Path)); // nothing in the folder of the first
    }
}
