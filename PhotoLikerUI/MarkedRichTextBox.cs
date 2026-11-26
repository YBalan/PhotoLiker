using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// A Rich Text Box that can show where its spaces, its tabs and the ends of its lines and paragraphs are, the way the ¶ button of a word processor does: a dot in each space, an arrow in each
    /// tab, a ¶ at the end of each paragraph and a bent arrow at a line break. The Rich Text Box has no such mode, so the marks are drawn over its own painting, only on the part of it that was
    /// just painted (what scrolls or is typed is painted again, with its marks; what stays is not drawn twice).
    /// </summary>
    internal sealed class MarkedRichTextBox : RichTextBox
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_PRINTCLIENT = 0x0318;

        /// <summary>The most marks drawn in one painting (a long text of spaces would take long to paint, and what is on the screen is much less).</summary>
        private const int MaxMarks = 5000;

        /// <summary>The colour of the marks: a blue-grey that is seen on the white paper and does not take the eye from the text.</summary>
        internal static readonly Color MarkColor = Color.FromArgb(78, 120, 196);

        /// <summary>The space that does not break a line (it is a number here: it cannot be told from a space in the code).</summary>
        private const char NoBreakSpace = (char)0x00A0;

        private bool _showMarks;

        /// <summary>Whether the marks are drawn.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowMarks
        {
            get => _showMarks;
            set
            {
                if (_showMarks == value)
                    return;

                _showMarks = value;
                if (IsHandleCreated)
                    Invalidate();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetUpdateRect(IntPtr window, out NativeRect rect, bool erase);

        protected override void WndProc(ref Message m)
        {
            if (!_showMarks || !IsHandleCreated || (m.Msg != WM_PAINT && m.Msg != WM_PRINTCLIENT))
            {
                base.WndProc(ref m);
                return;
            }

            if (m.Msg == WM_PAINT)
            {
                // What is to be painted, asked before it is (the painting makes it valid)
                var painted = GetUpdateRect(Handle, out var rect, false) ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : Rectangle.Empty;
                base.WndProc(ref m);
                Draw(Graphics.FromHwnd(Handle), painted);
            }
            else
            {
                // A copy of the window (printed to a picture): the device is the one it is given
                base.WndProc(ref m);
                Draw(Graphics.FromHdc(m.WParam), ClientRectangle);
            }
        }

        private void Draw(Graphics graphics, Rectangle clip)
        {
            using (graphics)
            {
                try
                {
                    clip.Intersect(ClientRectangle);
                    if (clip.Width > 0 && clip.Height > 0)
                        DrawMarks(graphics, clip);
                }
                catch (Exception ex) when (ex is ExternalException or ObjectDisposedException or InvalidOperationException or ArgumentException)
                {
                    // A window that is closing while it is painted has nothing to show the marks on
                }
            }
        }

        private void DrawMarks(Graphics graphics, Rectangle clip)
        {
            var text = Text;
            var length = text.Length;

            // From the line at the top of what was painted to the line at its bottom (and the end of the text when that is where it reaches)
            var first = FirstOfLine(GetCharIndexFromPosition(new Point(1, clip.Top)));
            var last = Math.Min(length, GetCharIndexFromPosition(new Point(Math.Max(1, ClientSize.Width - 2), clip.Bottom - 1)) + 1);
            if (last < first)
                return;

            graphics.SetClip(clip);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var brush = new SolidBrush(MarkColor);
            using var pen = new Pen(MarkColor, 1.25f);

            var lineTop = int.MinValue;
            var lineHeight = Font.Height;
            var pictureOnLine = false; // a picture makes its line tall, and what is written on it stands on its bottom
            Font? glyphs = null;
            var glyphEm = 0f;
            var position = GetPositionFromCharIndex(first);
            var drawn = 0;
            try
            {
                for (var i = first; i <= last && drawn < MaxMarks; i++)
                {
                    var next = i < length ? GetPositionFromCharIndex(i + 1) : position;
                    var c = i < length ? text[i] : '\n'; // after the last character is the end of the last paragraph

                    if (position.Y != lineTop)
                    {
                        lineTop = position.Y;
                        lineHeight = HeightOfLineAt(i, position.Y);
                        pictureOnLine = false;
                    }

                    if (position.Y > clip.Bottom || position.Y + lineHeight < clip.Top || (c is not (' ' or NoBreakSpace or '\t' or '\n' or '\v')))
                    {
                        position = next;
                        continue;
                    }

                    // The marks are the size of the text of a line at most the size of the usual text, and stand where its letters do: the baseline is the same for all the text of a line
                    var em = Math.Min(lineHeight * 0.75f, Font.Height * 0.75f);
                    var baseline = position.Y + (pictureOnLine ? lineHeight - Font.Height * 0.19f : lineHeight * 0.81f);
                    var middle = baseline - em * 0.25f; // the height of the middle of the small letters
                    var sameLine = next.Y == position.Y;

                    switch (c)
                    {
                        case ' ' or NoBreakSpace:
                        {
                            var width = sameLine ? next.X - position.X : em * 0.3f;
                            if (width > em * 1.4f)
                            {
                                pictureOnLine = true; // a picture in the text is a blank that wide: it is not a space
                                break;
                            }

                            var x = position.X + width / 2f;
                            if (c == ' ')
                            {
                                var dot = Math.Max(3f, em * 0.2f);
                                graphics.FillEllipse(brush, x - dot / 2, middle - dot / 2, dot, dot);
                            }
                            else
                            {
                                var ring = Math.Max(3.5f, em * 0.22f);
                                graphics.DrawEllipse(pen, x - ring / 2, middle - ring / 2 - em * 0.2f, ring, ring); // the degree sign of a word processor's non-breaking space
                            }

                            drawn++;
                            break;
                        }

                        case '\t':
                        {
                            var from = position.X + 2f;
                            var to = (sameLine ? next.X : position.X + em * 1.5f) - 2f;
                            if (to - from < 3f)
                                break;

                            var head = Math.Clamp(em * 0.22f, 2.5f, 5f);
                            graphics.DrawLine(pen, from, middle, to, middle);
                            graphics.DrawLine(pen, to, middle, to - head, middle - head);
                            graphics.DrawLine(pen, to, middle, to - head, middle + head);
                            drawn++;
                            break;
                        }

                        default:
                        {
                            // The end of a paragraph (¶) or a line break inside it (the arrow bent left)
                            if (glyphs is null || Math.Abs(glyphEm - em) > 0.4f)
                            {
                                glyphs?.Dispose();
                                glyphs = new Font("Segoe UI", em, FontStyle.Regular, GraphicsUnit.Pixel);
                                glyphEm = em;
                            }

                            // (text is drawn from its top: the top of a line of this size, so that its baseline is where the baseline of the text is)
                            var top = baseline - glyphs.Height * 0.81f;
                            graphics.DrawString(c == '\v' ? "↵" : "¶", glyphs, brush, position.X + 1f, top, StringFormat.GenericTypographic);
                            drawn++;
                            break;
                        }
                    }

                    position = next;
                }
            }
            finally
            {
                glyphs?.Dispose();
            }
        }

        /// <summary>The first character of the line (as it is wrapped on the screen) the character is in.</summary>
        private int FirstOfLine(int index)
        {
            var start = GetFirstCharIndexFromLine(GetLineFromCharIndex(Math.Max(0, index)));
            return start < 0 ? 0 : start;
        }

        /// <summary>How tall the line of a character is: from its top to the top of the next line (the usual line of the text for the last, which has none under it).</summary>
        private int HeightOfLineAt(int index, int top)
        {
            var nextLine = GetFirstCharIndexFromLine(GetLineFromCharIndex(index) + 1);
            var height = nextLine > index ? GetPositionFromCharIndex(nextLine).Y - top : Font.Height;
            return Math.Clamp(height, 6, 600);
        }
    }
}
