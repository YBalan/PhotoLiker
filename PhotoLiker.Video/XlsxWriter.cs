using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace PhotoLiker.Video
{
    /// <summary>How the cells of a column look.</summary>
    public enum XlsxFormat
    {
        /// <summary>Text or a number as it is.</summary>
        General,
        /// <summary>Text that is wrapped inside the cell.</summary>
        Wrapped,
        /// <summary>A date and time (the cell holds a <see cref="DateTime"/>).</summary>
        DateTime,
        /// <summary>A length of time as hours:minutes:seconds (the cell holds a <see cref="TimeSpan"/>).</summary>
        Duration,
    }

    /// <param name="Header">The title of the column, in the first row.</param>
    /// <param name="Width">The width in characters of the digit 0 (Excel's own unit).</param>
    public sealed record XlsxColumn(string Header, double Width, XlsxFormat Format = XlsxFormat.General);

    /// <param name="Rows">One array of cells per row, as many as there are columns: a string, a number, a <see cref="DateTime"/> or <see cref="TimeSpan"/> (as the column's format says), or null for an empty cell.</param>
    public sealed record XlsxSheet(string Name, IReadOnlyList<XlsxColumn> Columns, IReadOnlyList<object?[]> Rows);

    /// <summary>
    /// Writes a workbook (.xlsx, Office Open XML) with plain tables: a title row that stays in view and has a filter, then the rows. Nothing but the zip and XML of the
    /// framework is used: a spreadsheet of text and numbers needs no library. Excel, LibreOffice and the browsers' viewers open it.
    /// </summary>
    public static class XlsxWriter
    {
        /// <summary>Excel does not keep more than this in a cell; longer text is cut (with a "…") rather than making the file one it refuses to open.</summary>
        public const int MaxCellCharacters = 32_000;

        private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

        // The cell formats of styles.xml, by their number
        private const int StyleDefault = 0, StyleHeader = 1, StyleWrapped = 2, StyleDateTime = 3, StyleDuration = 4;

        /// <summary>Writes the file; a file that is there is replaced only when the new one is whole.</summary>
        public static void Write(string path, IReadOnlyList<XlsxSheet> sheets)
        {
            var temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    Write(stream, sheets);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); } catch (IOException) { /* a leftover with .tmp at the end is harmless */ }
            }
        }

        public static void Write(Stream stream, IReadOnlyList<XlsxSheet> sheets)
        {
            if (sheets.Count == 0)
                throw new ArgumentException("A workbook needs a sheet.", nameof(sheets));

            var names = SheetNames(sheets);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

            Part(zip, "[Content_Types].xml", w =>
            {
                w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Default(w, "xml", "application/xml");
                Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                for (var i = 1; i <= sheets.Count; i++)
                    Override(w, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                w.WriteEndElement();
            });

            Part(zip, "_rels/.rels", w =>
            {
                w.WriteStartElement("Relationships", PackageRelationships);
                Relationship(w, "rId1", Relationships + "/officeDocument", "xl/workbook.xml");
                w.WriteEndElement();
            });

            Part(zip, "xl/workbook.xml", w =>
            {
                w.WriteStartElement("workbook", Main);
                w.WriteAttributeString("xmlns", "r", null, Relationships);
                w.WriteStartElement("sheets");
                for (var i = 0; i < sheets.Count; i++)
                {
                    w.WriteStartElement("sheet");
                    w.WriteAttributeString("name", names[i]);
                    w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("id", Relationships, $"rId{i + 1}");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndElement();
            });

            Part(zip, "xl/_rels/workbook.xml.rels", w =>
            {
                w.WriteStartElement("Relationships", PackageRelationships);
                for (var i = 1; i <= sheets.Count; i++)
                    Relationship(w, $"rId{i}", Relationships + "/worksheet", $"worksheets/sheet{i}.xml");
                Relationship(w, $"rId{sheets.Count + 1}", Relationships + "/styles", "styles.xml");
                w.WriteEndElement();
            });

            Part(zip, "xl/styles.xml", WriteStyles);

            for (var i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                Part(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, sheet));
            }
        }

        /// <summary>
        /// A name Excel takes for a sheet: at most 31 characters, none of []:*?/\, not empty, no apostrophe at either end, and different from the others (without regard to case).
        /// </summary>
        public static string SheetName(string name)
        {
            var text = new string([.. (name ?? string.Empty).Select(c => "[]:*?/\\".Contains(c) || char.IsControl(c) ? '-' : c)]).Trim().Trim('\'');
            if (text.Length == 0)
                text = "Sheet";
            return text.Length <= 31 ? text : text[..31].TrimEnd().Trim('\'');
        }

        private static List<string> SheetNames(IReadOnlyList<XlsxSheet> sheets)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>(sheets.Count);
            foreach (var sheet in sheets)
            {
                var wanted = SheetName(sheet.Name);
                var name = wanted;
                for (var n = 2; !used.Add(name); n++)
                {
                    var suffix = $" ({n})";
                    name = (wanted.Length + suffix.Length > 31 ? wanted[..(31 - suffix.Length)] : wanted) + suffix;
                }
                names.Add(name);
            }
            return names;
        }

        /// <summary>The name of a column as Excel writes it: A, B, ... Z, AA, AB ... (the number counts from 0).</summary>
        public static string ColumnName(int index)
        {
            var name = string.Empty;
            for (var n = index + 1; n > 0; n = (n - 1) / 26)
                name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        /// <summary>The text as it can go into a cell: what XML cannot hold is left out, and it is cut at <see cref="MaxCellCharacters"/>.</summary>
        public static string CellText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new StringBuilder(Math.Min(text.Length, MaxCellCharacters + 1));
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    if (builder.Length + 2 > MaxCellCharacters)
                        return builder.Append('…').ToString();
                    builder.Append(c).Append(text[++i]);
                }
                else if (XmlConvert.IsXmlChar(c))
                {
                    if (builder.Length + 1 > MaxCellCharacters)
                        return builder.Append('…').ToString();
                    builder.Append(c);
                }
                // a lone surrogate or a control character: nothing XML can carry
            }
            return builder.ToString();
        }

        // ── the parts ──────────────────────────────────────────────────────

        private static void Part(ZipArchive zip, string name, Action<XmlWriter> write)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, Indent = false });
            writer.WriteStartDocument(standalone: true);
            write(writer);
            writer.WriteEndDocument();
        }

        private static void Default(XmlWriter w, string extension, string contentType)
        {
            w.WriteStartElement("Default");
            w.WriteAttributeString("Extension", extension);
            w.WriteAttributeString("ContentType", contentType);
            w.WriteEndElement();
        }

        private static void Override(XmlWriter w, string part, string contentType)
        {
            w.WriteStartElement("Override");
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", contentType);
            w.WriteEndElement();
        }

        private static void Relationship(XmlWriter w, string id, string type, string target)
        {
            w.WriteStartElement("Relationship");
            w.WriteAttributeString("Id", id);
            w.WriteAttributeString("Type", type);
            w.WriteAttributeString("Target", target);
            w.WriteEndElement();
        }

        private static void WriteStyles(XmlWriter w)
        {
            w.WriteStartElement("styleSheet", Main);

            w.WriteStartElement("numFmts");
            w.WriteAttributeString("count", "2");
            NumberFormat(w, 164, "yyyy-mm-dd hh:mm");
            NumberFormat(w, 165, "[h]:mm:ss");
            w.WriteEndElement();

            w.WriteStartElement("fonts");
            w.WriteAttributeString("count", "2");
            Font(w, bold: false);
            Font(w, bold: true);
            w.WriteEndElement();

            w.WriteStartElement("fills");
            w.WriteAttributeString("count", "3");
            w.WriteRaw("<fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>");
            w.WriteRaw("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E2F3\"/><bgColor indexed=\"64\"/></patternFill></fill>");
            w.WriteEndElement();

            w.WriteRaw("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
            w.WriteRaw("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");

            w.WriteStartElement("cellXfs");
            w.WriteAttributeString("count", "5");
            Format(w, numberFormat: 0, font: 0, fill: 0, "<alignment vertical=\"top\"/>");                    // StyleDefault: text and numbers, kept to the top of a tall row
            Format(w, numberFormat: 0, font: 1, fill: 2, "<alignment vertical=\"center\" wrapText=\"1\"/>");   // StyleHeader
            Format(w, numberFormat: 0, font: 0, fill: 0, "<alignment vertical=\"top\" wrapText=\"1\"/>");      // StyleWrapped
            Format(w, numberFormat: 164, font: 0, fill: 0, "<alignment vertical=\"top\" horizontal=\"left\"/>"); // StyleDateTime
            Format(w, numberFormat: 165, font: 0, fill: 0, "<alignment vertical=\"top\" horizontal=\"left\"/>"); // StyleDuration
            w.WriteEndElement();

            w.WriteRaw("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
            w.WriteEndElement();
        }

        private static void NumberFormat(XmlWriter w, int id, string code)
        {
            w.WriteStartElement("numFmt");
            w.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("formatCode", code);
            w.WriteEndElement();
        }

        private static void Font(XmlWriter w, bool bold)
        {
            w.WriteStartElement("font");
            if (bold)
                w.WriteElementString("b", null);
            w.WriteStartElement("sz");
            w.WriteAttributeString("val", "11");
            w.WriteEndElement();
            w.WriteStartElement("name");
            w.WriteAttributeString("val", "Calibri");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void Format(XmlWriter w, int numberFormat, int font, int fill, string alignment)
        {
            w.WriteStartElement("xf");
            w.WriteAttributeString("numFmtId", numberFormat.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", fill.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("borderId", "0");
            w.WriteAttributeString("xfId", "0");
            if (numberFormat != 0)
                w.WriteAttributeString("applyNumberFormat", "1");
            if (font != 0)
                w.WriteAttributeString("applyFont", "1");
            if (fill != 0)
                w.WriteAttributeString("applyFill", "1");
            w.WriteAttributeString("applyAlignment", "1");
            w.WriteRaw(alignment);
            w.WriteEndElement();
        }

        private static void WriteSheet(XmlWriter w, XlsxSheet sheet)
        {
            var columns = sheet.Columns;
            var lastRow = sheet.Rows.Count + 1;

            w.WriteStartElement("worksheet", Main);

            // The title row stays in view while the rows scroll
            w.WriteStartElement("sheetViews");
            w.WriteStartElement("sheetView");
            w.WriteAttributeString("workbookViewId", "0");
            w.WriteStartElement("pane");
            w.WriteAttributeString("ySplit", "1");
            w.WriteAttributeString("topLeftCell", "A2");
            w.WriteAttributeString("activePane", "bottomLeft");
            w.WriteAttributeString("state", "frozen");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("sheetFormatPr");
            w.WriteAttributeString("defaultRowHeight", "15");
            w.WriteEndElement();

            w.WriteStartElement("cols");
            for (var c = 0; c < columns.Count; c++)
            {
                w.WriteStartElement("col");
                w.WriteAttributeString("min", (c + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("max", (c + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("width", columns[c].Width.ToString("0.##", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("sheetData");
            w.WriteStartElement("row");
            w.WriteAttributeString("r", "1");
            for (var c = 0; c < columns.Count; c++)
                Text(w, c, 1, columns[c].Header, StyleHeader);
            w.WriteEndElement();

            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                var number = r + 2;
                w.WriteStartElement("row");
                w.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
                for (var c = 0; c < columns.Count && c < row.Length; c++)
                    Cell(w, c, number, row[c], columns[c].Format);
                w.WriteEndElement();
            }
            w.WriteEndElement();

            if (columns.Count > 0)
            {
                w.WriteStartElement("autoFilter");
                w.WriteAttributeString("ref", $"A1:{ColumnName(columns.Count - 1)}{lastRow}");
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        private static void Cell(XmlWriter w, int column, int row, object? value, XlsxFormat format)
        {
            switch (value)
            {
                case null:
                    return;
                case string text:
                    Text(w, column, row, text, format == XlsxFormat.Wrapped ? StyleWrapped : StyleDefault);
                    return;
                case DateTime date:
                    Number(w, column, row, date.ToOADate(), StyleDateTime);
                    return;
                case TimeSpan span:
                    Number(w, column, row, span.TotalDays, StyleDuration);
                    return;
                case int or long or short or byte or float or double or decimal:
                    Number(w, column, row, Convert.ToDouble(value, CultureInfo.InvariantCulture), StyleDefault);
                    return;
                default:
                    Text(w, column, row, Convert.ToString(value, CultureInfo.InvariantCulture), StyleDefault);
                    return;
            }
        }

        private static void Text(XmlWriter w, int column, int row, string? text, int style)
        {
            w.WriteStartElement("c");
            w.WriteAttributeString("r", ColumnName(column) + row.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("t", "inlineStr");
            w.WriteStartElement("is");
            w.WriteStartElement("t");
            w.WriteAttributeString("xml", "space", null, "preserve");
            w.WriteString(CellText(text));
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void Number(XmlWriter w, int column, int row, double value, int style)
        {
            w.WriteStartElement("c");
            w.WriteAttributeString("r", ColumnName(column) + row.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("v", value.ToString("R", CultureInfo.InvariantCulture));
            w.WriteEndElement();
        }
    }
}
