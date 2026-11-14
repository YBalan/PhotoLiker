namespace PhotoLiker.Maui.Services;

/// <summary>
/// The keyboard of the viewer, free of any platform's key codes: each platform maps its own keys onto these
/// (Windows does today; other platforms can later), and the smoke tour presses them directly.
/// </summary>
public enum ViewerKey
{
    /// <summary>Left arrow: the previous photo.</summary>
    Previous,

    /// <summary>Right arrow: the next photo.</summary>
    Next,

    /// <summary>Home: the first photo of the list.</summary>
    First,

    /// <summary>End: the last photo of the list.</summary>
    Last,

    /// <summary>Space: like / unlike the shown photo.</summary>
    ToggleLike,

    /// <summary>PageUp: the first photo of the previous month.</summary>
    PreviousMonth,

    /// <summary>PageDown: the first photo of the next month.</summary>
    NextMonth,
}
