namespace PhotoLiker.Maui.SmokeTour;

/// <summary>Collects the outcome of the tour, like the WinForms tour's Checks: every check becomes a line of the log, the failures decide the exit code.</summary>
public sealed class SmokeChecks
{
    private readonly List<string> _lines = [];
    private readonly List<string> _failures = [];

    public int Failures => _failures.Count;

    public void Step(string title) => _lines.Add($"{Environment.NewLine}{title}");

    public void That(bool condition, string what)
    {
        _lines.Add($"  [{(condition ? "ok" : "FAIL")}] {what}");
        if (!condition)
            _failures.Add(what);
    }

    public void Fail(string what)
    {
        _lines.Add($"  [FAIL] {what}");
        _failures.Add(what);
    }

    public void Note(string text) => _lines.Add($"         {text}");

    /// <summary>The whole log, ending with the summary line.</summary>
    public string Render()
    {
        var summary = _failures.Count == 0
            ? "ALL CHECKS PASSED"
            : $"{_failures.Count} CHECK(S) FAILED:{Environment.NewLine}" + string.Join(Environment.NewLine, _failures.Select(f => "  - " + f));
        return string.Join(Environment.NewLine, _lines) + Environment.NewLine + Environment.NewLine + summary + Environment.NewLine;
    }
}
