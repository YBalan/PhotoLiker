using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SmokeTour;

/// <summary>Takes pictures of windows as they are on screen (so the real rendering, dark theme included).</summary>
internal static class ScreenCapture
{
    private const int DwmExtendedFrameBounds = 9;
    private const uint WmClose = 0x0010;

    /// <summary>The window as it looks, without the invisible resize border Windows 10/11 put around it.</summary>
    public static Rectangle FrameBounds(IntPtr window)
    {
        if (DwmGetWindowAttribute(window, DwmExtendedFrameBounds, out var rect, Marshal.SizeOf<Rect>()) != 0)
            GetWindowRect(window, out rect);
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public static Bitmap Capture(IntPtr window) => Capture(FrameBounds(window));

    public static Bitmap Capture(Rectangle screenBounds)
    {
        var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
        return bitmap;
    }

    /// <summary>Mean brightness (0 black .. 255 white) of an area, sampled, to tell a dark picture from a light one.</summary>
    public static double Brightness(Bitmap bitmap, Rectangle area)
    {
        area.Intersect(new Rectangle(Point.Empty, bitmap.Size));
        double sum = 0;
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y += 3)
        {
            for (var x = area.Left; x < area.Right; x += 3)
            {
                var c = bitmap.GetPixel(x, y);
                sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                count++;
            }
        }
        return count == 0 ? 0 : sum / count;
    }

    /// <summary>Hides an area behind coarse blocks of its average colour.</summary>
    public static void Pixelate(Bitmap bitmap, Rectangle area, int block)
    {
        area.Intersect(new Rectangle(Point.Empty, bitmap.Size));
        using var graphics = Graphics.FromImage(bitmap);
        for (var y = area.Top; y < area.Bottom; y += block)
        {
            for (var x = area.Left; x < area.Right; x += block)
            {
                var cell = Rectangle.Intersect(new Rectangle(x, y, block, block), area);
                double r = 0, g = 0, b = 0;
                var count = 0;
                for (var py = cell.Top; py < cell.Bottom; py += 2)
                {
                    for (var px = cell.Left; px < cell.Right; px += 2)
                    {
                        var c = bitmap.GetPixel(px, py);
                        r += c.R; g += c.G; b += c.B;
                        count++;
                    }
                }

                using var brush = new SolidBrush(Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count)));
                graphics.FillRectangle(brush, cell);
            }
        }
    }

    /// <summary>The biggest visible window of this thread that is not one of <paramref name="except"/> (a dialog being shown), or zero.</summary>
    public static IntPtr FindDialogOfThisThread(params IntPtr[] except)
    {
        var found = IntPtr.Zero;
        long biggest = 0;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (!except.Contains(window) && IsWindowVisible(window))
            {
                var bounds = FrameBounds(window);
                var area = (long)bounds.Width * bounds.Height;
                if (area > biggest)
                {
                    biggest = area;
                    found = window;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void Close(IntPtr window) => PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// Makes the window the active one. Windows doesn't let a program started from a console take the foreground, and an
    /// inactive window is drawn differently (no focus, no "..." button on the selected grid row), so borrow the input
    /// queue of the current foreground window for the moment it takes.
    /// </summary>
    public static bool BringToFront(IntPtr window)
    {
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var thisThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            return SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
                AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumThreadWindowsProc(IntPtr window, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint threadId, uint attachTo, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
