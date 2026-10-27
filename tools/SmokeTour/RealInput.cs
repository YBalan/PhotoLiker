using System.Runtime.InteropServices;

namespace SmokeTour;

/// <summary>
/// Mouse and focus the way the operating system has them. <see cref="DrivenForm.Press"/> hands a key straight to the form, which
/// is why it can't tell which control has the keyboard focus; this can, because a real click moves the focus the way a person's does.
/// </summary>
internal static class RealInput
{
    private const uint LeftDown = 0x0002, LeftUp = 0x0004;

    /// <summary>Moves the mouse pointer to the point (in screen coordinates) and clicks there, twice for a double click.</summary>
    public static void Click(Point screen, int times = 1)
    {
        SetCursorPos(screen.X, screen.Y);
        for (var i = 0; i < times; i++)
        {
            mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
        }
    }

    /// <summary>The control that has the keyboard focus in this thread's window, if it is one of ours.</summary>
    public static Control? Focused() => Control.FromHandle(GetFocus());

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
}
