using Microsoft.Maui.Graphics.Platform;

namespace PhotoLiker.Maui.Services;

/// <summary>
/// Downsizes a photo to thumbnail bytes with the platform's image codec (Win2D on Windows, the native codecs elsewhere),
/// on a pool thread. Null when the photo is already small, or cannot be decoded: the full bytes serve as the thumbnail then.
/// </summary>
public static class ThumbnailResizer
{
    public static Task<byte[]?> ResizeAsync(byte[] fullBytes, int size, CancellationToken cancellationToken) =>
        Task.Run<byte[]?>(() =>
        {
            try
            {
                using var input = new MemoryStream(fullBytes, writable: false);
                using var image = PlatformImage.FromStream(input);
                if (image.Width <= size && image.Height <= size)
                    return null;

                using var small = image.Downsize(size, disposeOriginal: false);
                using var output = new MemoryStream();
                small.Save(output, ImageFormat.Jpeg, quality: 0.85f);
                return output.ToArray();
            }
            catch
            {
                return null;
            }
        }, cancellationToken);
}
