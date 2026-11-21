using System.Text;

namespace PhotoLiker.PhotoBook
{
    /// <summary>How the lines of a paragraph are set between the sides of the page.</summary>
    public enum TextAlign
    {
        Left,
        Center,
        Right,
        Justify,
    }

    /// <summary>
    /// How a run of text looks. <see cref="Size"/> is in points (0: the size the document has by default, see <see cref="RichStyle.DefaultSize"/>), <see cref="Color"/> is 0xRRGGBB, or null
    /// for the colour of the text of the book (black). A style that is <c>default</c> is the plain look: nothing bold, nothing coloured, the usual size.
    /// </summary>
    public readonly record struct RichStyle(bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false, double Size = 0, int? Color = null)
    {
        /// <summary>The size of text that has none of its own, in points.</summary>
        public const double DefaultSize = 12;

        /// <summary>The size this text is set in, in points.</summary>
        public double PointSize => Size > 0 ? Size : DefaultSize;
    }

    /// <summary>A piece of text that is all in one style. A line break inside a paragraph (Shift+Enter) is a <c>\n</c> in the text.</summary>
    public sealed record RichRun(string Text, RichStyle Style);

    /// <summary>A paragraph: what was typed up to Enter. One with no runs is an empty line.</summary>
    public sealed record RichParagraph(TextAlign Align, IReadOnlyList<RichRun> Runs)
    {
        public string PlainText => string.Concat(Runs.Select(run => run.Text));

        public bool IsBlank => string.IsNullOrWhiteSpace(PlainText);
    }

    /// <summary>
    /// The description of a photo as the book has it: paragraphs of runs. It is what the Rich Text Box of the dialog holds, read from its RTF by <see cref="RtfParser"/>; nothing of the Rich Text
    /// Box (fonts, bullets, pictures) is kept that the PDF and the EPUB could not both show.
    /// </summary>
    public sealed class RichDocument
    {
        public static RichDocument Empty { get; } = new([]);

        public RichDocument(IReadOnlyList<RichParagraph> paragraphs) => Paragraphs = paragraphs;

        public IReadOnlyList<RichParagraph> Paragraphs { get; }

        /// <summary>True when there is nothing to show: no text, or only blanks.</summary>
        public bool IsEmpty => Paragraphs.All(paragraph => paragraph.IsBlank);

        public string PlainText => string.Join("\n", Paragraphs.Select(paragraph => paragraph.PlainText));

        /// <summary>
        /// The document without the empty lines at its end: a Rich Text Box that was left with a few Enters at the end would otherwise push the photo down the page. The ones at the
        /// start stay (they are a space someone typed).
        /// </summary>
        public RichDocument WithoutTrailingBlank()
        {
            var count = Paragraphs.Count;
            while (count > 0 && Paragraphs[count - 1].IsBlank)
                count--;

            return count == Paragraphs.Count ? this : new RichDocument([.. Paragraphs.Take(count)]);
        }

        /// <summary>Plain text as a document: each line a paragraph, all in the default style.</summary>
        public static RichDocument FromPlainText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return Empty;

