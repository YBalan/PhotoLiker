using PhotoLiker.Search;

namespace PhotoLikerUI
{
    using System.Drawing.Imaging;
    using System.Runtime.InteropServices;

    public static partial class ImageHelper
    {
        /// <summary>
        /// A photo that was decoded to pixels (see <see cref="DecodedPhoto"/>) as a bitmap in the format the screen draws fastest, to be shown in the viewer. The rows are made side by side on all the
        /// cores. Safe to call from any thread; the bitmap belongs to the caller.
        /// </summary>
        internal static Bitmap BitmapOf(DecodedPhoto photo)
        {
            var width = photo.Width;
            var height = photo.Height;
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            try
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var origin = data.Scan0;
                    var stride = data.Stride;
                    Parallel.For(0, height, () => new byte[width * 4], (y, _, row) =>
                    {
                        var from = y * width * 3;
                        for (int x = 0, i = from, o = 0; x < width; x++, i += 3, o += 4)
                        {
                            row[o] = photo.Bgr[i];
                            row[o + 1] = photo.Bgr[i + 1];
                            row[o + 2] = photo.Bgr[i + 2];
                            row[o + 3] = 255;
                        }
                        Marshal.Copy(row, 0, origin + y * stride, width * 4);
                        return row;
                    }, _ => { });
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }
}
