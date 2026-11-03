using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class TaskbarProgressViewTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(1, 3, 333)]
    [InlineData(2, 3, 667)]
    [InlineData(50, 100, 500)]
    [InlineData(100, 100, 1000)]
    [InlineData(250, 100, 1000)]   // a bar that went past its end is full, not more
    [InlineData(-5, 100, 0)]
    [InlineData(1_500_000, 3_000_000, 500)]
    public void ABarWithAnEnd_IsFilledByTheShareDone(double done, double total, int permille) =>
        Assert.Equal(new TaskbarProgressView(TaskbarProgressState.Normal, permille), TaskbarProgressView.Of(new ProgressSample(false, done, total)));

    [Fact]
    public void AMarquee_RunsToAndFro() =>
        Assert.Equal(TaskbarProgressState.Indeterminate, TaskbarProgressView.Of(new ProgressSample(true)).State);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    [InlineData(double.NaN, 10)]
    public void ABarWithNothingToFill_RunsToAndFro_NotDividedByZero(double done, double total)
    {
        var view = TaskbarProgressView.Of(new ProgressSample(false, done, total));

        if (!double.IsNaN(done))
            Assert.Equal(TaskbarProgressState.Indeterminate, view.State);
        Assert.InRange(view.Permille, 0, TaskbarProgressView.Scale);
    }

    [Fact]
    public void NothingRunning_ShowsNothing()
    {
        Assert.Equal(TaskbarProgressView.None, TaskbarProgressView.Choose([]));
        Assert.Equal(TaskbarProgressView.None, TaskbarProgressView.Choose([null, null]));
    }

    [Fact]
    public void ThatWhichStartedLast_IsOnTheButton()
    {
        // the bar of the window is at 20 %, a dialog opened over it is at 80 %: the dialog is the one in front
        var dialog = new ProgressSample(false, 80, 100);
        var window = new ProgressSample(false, 20, 100);

        Assert.Equal(800, TaskbarProgressView.Choose([dialog, window]).Permille);
        Assert.Equal(800, TaskbarProgressView.Choose([null, dialog, window]).Permille);
    }

    [Fact]
    public void WhenTheLastOneIsOver_TheOneBeforeItShowsAgain()
    {
        var window = new ProgressSample(false, 20, 100);

        Assert.Equal(200, TaskbarProgressView.Choose([null, window]).Permille);
    }

    [Fact]
    public void TheBarsBehindTheChosenOne_AreNotLookedAt()
    {
        var looked = new List<int>();
        IEnumerable<ProgressSample?> Sources()
        {
            looked.Add(1);
            yield return new ProgressSample(true);
            looked.Add(2);
            yield return new ProgressSample(false, 1, 2);
        }

        Assert.Equal(TaskbarProgressState.Indeterminate, TaskbarProgressView.Choose(Sources()).State);
        Assert.Equal([1], looked);
    }
}
