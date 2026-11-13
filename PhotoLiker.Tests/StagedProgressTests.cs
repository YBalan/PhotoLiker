using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>The bar of work that goes in stages (an index is copied, then another, then both are packed), and the copy that says how far it is.</summary>
public class StagedProgressTests
{
    /// <summary>Takes the reports where they are made (Progress&lt;T&gt; would post them to another thread).</summary>
    private sealed class Reports : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }

    [Fact]
    public void EachStage_TakesTheShareOfTheBarItsWeightSays()
    {
        var reports = new Reports();
        var stages = new StagedProgress(reports, 1, 3); // a quarter, then three quarters

        stages.Stage(0).Report(0.5);
        stages.Stage(0).Report(1);
        stages.Stage(1).Report(0);
        stages.Stage(1).Report(0.5);
        stages.Stage(1).Report(1);

        Assert.Equal([0.125, 0.25, 0.25, 0.625, 1.0], reports.Values);
    }

    [Fact]
    public void AStageThatIsReportedPastItsEnd_StaysInsideItsShare()
    {
        var reports = new Reports();
        var stages = new StagedProgress(reports, 1, 1);

        stages.Stage(0).Report(7);
        stages.Stage(0).Report(-3);

        Assert.Equal([0.5, 0.0], reports.Values);
    }

    [Fact]
    public void AStageOfNoWeight_TakesNoPartOfTheBar()
    {
        var reports = new Reports();
        var stages = new StagedProgress(reports, 2, 0, 2);

        stages.Stage(1).Report(1);
        stages.Stage(2).Report(0);

        Assert.Equal([0.5, 0.5], reports.Values);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-4, 0)]
    [InlineData(double.NaN, double.PositiveInfinity)]
    public void StagesWithNoSizeToGoBy_SplitTheBarEvenly(double first, double second)
    {
        var reports = new Reports();
        var stages = new StagedProgress(reports, first, second);

        stages.Stage(0).Report(1);
        stages.Stage(1).Report(1);

        Assert.Equal([0.5, 1.0], reports.Values);
    }

    [Fact]
    public void Complete_FillsTheBar()
    {
        var reports = new Reports();

        new StagedProgress(reports, 5).Complete();

        Assert.Equal([1.0], reports.Values);
    }

    [Fact]
    public void WithNobodyLooking_NothingIsRequired()
    {
        var stages = new StagedProgress(null, 1, 2);

        stages.Stage(0).Report(0.5);
        stages.Stage(1).Report(1);
        stages.Complete(); // does not throw
    }

    [Fact]
    public void StagesOfStages_AddUp()
    {
        // The packing of two files is one stage of the export; its own two stages share it by the sizes of the files
        var reports = new Reports();
        var export = new StagedProgress(reports, 1, 1);
        var packing = new StagedProgress(export.Stage(1), 3, 1);

        packing.Stage(0).Report(1);
        packing.Stage(1).Report(1);

        Assert.Equal([0.5 + 0.5 * 0.75, 1.0], reports.Values);
    }

    // ── the copy ───────────────────────────────────────────────────────

    [Fact]
    public void ACopy_IsTheSameBytes_AndSaysHowFarItIsOnTheWay()
    {
        var data = new byte[5 * (1 << 20) + 123];
        new Random(1).NextBytes(data);
        var reports = new Reports();

        using var from = new MemoryStream(data);
        using var to = new MemoryStream();
        ProgressCopy.CopyStream(from, to, data.Length, reports);

        Assert.Equal(data, to.ToArray());
        Assert.True(reports.Values.Count >= 5, "a report for about every megabyte, not only at the end: " + reports.Values.Count);
        Assert.Equal(reports.Values.Order().ToList(), reports.Values); // it only goes forward
        Assert.Equal(1.0, reports.Values[^1]);
        Assert.All(reports.Values.SkipLast(1), value => Assert.InRange(value, 0.0, 1.0));
    }

    [Fact]
    public void ACopyOfNothing_IsDone()
    {
        var reports = new Reports();

        ProgressCopy.CopyStream(new MemoryStream(), new MemoryStream(), 0, reports);

        Assert.Equal([1.0], reports.Values);
    }

    [Fact]
    public void ACopyOfAFile_ReplacesWhatWasThere_AndWorksWithNobodyLooking()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PLProgressCopy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "from.bin");
            var target = Path.Combine(folder, "to.bin");
            File.WriteAllBytes(source, [1, 2, 3, 4, 5]);
            File.WriteAllBytes(target, [9, 9, 9, 9, 9, 9, 9, 9]);

            ProgressCopy.CopyFile(source, target, overwrite: true, progress: null);

            Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(target));
            Assert.Throws<IOException>(() => ProgressCopy.CopyFile(source, target, overwrite: false, progress: null)); // as File.Copy refuses
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
