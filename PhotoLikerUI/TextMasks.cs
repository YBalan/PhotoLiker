using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using PhotoLiker.Drawing;

namespace PhotoLikerUI
{
    /// <summary>Draws a note of text with the fonts of Windows and gives how much of each pixel the letters cover, which is what <see cref="TextStep"/> takes (the drawing library has no fonts).</summary>
    internal static class TextMasks
    {
        /// <summary>The room round the letters, so that the soft edge of the first and the last is not cut off.</summary>
        public const int Margin = 2;

        /// <summary>The longest side a note may have (a note this large is a mistake, and its mask would be all the memory there is).</summary>
        private const int MaxSide = 16000;

        /// <summary>The name of the font notes are written in.</summary>
        public const string FontName = "Segoe UI";

        /// <summary>The note as a mask, with <see cref="Margin"/> pixels round it; null for a note with nothing in it. The text may have several lines; a line is not wrapped.</summary>
        public static TextMask? Render(string text, float sizePixels, bool bold = false)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            using var font = new Font(FontName, Math.Max(1f, sizePixels), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };

            SizeF size;
            using (var probe = new Bitmap(1, 1))
            using (var graphics = Graphics.FromImage(probe))
                size = graphics.MeasureString(text, font, PointF.Empty, format);

            var width = Math.Min(MaxSide, (int)Math.Ceiling(size.Width) + 2 * Margin);
            var height = Math.Min(MaxSide, (int)Math.Ceiling(size.Height) + 2 * Margin);
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // grey edges, not the coloured ones of ClearType: the mask is put on any colour
                graphics.DrawString(text, font, Brushes.White, new PointF(Margin, Margin), format);
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                var coverage = new byte[width * height];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                        coverage[y * width + x] = bytes[y * data.Stride + x * 4 + 2]; // the red of white on black: how much of the pixel is letter
                }
                return new TextMask(width, height, coverage);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
