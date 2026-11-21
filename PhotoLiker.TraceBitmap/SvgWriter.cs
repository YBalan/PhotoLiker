using System.Globalization;
using System.Text;

namespace PhotoLiker.TraceBitmap
{
    /// <summary>
    /// Writes a trace as an SVG file whose size is given in millimetres: the width and the height say so (<c>100mm</c>) and the coordinates inside are millimetres too (the view box is as many
    /// units as there are millimetres), so a program that reads the file as it is - Inkscape, a laser or plotter program, a CAD program - gets the size that was asked for. Each layer is one path,
    /// filled with the layer's colour by the even-odd rule, so the holes are holes.
    /// </summary>
    public static class SvgWriter
    {
        /// <param name="trace">What was traced.</param>
        /// <param name="widthMillimetres">The width of the drawing; the traced picture is stretched to it.</param>
        /// <param name="heightMillimetres">The height of the drawing.</param>
        public static string Write(TraceResult trace, double widthMillimetres, double heightMillimetres)
        {
            ArgumentNullException.ThrowIfNull(trace);
            Sizes.Check(widthMillimetres, heightMillimetres);

            var scaleX = widthMillimetres / trace.Width;
            var scaleY = heightMillimetres / trace.Height;
            var text = new StringBuilder();
            text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            text.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(Number(widthMillimetres)).Append("mm\" height=\"").Append(Number(heightMillimetres))
                .Append("mm\" viewBox=\"0 0 ").Append(Number(widthMillimetres)).Append(' ').Append(Number(heightMillimetres)).Append("\">\n");

            var layerNumber = 0;
            foreach (var layer in trace.Layers)
            {
                if (layer.Paths.Count == 0)
                    continue;

                layerNumber++;
                text.Append("  <path id=\"trace").Append(layerNumber.ToString(CultureInfo.InvariantCulture)).Append("\" fill=\"").Append(layer.Color.Hex)
                    .Append("\" fill-rule=\"evenodd\" stroke=\"none\" d=\"");
                foreach (var path in layer.Paths)
                    AppendPath(text, path, scaleX, scaleY);
                text.Append("\"/>\n");
            }

            text.Append("</svg>\n");
            return text.ToString();
        }

        private static void AppendPath(StringBuilder text, TracePath path, double scaleX, double scaleY)
        {
            text.Append('M').Append(Number(path.Start.X * scaleX)).Append(' ').Append(Number(path.Start.Y * scaleY));
            var command = 'M';
            foreach (var segment in path.Segments)
            {
                var wanted = segment.IsCurve ? 'C' : 'L';
                if (wanted != command)
                {
                    text.Append(wanted);
                    command = wanted;
                }
                else
                {
                    text.Append(' ');
                }

                if (segment.IsCurve)
                {
                    text.Append(Number(segment.Control1.X * scaleX)).Append(' ').Append(Number(segment.Control1.Y * scaleY)).Append(' ')
                        .Append(Number(segment.Control2.X * scaleX)).Append(' ').Append(Number(segment.Control2.Y * scaleY)).Append(' ');
                }

                text.Append(Number(segment.End.X * scaleX)).Append(' ').Append(Number(segment.End.Y * scaleY));
            }

            text.Append('Z');
        }

        /// <summary>A number the way SVG wants it: a point for the decimals whatever the language of the computer is, three decimals (a thousandth of a millimetre) and no zeros at the end.</summary>
        internal static string Number(double value)
        {
            var text = Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
            return text is "-0" ? "0" : text;
        }
    }

    internal static class Sizes
    {
        public static void Check(double widthMillimetres, double heightMillimetres)
        {
            if (!double.IsFinite(widthMillimetres) || widthMillimetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(widthMillimetres), widthMillimetres, "The width has to be more than nothing.");
            if (!double.IsFinite(heightMillimetres) || heightMillimetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(heightMillimetres), heightMillimetres, "The height has to be more than nothing.");
        }
    }
}
