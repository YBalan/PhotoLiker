using PhotoLiker.Core;
using PhotoLiker.PhotoBook;

namespace PhotoLikerUI.Tests;

/// <summary>The thumbnail view of the book window, in a real window: where the cells are, what a click, a drag and a key do, undo, and which photos are read.</summary>
[Collection(WindowsCollection.Name)]
public sealed class ThumbnailReorderViewTests
{
    private static List<BookEntry> Entries(int count) => [.. Enumerable.Range(1, count).Select(number => new BookEntry($@"C:\fake\p{number:D3}.jpg"))];

    private static string Names(ThumbnailReorderView view) => string.Join(" ", view.Entries.Select(entry => Path.GetFileNameWithoutExtension(entry.Path)[1..].TrimStart('0')));

    private static Bitmap Fake(string path)
    {
        var bitmap = new Bitmap(40, 30);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(60 + Math.Abs(path.GetHashCode()) % 150, 120, 200));
        return bitmap;
    }

    /// <summary>The paths the decoder of the last window made by <see cref="Host"/> was asked for (the tests of this class run one at a time).</summary>
    private static List<string> _lastRead = [];

    /// <summary>A window with the view in it (640 x 440), the photos in it, and the decoder of the thumbnails that is told what was read.</summary>
    private static (Form Form, ThumbnailReorderView View, List<string> Read) Host(int count, Func<string, Bitmap?>? decode = null)
    {
        var read = _lastRead = [];
        var cache = new ThumbnailCache(path =>
        {
            lock (read)
                read.Add(path);
            return (decode ?? Fake)(path);
        });
        var view = new ThumbnailReorderView(cache) { Dock = DockStyle.Fill, EmptyText = "empty", LeftOutText = "left out" };
        var form = new Form { ClientSize = new Size(640, 440), StartPosition = FormStartPosition.CenterScreen, ShowInTaskbar = false };
        form.Controls.Add(view);
        view.SetEntries(Entries(count));
        return (form, view, read);
    }

    private static Point Middle(ThumbnailReorderView view, int index)
    {
        var cell = view.CellBounds(index);
        return new Point(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
    }

    // ── Where the cells are ─────────────────────────────────────────────────

    [Fact]
    public void TheCells_AreInRows_FromTheLeft_AndAHitFindsThem()
    {
        Ui.Run(() => Host(40).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var columns = view.Columns;
            Assert.True(columns >= 3, $"{columns} columns");

            Assert.Equal(new Point(0, 0), view.CellBounds(0).Location);
            Assert.Equal(view.CellBounds(0).Width, view.CellBounds(1).X);
            Assert.Equal(0, view.CellBounds(columns).X);
            Assert.Equal(view.CellBounds(0).Height, view.CellBounds(columns).Y);

            foreach (var index in new[] { 0, 1, columns - 1, columns, 2 * columns + 1 })
                Assert.Equal(index, view.IndexAt(Middle(view, index)));

            Assert.Equal(-1, view.IndexAt(new Point(-5, 10)));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ADropPoint_IsInFrontOfTheCellWhoseLeftHalfItIsIn_AndBehindTheOneWhoseRightHalf()
    {
        Ui.Run(() => Host(40).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var cell = view.CellBounds(2);

            Assert.Equal(2, view.InsertionIndexAt(new Point(cell.X + 10, cell.Y + 20)));
            Assert.Equal(3, view.InsertionIndexAt(new Point(cell.Right - 10, cell.Y + 20)));
            Assert.Equal(0, view.InsertionIndexAt(new Point(5, -50)));
            Assert.Equal(40, view.InsertionIndexAt(new Point(5, 100000)));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheZoom_ChangesTheCells_AndStaysInItsRange()
    {
        Ui.Run(() => Host(40).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var before = view.Columns;

            view.ThumbnailSize = 100;
            Assert.True(view.Columns > before);
            view.ThumbnailSize = 5000;
            Assert.Equal(320, view.ThumbnailSize);
            view.ThumbnailSize = 1;
            Assert.Equal(80, view.ThumbnailSize);
            await Task.CompletedTask;
        });
    }

    // ── Picking ─────────────────────────────────────────────────────────────

    [Fact]
    public void AClick_PicksOne_CtrlAddsAndTakesAway_ShiftPicksARange()
    {
        Ui.Run(() => Host(40).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var changes = 0;
            view.SelectionChanged += (_, _) => changes++;

            view.Press(Middle(view, 3), Keys.None);
            view.Release(Middle(view, 3));
            Assert.Equal([3], view.SelectedIndices);

            view.Press(Middle(view, 6), Keys.Control);
            view.Release(Middle(view, 6));
            Assert.Equal([3, 6], view.SelectedIndices);

            view.Press(Middle(view, 3), Keys.Control);
            view.Release(Middle(view, 3));
            Assert.Equal([6], view.SelectedIndices);

            view.Press(Middle(view, 1), Keys.None);
            view.Release(Middle(view, 1));
            view.Press(Middle(view, 5), Keys.Shift);
            view.Release(Middle(view, 5));
            Assert.Equal([1, 2, 3, 4, 5], view.SelectedIndices);
            Assert.True(changes >= 4);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AClickOnAPhotoOfASelection_PicksThatOne_WhenTheButtonGoesUpWithoutADrag()
    {
        Ui.Run(() => Host(20).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(2, 3, 4);

            view.Press(Middle(view, 3), Keys.None);
            Assert.Equal([2, 3, 4], view.SelectedIndices); // kept: it may be the start of a drag of all three
            view.Release(Middle(view, 3));

            Assert.Equal([3], view.SelectedIndices);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AClickInTheEmptyRoom_PicksNothing()
    {
        Ui.Run(() => Host(3).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(1);

            view.Press(new Point(600, 400), Keys.None);
            view.Release(new Point(600, 400));

            Assert.Empty(view.SelectedIndices);
            await Task.CompletedTask;
        });
    }

    // ── Dragging ────────────────────────────────────────────────────────────

    [Fact]
    public void DraggingAPhoto_PutsItWhereTheLineIs()
    {
        Ui.Run(() => Host(8).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var orderChanges = 0;
            view.OrderChanged += (_, _) => orderChanges++;

            // photo 1 dragged to the gap between 3 and 4 (the right half of the cell of 3)
            var target = view.CellBounds(2);
            var drop = new Point(target.Right - 10, target.Y + 30);
            view.Press(Middle(view, 0), Keys.None);
            view.DragTo(new Point(Middle(view, 0).X + 40, Middle(view, 0).Y));
            Assert.True(view.IsDragging);
            view.DragTo(drop);
            Assert.Equal(3, view.DropIndex);
            view.Release(drop);

            Assert.Equal("2 3 1 4 5 6 7 8", Names(view));
            Assert.Equal([2], view.SelectedIndices);
            Assert.Equal(1, orderChanges);
            Assert.False(view.IsDragging);
            Assert.Equal(-1, view.DropIndex);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void DraggingSeveralPhotos_MovesThemTogether_InTheirOrder()
    {
        Ui.Run(() => Host(8).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(1, 5);

            var start = Middle(view, 5);
            var target = view.CellBounds(7);
            var drop = new Point(target.Right - 10, target.Y + 30); // the end
            view.Press(start, Keys.None);
            view.DragTo(new Point(start.X + 30, start.Y + 30));
            view.DragTo(drop);
            view.Release(drop);

            Assert.Equal("1 3 4 5 7 8 2 6", Names(view));
            Assert.Equal([6, 7], view.SelectedIndices);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ADragThatEndsOutsideTheView_MovesNothing()
    {
        Ui.Run(() => Host(8).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();

            view.Press(Middle(view, 0), Keys.None);
            view.DragTo(new Point(300, 200));
            Assert.True(view.IsDragging);
            view.Release(new Point(-100, -100));

            Assert.Equal("1 2 3 4 5 6 7 8", Names(view));
            Assert.False(view.CanUndo);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void Escape_EndsADrag_WithoutMovingAnything()
    {
        Ui.Run(() => Host(8).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();

            view.Press(Middle(view, 0), Keys.None);
            view.DragTo(new Point(300, 200));
            Assert.True(view.IsDragging);
            Assert.True(view.HandleKey(Keys.Escape));

            Assert.False(view.IsDragging);
            Assert.Equal("1 2 3 4 5 6 7 8", Names(view));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AMovementOfAFewPixels_IsAClick_NotADrag()
    {
        Ui.Run(() => Host(8).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();

            var start = Middle(view, 2);
            view.Press(start, Keys.None);
            view.DragTo(new Point(start.X + 1, start.Y + 1));
            Assert.False(view.IsDragging);
            view.Release(new Point(start.X + 1, start.Y + 1));

            Assert.Equal([2], view.SelectedIndices);
            Assert.Equal("1 2 3 4 5 6 7 8", Names(view));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AutoScroll_WhileDragging_MovesTheViewDownAtTheEdge()
    {
        Ui.Run(() => Host(200).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            Assert.Equal(0, -view.AutoScrollPosition.Y);

            var start = Middle(view, 0);
            view.Press(start, Keys.None);
            view.DragTo(new Point(start.X + 30, start.Y + 30));
            var edge = new Point(start.X, view.ClientSize.Height - 4); // the bottom edge
            view.DragTo(edge);

            // A window that is in front and has the mouse captured is told where the REAL cursor is (a mouse move the system makes up), and that moved the held
            // pointer off the edge: the view then rightly stopped scrolling, and whether this test passed depended on where the cursor of the machine was (it
            // failed once on the CI runner and on 3 of 6 fresh runs here). A hand that holds the mouse at the edge puts it back there at once, so the test does the same at every look.
            Assert.True(await Ui.Until(() => { view.DragTo(edge); return -view.AutoScrollPosition.Y > 100; }, 5000), "the view scrolled by itself");

            view.CancelDrag();
            await Task.CompletedTask;
        });
    }

    // ── The keyboard ────────────────────────────────────────────────────────

    [Fact]
    public void TheArrows_MoveTheFocus_ShiftExtends_CtrlAPicksAll()
    {
        Ui.Run(() => Host(40).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var columns = view.Columns;
            view.Select(0);

            Assert.True(view.HandleKey(Keys.Right));
            Assert.Equal([1], view.SelectedIndices);
            Assert.True(view.HandleKey(Keys.Down));
            Assert.Equal([1 + columns], view.SelectedIndices);
            view.HandleKey(Keys.Left);
            view.HandleKey(Keys.Up);
            Assert.Equal([0], view.SelectedIndices);

            view.HandleKey(Keys.Right | Keys.Shift);
            view.HandleKey(Keys.Right | Keys.Shift);
            Assert.Equal([0, 1, 2], view.SelectedIndices);

            view.HandleKey(Keys.End);
            Assert.Equal([39], view.SelectedIndices);
            view.HandleKey(Keys.Home);
            Assert.Equal([0], view.SelectedIndices);

            view.HandleKey(Keys.A | Keys.Control);
            Assert.Equal(40, view.SelectedIndices.Count);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AltAndTheArrows_MoveThePickedPhotos_AltHomeAndEndSendThemToTheEnds()
    {
        Ui.Run(() => Host(30).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var columns = view.Columns;
            view.Select(1);

            Assert.True(view.HandleKey(Keys.Right | Keys.Alt));
            Assert.Equal([2], view.SelectedIndices);
            Assert.StartsWith("1 3 2 4", Names(view));

            view.HandleKey(Keys.Left | Keys.Alt);
            Assert.StartsWith("1 2 3 4", Names(view));

            view.HandleKey(Keys.Down | Keys.Alt);
            Assert.Equal([1 + columns], view.SelectedIndices);

            view.HandleKey(Keys.Home | Keys.Alt);
            Assert.Equal([0], view.SelectedIndices);
            Assert.StartsWith("2 1", Names(view));

            view.HandleKey(Keys.End | Keys.Alt);
            Assert.Equal([29], view.SelectedIndices);
            Assert.EndsWith(" 2", Names(view));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void Delete_LeavesThePickedPhotosOut_AndAgainPutsThemBack()
    {
        Ui.Run(() => Host(6).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(1, 2);

            view.HandleKey(Keys.Delete);
            Assert.Equal([false, true, true, false, false, false], view.Entries.Select(entry => entry.LeftOut));

            view.HandleKey(Keys.Delete);
            Assert.All(view.Entries, entry => Assert.False(entry.LeftOut));

            // A pick of a left out photo and one that is not: all of them are left out first
            view.Select(0);
            view.ToggleLeftOut();
            view.Select(0, 1);
            view.ToggleLeftOut();
            Assert.True(view.Entries[0].LeftOut && view.Entries[1].LeftOut);
            await Task.CompletedTask;
        });
    }

    // ── Undo ────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryChange_CanBeUndoneAndDoneAgain_AndANewChangeEndsTheRedo()
    {
        Ui.Run(() => Host(6).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            Assert.False(view.CanUndo);

            view.Select(0);
            view.MoveSelectedBy(2);
            view.Select(5);
            view.ToggleLeftOut();
            Assert.Equal("2 3 1 4 5 6", Names(view));
            Assert.True(view.Entries[5].LeftOut);

            view.Undo();
            Assert.False(view.Entries[5].LeftOut);
            view.Undo();
            Assert.Equal("1 2 3 4 5 6", Names(view));
            Assert.False(view.CanUndo);
            Assert.True(view.CanRedo);

            view.Redo();
            Assert.Equal("2 3 1 4 5 6", Names(view));

            view.Select(3);
            view.MoveSelectedToStart();
            Assert.False(view.CanRedo); // a change after an undo ends what could be done again
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AMoveThatChangesNothing_IsNoStepOfTheUndo()
    {
        Ui.Run(() => Host(6).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(0);

            view.MoveSelectedBy(-1);
            view.MoveSelectedToStart();

            Assert.False(view.CanUndo);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheUndo_IsAHundredStepsDeep()
    {
        Ui.Run(() => Host(6).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(0);
            for (var i = 0; i < 120; i++)
                view.MoveSelectedBy(i % 2 == 0 ? 1 : -1);

            var steps = 0;
            while (view.CanUndo)
            {
                view.Undo();
                steps++;
            }

            Assert.Equal(100, steps);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ReverseAndSort_KeepThePickedPhotosPicked()
    {
        Ui.Run(() => Host(5).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(0);

            view.ReverseAll();
            Assert.Equal("5 4 3 2 1", Names(view));
            Assert.Equal([4], view.SelectedIndices);

            view.SortAll(PhotoSortOrder.ByName);
            Assert.Equal("1 2 3 4 5", Names(view));
            Assert.Equal([0], view.SelectedIndices);
            view.Undo();
            Assert.Equal("5 4 3 2 1", Names(view));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void MoveToPlace_PutsTheFirstPickedPhotoAtThatNumber()
    {
        Ui.Run(() => Host(6).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(0, 1);

            view.MoveSelectedToPlace(4);

            Assert.Equal("3 4 5 1 2 6", Names(view));
            Assert.Equal([3, 4], view.SelectedIndices);
            await Task.CompletedTask;
        });
    }

    // ── What is read, and what is drawn ─────────────────────────────────────

    [Fact]
    public void OnlyThePhotosOnTheScreen_AreRead()
    {
        Ui.Run(() => Host(400).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var read = _lastRead;

            view.Invalidate();
            view.Update();
            Assert.True(await Ui.Until(() => Count(read) > 5, 10000), "the first photos were read");
            await Task.Delay(300);

            var first = Count(read);
            Assert.True(first < 60, $"{first} of 400 photos were read for a screen of about {view.Columns * 3}");
            Assert.DoesNotContain(@"C:\fake\p400.jpg", Snapshot(read));

            view.AutoScrollPosition = new Point(0, view.CellBounds(0).Height * 40); // far down (the setter takes the offset as a positive number)
            view.Invalidate();
            view.Update();
            Assert.True(await Ui.Until(() => Snapshot(read).Any(path => string.CompareOrdinal(path, @"C:\fake\p100.jpg") > 0), 10000), "the photos that came on the screen were read");
        });
    }

    private static int Count(List<string> read)
    {
        lock (read)
            return read.Count;
    }

    private static List<string> Snapshot(List<string> read)
    {
        lock (read)
            return [.. read];
    }

    [Fact]
    public void APhotoThatCannotBeRead_IsDrawnWithAQuestionMark_AndTheViewKeepsWorking()
    {
        Ui.Run(() => Host(6, decode: _ => null).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Invalidate();
            view.Update();
            await Task.Delay(400);

            using var picture = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(picture, new Rectangle(0, 0, view.Width, view.Height));
            Assert.True(picture.Width > 0);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePhotos_AreDrawn_WithTheirNumbers_TheMarksOfTheirTexts_AndLeftOutOnesDimmed()
    {
        Ui.Run(() => Host(6).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.AccentColor = Color.FromArgb(0, 120, 215);
            view.Entries[1].AboveRtf = "x";
            view.Entries[2].BelowRtf = "y";
            view.Select(3);
            view.Entries[4].LeftOut = true;

            view.Invalidate();
            view.Update();
            await Task.Delay(500);
            view.Invalidate();
            view.Update();

            using var picture = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(picture, new Rectangle(0, 0, view.Width, view.Height));

            // The accent colour is there: on the picked photo (its fill and its frame) and on the two marks
            var accent = 0;
            for (var y = 0; y < picture.Height; y += 2)
            for (var x = 0; x < picture.Width; x += 2)
            {
                var pixel = picture.GetPixel(x, y);
                if (Math.Abs(pixel.B - 215) < 40 && pixel.R < 60 && pixel.G > 90 && pixel.G < 160)
                    accent++;
            }

            Assert.True(accent > 40, $"{accent} accent pixels");
        });
    }

    [Fact]
    public void AnEmptyList_SaysSo()
    {
        Ui.Run(() => Host(0).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();

            view.Invalidate();
            view.Update();

            Assert.Equal(-1, view.FocusIndex);
            Assert.Empty(view.SelectedIndices);
            view.HandleKey(Keys.Delete); // nothing to leave out: and no failure
            view.HandleKey(Keys.Right);
            await Task.CompletedTask;
        });
    }

    // ── Turning ─────────────────────────────────────────────────────────────

    [Fact]
    public void RotateSelected_TurnsEachPickedPhoto_ByAQuarter_AndAroundTheFullCircle()
    {
        Ui.Run(() => Host(5).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(1, 3);

            view.RotateSelected(1);
            Assert.Equal([0, 90, 0, 90, 0], view.Entries.Select(entry => entry.Rotation));

            view.RotateSelected(1);
            view.RotateSelected(1);
            view.RotateSelected(1);
            Assert.All(view.Entries, entry => Assert.Equal(0, entry.Rotation));

            view.RotateSelected(-1);
            Assert.Equal([0, 270, 0, 270, 0], view.Entries.Select(entry => entry.Rotation));
            Assert.Equal([1, 3], view.SelectedIndices);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ATurn_IsAStepOfTheUndo_AndTurnsAreKeptByTheMovesOfThePhoto()
    {
        Ui.Run(() => Host(4).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            var changes = 0;
            view.OrderChanged += (_, _) => changes++;
            view.Select(0);

            view.RotateSelected(1);
            view.MoveSelectedToEnd();
            Assert.Equal(90, view.Entries[3].Rotation); // it turned with the photo
            Assert.Equal(2, changes);

            view.Undo(); // the move
            view.Undo(); // the turn
            Assert.Equal(0, view.Entries[0].Rotation);
            view.Redo();
            Assert.Equal(90, view.Entries[0].Rotation);
            Assert.Equal(0, view.Entries[1].Rotation);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void NothingPicked_NothingIsTurned_AndNoStepIsNoted()
    {
        Ui.Run(() => Host(3).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select();

            view.RotateSelected(1);

            Assert.False(view.CanUndo);
            Assert.All(view.Entries, entry => Assert.Equal(0, entry.Rotation));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void CtrlPlusAndCtrlMinus_TurnThePickedPhotos_LikeTheMainWindowDoes()
    {
        Ui.Run(() => Host(3).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Select(1);

            Assert.True(view.HandleKey(Keys.Oemplus | Keys.Control));
            Assert.Equal(90, view.Entries[1].Rotation);
            Assert.True(view.HandleKey(Keys.Oemplus | Keys.Control | Keys.Shift)); // some keyboards need Shift for the plus
            Assert.Equal(180, view.Entries[1].Rotation);
            Assert.True(view.HandleKey(Keys.Add | Keys.Control)); // the plus of the numpad
            Assert.Equal(270, view.Entries[1].Rotation);
            Assert.True(view.HandleKey(Keys.OemMinus | Keys.Control));
            Assert.True(view.HandleKey(Keys.Subtract | Keys.Control));
            Assert.Equal(90, view.Entries[1].Rotation);

            Assert.False(view.HandleKey(Keys.Oemplus), "the plus alone is not a key of the view");
            await Task.CompletedTask;
        });
    }

    /// <summary>A picture whose left half is red and whose right half is blue, to see which way a thumbnail is turned.</summary>
    private static Bitmap RedAndBlue(string path)
    {
        var bitmap = new Bitmap(40, 30);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.FillRectangle(Brushes.Red, 0, 0, 20, 30);
        graphics.FillRectangle(Brushes.Blue, 20, 0, 20, 30);
        return bitmap;
    }

    [Theory]
    [InlineData(0, "left red", "right blue")]
    [InlineData(90, "top red", "bottom blue")]
    [InlineData(180, "left blue", "right red")]
    [InlineData(270, "top blue", "bottom red")]
    public void ATurnedThumbnail_IsDrawnTurned(int rotation, string first, string second)
    {
        Ui.Run(() => Host(1, decode: RedAndBlue).Form, async form =>
        {
            var view = Ui.Controls<ThumbnailReorderView>(form).Single();
            view.Entries[0].Rotation = rotation;
            view.Select(); // nothing picked: the thumbnail is not covered by the colour of a pick
            view.Invalidate();
            view.Update();
            await Task.Delay(500); // the thumbnail is read in the background
            view.Invalidate();
            view.Update();

            using var picture = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(picture, new Rectangle(0, 0, view.Width, view.Height));

            var cell = view.CellBounds(0);
            var pad = (int)Math.Round(8 * view.DeviceDpi / 96f);
            var box = new Rectangle(cell.X + pad, cell.Y + pad, view.ThumbnailSize, view.ThumbnailSize * 3 / 4);
            var middle = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);

            Color At(string where) => where switch
            {
                "left red" or "left blue" => picture.GetPixel(middle.X - 15, middle.Y),
                "right blue" or "right red" => picture.GetPixel(middle.X + 15, middle.Y),
                "top red" or "top blue" => picture.GetPixel(middle.X, middle.Y - 30),
                _ => picture.GetPixel(middle.X, middle.Y + 30),
            };

            static bool IsRed(Color c) => c.R > 200 && c.B < 80;
            static bool IsBlue(Color c) => c.B > 200 && c.R < 80;
            Assert.True(first.EndsWith("red") ? IsRed(At(first)) : IsBlue(At(first)), $"{first}: {At(first)}");
            Assert.True(second.EndsWith("red") ? IsRed(At(second)) : IsBlue(At(second)), $"{second}: {At(second)}");
        });
    }
}
