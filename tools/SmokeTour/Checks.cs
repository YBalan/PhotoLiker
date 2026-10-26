namespace SmokeTour;

/// <summary>Collects the outcome of the tour: every check is printed, the failures decide the exit code.</summary>
internal sealed class Checks
{
    private readonly List<string> _failures = [];

    public void Step(string title) => Console.WriteLine($"\n{title}");

    public void That(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        if (!condition)
            _failures.Add(what);
    }

    public void Fail(string what)
    {
        Console.WriteLine($"  [FAIL] {what}");
        _failures.Add(what);
    }

    public void Note(string text) => Console.WriteLine($"         {text}");

    public int Summary()
    {
        Console.WriteLine();
        if (_failures.Count == 0)
        {
            Console.WriteLine("ALL CHECKS PASSED");
            return 0;
        }

        Console.WriteLine($"{_failures.Count} CHECK(S) FAILED:");
        _failures.ForEach(f => Console.WriteLine("  - " + f));
        return 1;
    }
}