            return new RichDocument([.. text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(line => new RichParagraph(TextAlign.Left, line.Length == 0 ? [] : [new RichRun(line, default)]))]);
        }
    }

    /// <summary>
    /// Reads the RTF of a Rich Text Box. It knows what that writes for the formatting the dialog offers (bold, italic, underline, strike-through, size, colour, alignment) and for text: the
    /// characters of the languages of Windows as <c>\uN?</c>, or as <c>\'hh</c> bytes in the code page of the font. It skips what it does not know (pictures, tables, fonts, styles) and never throws:
    /// RTF that is broken gives the text that could be read.
    /// </summary>
    public static class RtfParser
    {
        public static RichDocument Parse(string? rtf)
        {
            if (string.IsNullOrWhiteSpace(rtf))
                return RichDocument.Empty;

            // Not RTF at all: it is the text, as it is
            if (!rtf.AsSpan().TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
                return RichDocument.FromPlainText(rtf);

            try
            {
                return new Reader(rtf).Read();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException or NotSupportedException)
            {
                return RichDocument.Empty;
            }
        }

        /// <summary>The characters of a code page are what <c>\'hh</c> stands for; the code pages other than the ones .NET has built in come with this provider.</summary>
        static RtfParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        /// <summary>The state RTF keeps per group: what the characters look like, and how many characters follow a <c>\uN</c> that stand for it for readers that do not know Unicode.</summary>
        private sealed record State(RichStyle Style, int Font, int SkipAfterUnicode, bool Skip);

        private sealed class Reader(string text)
        {
            private int _position;
            private readonly Stack<State> _stack = new();
            private State _state = new(default, 0, 1, false);

            private readonly List<RichParagraph> _paragraphs = [];
            private List<RichRun> _runs = [];
            private readonly StringBuilder _pending = new();
            private RichStyle _pendingStyle;
            private TextAlign _align = TextAlign.Left;

            // The tables of the head: the code page each font is in (for \'hh), the colours
            private readonly Dictionary<int, int> _fontCodePages = [];
            private readonly List<int?> _colours = [null];
            private int _defaultCodePage = 1252;
            private int _defaultFont;

            // A destination that is read for its tables (fonts, colours) and not for text; its depth in groups
            private string? _table;
            private int _tableDepth;
            private int _currentTableFont = -1;
            private int _colourRed = -1, _colourGreen = -1, _colourBlue = -1;
            private bool _colourAny;

            private int _unicodeSkip;

            public RichDocument Read()
            {
                while (_position < text.Length)
                {
                    var c = text[_position];
                    switch (c)
                    {
                        case '{':
                            _position++;
                            _stack.Push(_state);
                            if (_table is not null)
                                _tableDepth++;
                            break;

                        case '}':
                            _position++;
                            CloseGroup();
                            break;

                        case '\\':
                            _position++;
                            ReadControl();
                            break;

                        case '\r':
                        case '\n':
                            _position++; // the line ends of the file are not in the text
                            break;

                        default:
                            _position++;
                            if (_unicodeSkip > 0)
                            {
                                _unicodeSkip--; // the stand-in for a \uN
                                break;
                            }
                            if (_table is not null)
                                ReadTableCharacter(c);
                            else
                                Append(c.ToString());
                            break;
                    }
                }

                FlushRun();
                if (_runs.Count > 0 || _paragraphs.Count == 0)
                    EndParagraph(keepEmpty: false);

                return new RichDocument(_paragraphs);
            }

            private void CloseGroup()
            {
                var wasTable = _table is not null && _tableDepth == 0;
                if (_table is not null && _tableDepth > 0)
                    _tableDepth--;

                if (wasTable)
                    _table = null;

                if (_stack.Count == 0)
                    return;

                FlushRun(); // what was typed in the group's style is closed before the style goes back
                _state = _stack.Pop();
            }

            private void ReadControl()
            {
                if (_position >= text.Length)
                    return;

                var c = text[_position];
                if (!char.IsAsciiLetter(c))
                {
                    _position++;
                    switch (c)
                    {
                        case '\\':
                        case '{':
                        case '}':
                            if (_table is null && !_state.Skip)
                                Append(c.ToString());
                            break;
                        case '\'':
                            ReadHexByte();
                            break;
                        case '*':
                            // An ignorable destination ({\*\name ...}) is skipped unless it is a known one: none is that matters here
                            _state = _state with { Skip = true };
                            break;
                        case '~':
                            Append("\x00A0"); // a non-breaking space
                            break;
                        case '_':
                            Append("\x2011"); // a non-breaking hyphen
                            break;
                        case '-':
                            break; // an optional hyphen
                        case '\r':
                        case '\n':
                            if (_table is null && !_state.Skip)
                                EndParagraph(keepEmpty: true); // a line end after a backslash is \par
                            break;
                    }
                    return;
                }

                var start = _position;
                while (_position < text.Length && char.IsAsciiLetter(text[_position]))
                    _position++;
                var word = text[start.._position];

                int? parameter = null;
                var negative = false;
                if (_position < text.Length && text[_position] == '-')
                {
                    negative = true;
                    _position++;
                }
                var digits = _position;
                while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                    _position++;
                if (_position > digits && int.TryParse(text.AsSpan(digits, _position - digits), out var number))
                    parameter = negative ? -number : number;
                else if (negative)
                    _position--; // the minus was not a sign

                if (_position < text.Length && text[_position] == ' ')
                    _position++; // the space that ends a control word is not text

                Apply(word, parameter);
            }

            private void ReadHexByte()
            {
                if (_position + 2 > text.Length || !byte.TryParse(text.AsSpan(_position, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
                    return;

                _position += 2;
                if (_unicodeSkip > 0)
                {
                    _unicodeSkip--;
                    return;
                }

                if (_table is not null)
                {
                    ReadTableCharacter((char)value);
                    return;
                }

                // A run of bytes is one character in a double byte code page: they are read together, by the code page of the font
                var bytes = new List<byte> { value };
                while (_position + 4 <= text.Length && text[_position] == '\\' && text[_position + 1] == '\'' &&
                       byte.TryParse(text.AsSpan(_position + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var next))
                {
                    bytes.Add(next);
                    _position += 4;
                }

                if (_state.Skip)
                    return;

                Append(DecodeBytes([.. bytes], CodePageOf(_state.Font)));
            }

            private int CodePageOf(int font) => _fontCodePages.TryGetValue(font, out var page) ? page : _defaultCodePage;

            private static string DecodeBytes(byte[] bytes, int codePage)
            {
                try
                {
                    return Encoding.GetEncoding(codePage).GetString(bytes);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    return Encoding.Latin1.GetString(bytes);
                }
            }

            private void Apply(string word, int? parameter)
            {
                // The tables of the head
                switch (word)
                {
                    case "fonttbl":
                        BeginTable("fonttbl");
                        return;
                    case "colortbl":
                        BeginTable("colortbl");
                        _colourRed = _colourGreen = _colourBlue = -1;
                        _colourAny = false;
                        return;
                    case "stylesheet" or "info" or "pict" or "object" or "header" or "footer" or "headerl" or "headerr" or "footerl" or "footerr" or "xmlnstbl" or "listtable" or "listoverridetable" or
                         "rsidtbl" or "generator" or "latentstyles" or "datastore" or "themedata" or "colorschememapping" or "revtbl" or "fldinst" or "bkmkstart" or "bkmkend" or "pntxtb" or "pntxta":
                        _state = _state with { Skip = true };
                        return;
                    case "ansicpg":
                        if (parameter is { } page)
                            _defaultCodePage = page;
                        return;
                    case "deff":
                        _defaultFont = parameter ?? 0;
                        return;
                }

                if (_table == "fonttbl")
                {
                    switch (word)
                    {
                        case "f":
                            _currentTableFont = parameter ?? 0;
                            break;
                        case "fcharset":
                            if (_currentTableFont >= 0 && parameter is { } charset && CodePageOfCharset(charset) is { } codePage)
                                _fontCodePages[_currentTableFont] = codePage;
                            break;
                    }
                    return;
                }

                if (_table == "colortbl")
                {
                    switch (word)
                    {
                        case "red":
                            _colourRed = parameter ?? 0;
                            _colourAny = true;
                            break;
                        case "green":
                            _colourGreen = parameter ?? 0;
                            _colourAny = true;
                            break;
                        case "blue":
                            _colourBlue = parameter ?? 0;
                            _colourAny = true;
                            break;
                    }
                    return;
                }

                if (_state.Skip)
                    return;

                switch (word)
                {
                    case "par":
                    case "sect":
                    case "page":
                        EndParagraph(keepEmpty: true);
                        break;
                    case "line":
                        Append("\n");
                        break;
                    case "tab":
                        Append("\t");
                        break;
                    case "emdash":
                        Append("—");
                        break;
                    case "endash":
                        Append("–");
                        break;
                    case "bullet":
                        Append("•");
                        break;
                    case "lquote":
                        Append("‘");
                        break;
                    case "rquote":
                        Append("’");
                        break;
                    case "ldblquote":
                        Append("“");
                        break;
                    case "rdblquote":
                        Append("”");
                        break;
                    case "u":
                        if (parameter is { } code)
                        {
                            // A value above 32767 is written as a negative number
                            Append(((char)(code < 0 ? code + 65536 : code)).ToString());
                            _unicodeSkip = _state.SkipAfterUnicode;
                        }
                        break;
                    case "uc":
                        _state = _state with { SkipAfterUnicode = parameter ?? 1 };
                        break;
                    case "pard":
                        _align = TextAlign.Left;
                        break;
                    case "ql":
                        _align = TextAlign.Left;
                        break;
                    case "qc":
                        _align = TextAlign.Center;
                        break;
                    case "qr":
                        _align = TextAlign.Right;
                        break;
                    case "qj":
                        _align = TextAlign.Justify;
                        break;
                    case "plain":
                        Style(_ => default);
                        break;
                    case "b":
                        Style(style => style with { Bold = parameter != 0 });
                        break;
                    case "i":
                        Style(style => style with { Italic = parameter != 0 });
                        break;
                    case "ul" or "uld" or "uldb" or "ulw" or "ulth":
                        Style(style => style with { Underline = parameter != 0 });
                        break;
                    case "ulnone":
                        Style(style => style with { Underline = false });
                        break;
                    case "strike" or "striked":
                        Style(style => style with { Strike = parameter != 0 });
                        break;
                    case "fs":
                        Style(style => style with { Size = Math.Max(0, (parameter ?? 0) / 2.0) });
                        break;
                    case "cf":
                        Style(style => style with { Color = parameter is { } index && index > 0 && index < _colours.Count ? _colours[index] : null });
                        break;
                    case "f":
                        _state = _state with { Font = parameter ?? _defaultFont };
                        break;
                }
            }

            /// <summary>The text written before is closed in the style it had, and the new style goes on from here.</summary>
            private void Style(Func<RichStyle, RichStyle> change)
            {
                FlushRun();
                _state = _state with { Style = change(_state.Style) };
            }

            private void Append(string value)
            {
                if (_state.Skip || value.Length == 0)
                    return;

                if (_pending.Length > 0 && _pendingStyle != _state.Style)
                    FlushRun();

                _pendingStyle = _state.Style;
                _pending.Append(value);
            }

            private void FlushRun()
            {
                if (_pending.Length == 0)
                    return;

                _runs.Add(new RichRun(_pending.ToString(), _pendingStyle));
                _pending.Clear();
            }

            private void EndParagraph(bool keepEmpty)
            {
                FlushRun();
                if (_runs.Count == 0 && !keepEmpty)
                    return;

                _paragraphs.Add(new RichParagraph(_align, _runs));
                _runs = [];
            }

            // The head tables are read as text: a font table has the name of each font (which is not wanted), a colour table has ;-separated colours
            private void BeginTable(string name)
            {
                _table = name;
                _tableDepth = 0;
                _currentTableFont = -1;
            }

            private void ReadTableCharacter(char c)
            {
                if (_table == "colortbl" && c == ';')
                    EndColourEntry();
            }

            private void EndColourEntry()
            {
                // The first entry of a colour table, before the first ";" with nothing in it, is the automatic colour: index 0 is already that
                if (_colourAny)
                    _colours.Add((Math.Clamp(_colourRed, 0, 255) << 16) | (Math.Clamp(_colourGreen, 0, 255) << 8) | Math.Clamp(_colourBlue, 0, 255));
                else if (_colours.Count > 1 || _colourSeenFirst)
                    _colours.Add(null);

                _colourSeenFirst = true;
                _colourRed = _colourGreen = _colourBlue = -1;
                _colourAny = false;
            }

            private bool _colourSeenFirst;
        }

        /// <summary>The code page of a font charset (<c>\fcharsetN</c>), for the characters written as <c>\'hh</c>.</summary>
        private static int? CodePageOfCharset(int charset) => charset switch
        {
            0 => 1252,
            2 => 42, // symbol
            77 => 10000,
            128 => 932,
            129 => 949,
            130 => 1361,
            134 => 936,
            136 => 950,
            161 => 1253,
            162 => 1254,
            163 => 1258,
            177 => 1255,
            178 => 1256,
            186 => 1257,
            204 => 1251,
            222 => 874,
            238 => 1250,
            254 => 437,
            255 => 850,
            _ => null,
        };
    }
}
