using PhotoLikerUI;

namespace PhotoLiker.Tests;

/// <summary>The rows of the key list of the About box: wrapped to the room there is, in two columns.</summary>
public class ShortcutLinesTests
{
    private static (string Input, string Action)[] List(params (string, string)[] shortcuts) => shortcuts;

    [Fact]
    public void ShortRows_AreTheKeyColumnAndTheText_AsTheyWere()
    {
        var rows = ShortcutLines.Layout(List(("Space", "Like"), ("Home / End", "First / last")), 100);

        // The column is as wide as the longest key (10) and the gap (2)
        Assert.Equal(["Space       Like", "Home / End  First / last"], rows);
    }

    [Fact]
    public void ALongText_GoesOnToTheNextLine_UnderItself_NotUnderTheKeys()
    {
        var rows = ShortcutLines.Layout(List(("Alt+Enter", "one two three four five six seven eight nine ten")), 40);

        // column = 9 + 2 = 11, room = 29
        Assert.Equal(
        [
            "Alt+Enter  one two three four five six",
            "           seven eight nine ten",
        ], rows);
        Assert.All(rows, row => Assert.True(row.Length <= 40));
    }

    [Fact]
    public void ALongListOfKeys_IsWrappedInItsColumn_AndDoesNotWidenTheColumnForTheOthers()
    {
        var rows = ShortcutLines.Layout(
            List(("F1", "This box"), ("Shift+Right / Left, Ctrl+Right / Left, Ctrl+Up / Down", "While a video plays: 5 seconds, 30 seconds, the volume")), 100);

        // Not 55 characters wide (the longest list of keys): at most 24 and the gap
        var column = ShortcutLines.MaxKeyChars + ShortcutLines.ColumnGap;
        Assert.StartsWith("F1".PadRight(column) + "This box", rows[0]);
        Assert.Equal("Shift+Right / Left,".PadRight(column) + "While a video plays: 5 seconds, 30 seconds, the volume", rows[1]);
        Assert.Equal("Ctrl+Right / Left,", rows[2]);
        Assert.Equal("Ctrl+Up / Down", rows[3]);
    }

    [Fact]
    public void KeysThatNeedMoreLinesThanTheText_HaveTheirRestUnderThem_WithNothingBesideIt()
    {
        // 32 characters of keys in a column of 24: two lines, and the text of one
        var rows = ShortcutLines.Layout(List(("aaaaaaaaaa bbbbbbbbbb cccccccccc", "Text")), 100);

        Assert.Equal(2, rows.Length);
        Assert.EndsWith("Text", rows[0]);
        Assert.Equal("cccccccccc", rows[1]); // no padding after it: there is no text on this line
    }

    [Fact]
    public void NoRow_IsLongerThanTheRoom_WhateverTheTextIs_AndNoWordIsLost()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 120));

        var rows = ShortcutLines.Layout(List(("Alt+Enter", text), ("Esc", "Back")), 60);

        Assert.All(rows, row => Assert.True(row.Length <= 60, $"'{row}' is {row.Length} long"));
        Assert.Equal(120, string.Join(' ', rows).Split("word").Length - 1);
        Assert.EndsWith("Back", rows[^1]);
    }

    [Fact]
    public void AWordLongerThanTheLine_IsCut_AndNothingIsLost()
    {
        var lines = ShortcutLines.Wrap("abcdefghijklmnopqrstuvwxyz", 10);

        Assert.Equal(["abcdefghij", "klmnopqrst", "uvwxyz"], lines);
    }

    [Fact]
    public void WrappedLines_JoinedBySpaces_GiveTheTextBack()
    {
        var text = "Slideshow on the whole screen (any key but Esc pauses it and Space continues; F / N: the faces / the names of the people on or off)";

        var lines = ShortcutLines.Wrap(text, 30);

        Assert.Equal(text, string.Join(' ', lines));
        Assert.All(lines, line => Assert.True(line.Length <= 30));
    }

    [Fact]
    public void TheRoom_NeverGoesBelowTheNarrowestThatIsReadable()
    {
        var rows = ShortcutLines.Layout(List(("Alt+Enter", "a text that is long enough to need more than one line when it is narrow")), 5);

        // 5 characters of room would be a column of single words: the text keeps at least MinActionChars
        Assert.All(rows, row => Assert.True(row.Length <= 9 + ShortcutLines.ColumnGap + ShortcutLines.MinActionChars));
        Assert.True(rows.Length < 6);
    }

    [Fact]
    public void NoShortcuts_NoRows() => Assert.Empty(ShortcutLines.Layout([], 80));

    [Fact]
    public void AnEmptyText_StillMakesOneLine() => Assert.Equal([string.Empty], ShortcutLines.Wrap(string.Empty, 10));

    [Fact]
    public void TheWidestRow_IsTheLongestOne()
    {
        Assert.Equal(7, ShortcutLines.WidestRow(["ab", "abcdefg", "abc"]));
        Assert.Equal(0, ShortcutLines.WidestRow([]));
    }
}
