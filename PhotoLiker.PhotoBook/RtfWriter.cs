using System.Globalization;
using System.Text;

namespace PhotoLiker.PhotoBook
{
    /// <summary>
    /// Writes a <see cref="RichDocument"/> as the RTF a Rich Text Box shows and <see cref="RtfParser"/> reads back: one font, the look of each run (bold, italic, underline, strike-through, size,
    /// colour), how each paragraph is aligned, indented and tabbed, and the pictures that are PNG or JPEG. It is how a text is made (a scan of a manuscript), and how pictures are put into a text
    /// or taken out of it, without the Rich Text Box.
    /// </summary>
    public static class RtfWriter
    {
        public static string Write(RichDocument document, string fontFamily = "Segoe UI")
        {
            var colours = document.Paragraphs.SelectMany(paragraph => paragraph.Runs).Select(run => run.Style.Color).OfType<int>().Distinct().ToList();

            var rtf = new StringBuilder();
            rtf.Append(@"{\rtf1\ansi\ansicpg1252\uc1\deff0{\fonttbl{\f0\fnil\fcharset0 ").Append(fontFamily.Replace(';', ' ')).Append(";}}");
            rtf.Append(@"{\colortbl ;");
            foreach (var colour in colours)
                rtf.Append(CultureInfo.InvariantCulture, $@"\red{(colour >> 16) & 0xFF}\green{(colour >> 8) & 0xFF}\blue{colour & 0xFF};");
            rtf.Append('}');
            rtf.Append(@"\viewkind4").Append('\n');

            foreach (var paragraph in document.Paragraphs)
                WriteParagraph(rtf, paragraph, colours);

            rtf.Append('}');
            return rtf.ToString();
        }

        private static void WriteParagraph(StringBuilder rtf, RichParagraph paragraph, List<int> colours)
        {
            rtf.Append(@"\pard").Append(paragraph.Align switch
            {
                TextAlign.Center => @"\qc",
                TextAlign.Right => @"\qr",
                TextAlign.Justify => @"\qj",
                _ => @"\ql",
            });

            if (paragraph.LeftIndent > 0)
                rtf.Append(@"\li").Append(Twips(paragraph.LeftIndent));
            if (paragraph.FirstLineIndent != 0)
                rtf.Append(@"\fi").Append(Twips(paragraph.FirstLineIndent));
            foreach (var stop in paragraph.TabStops)
                rtf.Append(@"\tx").Append(Twips(stop));
            rtf.Append(' ');

            if (paragraph.Picture is { } picture)
            {
                WritePicture(rtf, picture);
            }
            else
            {
                foreach (var run in paragraph.Runs)
                    WriteRun(rtf, run, colours);
            }

            rtf.Append(@"\par").Append('\n');
        }

        private static string Twips(double points) => ((int)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);

        private static void WriteRun(StringBuilder rtf, RichRun run, List<int> colours)
        {
            var style = run.Style;
            rtf.Append(@"{\plain\f0\fs").Append((int)Math.Round(style.PointSize * 2));
            if (style.Bold)
                rtf.Append(@"\b");
            if (style.Italic)
                rtf.Append(@"\i");
            if (style.Underline)
                rtf.Append(@"\ul");
            if (style.Strike)
                rtf.Append(@"\strike");
            if (style.Color is { } colour)
                rtf.Append(@"\cf").Append(colours.IndexOf(colour) + 1);
            rtf.Append(' ');

            foreach (var c in run.Text)
            {
                switch (c)
                {
                    case '\\' or '{' or '}':
                        rtf.Append('\\').Append(c);
                        break;
                    case '\t':
                        rtf.Append(@"\tab ");
                        break;
                    case '\n':
                        rtf.Append(@"\line ");
                        break;
                    case < ' ':
                        break; // no other control character is text
                    case <= '~':
                        rtf.Append(c);
                        break;
                    default:
                        rtf.Append(@"\u").Append(((short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
                        break;
                }
            }

            rtf.Append('}');
        }

        /// <summary>A picture as the Rich Text Box writes one: its size twice (in hundredths of a millimetre, and as the width and height it is shown at in twips), then its bytes as hex digits.</summary>
        private static void WritePicture(StringBuilder rtf, RichPicture picture)
        {
            var width = (int)Math.Round(picture.WidthShare * RichDocument.ReferenceWidth * 20);
            var height = Math.Max(1, (int)Math.Round(width * picture.Ratio));
            rtf.Append(@"{\pict").Append(picture.IsPng ? @"\pngblip" : @"\jpegblip");
            rtf.Append(CultureInfo.InvariantCulture, $@"\picw{(int)Math.Round(width * 2540.0 / 1440)}\pich{(int)Math.Round(height * 2540.0 / 1440)}\picwgoal{width}\pichgoal{height} ");
            rtf.Append(Convert.ToHexString(picture.Data).ToLowerInvariant());
            rtf.Append('}');
        }
    }
}
