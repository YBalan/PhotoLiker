using PhotoLiker.Core;

namespace PhotoLiker.Maui.Services;

/// <summary>
/// Which photos the viewer keeps close at hand: the strip of thumbnails around the shown photo, and the same
/// photos as the loading order of the cache (the shown photo first, then its neighbours, nearest first).
/// Plain computation, shared by every platform.
/// </summary>
public static class ViewerWindowPlanner
{
    /// <summary>The first and the last index of the thumbnail strip: the shown photo with up to <paramref name="siblingCount"/> photos on either side. (0, -1) for an empty list.</summary>
    public static (int From, int To) StripRange(int count, int currentIndex, int siblingCount)
    {
        if (count <= 0)
            return (0, -1);

        var current = Math.Clamp(currentIndex, 0, count - 1);
        return (Math.Max(0, current - siblingCount), Math.Min(count - 1, current + siblingCount));
    }

    /// <summary>
    /// The paths of the strip in the order the cache should load them: the shown photo first, then alternating the next
    /// and the previous neighbour, moving outwards. Empty for an empty list.
    /// </summary>
    public static IReadOnlyList<string> NearestFirst(IReadOnlyList<PhotoFile> files, int currentIndex, int siblingCount)
    {
        if (files.Count == 0)
            return [];

        var current = Math.Clamp(currentIndex, 0, files.Count - 1);
        var (from, to) = StripRange(files.Count, current, siblingCount);

        var order = new List<string>(to - from + 1) { files[current].OriginalFilePath };
        for (var step = 1; step <= siblingCount; step++)
        {
            if (current + step <= to)
                order.Add(files[current + step].OriginalFilePath);
            if (current - step >= from)
                order.Add(files[current - step].OriginalFilePath);
        }

        return order;
    }
}
