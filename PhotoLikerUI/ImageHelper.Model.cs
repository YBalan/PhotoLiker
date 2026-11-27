using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PhotoLiker.VectorFiles;

namespace PhotoLikerUI
{
    public static partial class ImageHelper
    {
        /// <summary>The size of the picture a model is first shown as, until the viewer draws it at its own size, and the size the thumbnail is made from.</summary>
        private const int ModelPreviewWidth = 800;
        private const int ModelPreviewHeight = 600;

        /// <summary>The colours of the models: a dark stage with a light blue-grey part on it, whatever the theme of the window (a viewport, like the player of a video).</summary>
        internal static readonly RenderStyle ModelStyle = RenderStyle.ForTheme(dark: true);

        /// <summary>
        /// What the viewer shows for a model: its picture from the front right above (kept in the entry in place of a photo), the model itself (which the viewer turns with the mouse and
        /// draws again at its own size), and the facts about it in place of the EXIF rows. Safe to call from any thread; it reads the whole file, so it belongs on a background thread,
        /// which is where the photo cache calls it.
        /// </summary>
        internal static PhotoEntry LoadModel(string path, int thumbSize)
        {
            var mesh = StlReader.Read(path);
            var full = ToBitmap(MeshRenderer.Render(mesh, MeshView.Isometric, ModelPreviewWidth, ModelPreviewHeight, ModelStyle with { Supersample = 2 }));
            try
            {
                var thumb = CreateThumbnail(full, thumbSize);
                return new PhotoEntry(full, thumb, thumbSize, ModelMetadata.Entries(mesh, new FileInfo(path)), backingStream: null) { Model = mesh };
            }
            catch
            {
                full.Dispose();
                throw;
            }
        }

        /// <summary>The thumbnail of a model alone (see <see cref="LoadModel"/>).</summary>
        internal static Bitmap LoadModelThumbnail(string path, int thumbSize)
        {
            var mesh = StlReader.Read(path);
            var height = Math.Max(1, thumbSize * ModelPreviewHeight / ModelPreviewWidth);
            using var picture = ToBitmap(MeshRenderer.Render(mesh, MeshView.Isometric, thumbSize, height, ModelStyle with { Supersample = 3 }));
            return CreateThumbnail(picture, thumbSize);
        }

        /// <summary>The pixels of a finished picture as a bitmap of its own (the format the screen draws fastest; every pixel is opaque).</summary>
        internal static Bitmap ToBitmap(RenderedPicture picture)
        {
            var bitmap = new Bitmap(picture.Width, picture.Height, PixelFormat.Format32bppPArgb);
            try
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, picture.Width, picture.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var rowBytes = picture.Width * 4;
                    for (var y = 0; y < picture.Height; y++)
                        Marshal.Copy(picture.Bgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
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
