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

    /// <summary>
    /// A picture that stands in the text as a paragraph of its own: a PNG (which can have a see-through background, as a picture cut out of a photo has) or a JPEG, as the bytes of the file, and how
    /// wide it is as a share of the width of the text.
    /// </summary>
    public sealed record RichPicture(byte[] Data, bool IsPng, int PixelWidth, int PixelHeight, double WidthShare)
    {
        /// <summary>The height as a number of times the width.</summary>
        public double Ratio => PixelWidth > 0 && PixelHeight > 0 ? (double)PixelHeight / PixelWidth : 0.75;

        public string Extension => IsPng ? ".png" : ".jpg";

        public string MediaType => IsPng ? "image/png" : "image/jpeg";

        /// <summary>The picture of the bytes of a PNG or a JPEG file; null when they are neither.</summary>
        public static RichPicture? FromFile(byte[] data, double widthShare) =>
            ImageHeader.TryRead(data, out var png, out var width, out var height) ? new RichPicture(data, png, width, height, Math.Clamp(widthShare, 0.05, 1.0)) : null;
    }

    /// <summary>
    /// A paragraph: what was typed up to Enter. One with no runs is an empty line. It can be indented (<see cref="LeftIndent"/>, <see cref="FirstLineIndent"/>), have tab stops, or be a picture
    /// and nothing else (<see cref="Picture"/>). Distances are in points of a column that is <see cref="RichDocument.ReferenceWidth"/> wide: a column of another width has them in the same share
    /// of it, so a page that is indented a quarter of its width is that in a PDF of any paper and in an EPUB of any screen.
    /// </summary>
    public sealed record RichParagraph(TextAlign Align, IReadOnlyList<RichRun> Runs)
    {
        public string PlainText => string.Concat(Runs.Select(run => run.Text));

        public bool IsBlank => Picture is null && string.IsNullOrWhiteSpace(PlainText);

        /// <summary>How far the lines of the paragraph start from the left side of the column.</summary>
        public double LeftIndent { get; init; }

        /// <summary>How far the first line starts from where the others do (negative: it starts further left, a hanging indent).</summary>
        public double FirstLineIndent { get; init; }

        /// <summary>Where a tab character stops, from the left side of the column, from the nearest to the farthest; beyond the last the stops are every half inch.</summary>
        public IReadOnlyList<double> TabStops { get; init; } = [];

        /// <summary>The picture this paragraph is, when it is one (it has no text then).</summary>
        public RichPicture? Picture { get; init; }

        /// <summary>
        /// What stands before the text of an item of a list, as the Rich Text Box shows it: a bullet (<c>•</c>) or a number (<c>3.</c>); null for a paragraph that is not an item. It is not in
        /// <see cref="PlainText"/> (the Rich Text Box does not have it in its text either); a PDF, an EPUB or a Word document set it before the text, see <see cref="WithMarkerInText"/>.
        /// </summary>
        public string? Marker { get; init; }

        /// <summary>True when the marker is a number (or a letter) and not a bullet.</summary>
        public bool IsNumbered => Marker is { } marker && marker.Any(char.IsLetterOrDigit);

        /// <summary>A paragraph that is only a picture.</summary>
        public static RichParagraph OfPicture(RichPicture picture, TextAlign align = TextAlign.Center) => new(align, []) { Picture = picture };

        /// <summary>
        /// The paragraph with its marker made a part of its text: the marker, a tab, then the text, the tab ending at the left indent of the paragraph (the text of a list is indented, with the
        /// marker hanging out to its left). A marker with no text after it (an item nobody wrote anything in) is not shown. What shows the text of a book needs nothing else to show a list.
        /// </summary>
        public RichParagraph WithMarkerInText()
        {
            if (Marker is not { } marker)
                return this;

            if (IsBlank)
                return this with { Marker = null };

            // The marker looks as the start of the text does (a bold item has a bold number), but is not underlined or struck through
            var look = Runs.Count > 0 ? Runs[0].Style with { Underline = false, Strike = false } : default;
            var stops = TabStops;
            if (FirstLineIndent < 0 && LeftIndent > 0 && !stops.Any(stop => Math.Abs(stop - LeftIndent) < 0.5))
                stops = [.. stops.Append(LeftIndent).Order()];

            return this with { Runs = [new RichRun(marker + "\t", look), .. Runs], TabStops = stops, Marker = null };
        }
    }

    /// <summary>
    /// The description of a photo as the book has it: paragraphs of runs. It is what the Rich Text Box of the dialog holds, read from its RTF by <see cref="RtfParser"/>; nothing of the Rich Text
    /// Box (fonts) is kept that the PDF and the EPUB could not both show. Pictures (PNG and JPEG) are kept as paragraphs of their own, indents, tab stops and the marker of an item of a list with
    /// the paragraph.
    /// </summary>
    public sealed class RichDocument
    {
        /// <summary>The width in points of the column that the indents, the tab stops and the sizes of the pictures of a paragraph are measured against (six inches).</summary>
        public const double ReferenceWidth = 432;

        public static RichDocument Empty { get; } = new([]);

        public RichDocument(IReadOnlyList<RichParagraph> paragraphs) => Paragraphs = paragraphs;

        public IReadOnlyList<RichParagraph> Paragraphs { get; }

        /// <summary>True when there is nothing to show: no text, or only blanks.</summary>
        public bool IsEmpty => Paragraphs.All(paragraph => paragraph.IsBlank);

        public string PlainText => string.Join("\n", Paragraphs.Select(paragraph => paragraph.PlainText));

        /// <summary>True when a paragraph of the document is a picture.</summary>
        public bool HasPictures => Paragraphs.Any(paragraph => paragraph.Picture is not null);

        /// <summary>The document with its pictures taken out.</summary>
        public RichDocument WithoutPictures() => HasPictures ? new RichDocument([.. Paragraphs.Where(paragraph => paragraph.Picture is null)]) : this;

        /// <summary>True when a paragraph of the document is an item of a list.</summary>
        public bool HasLists => Paragraphs.Any(paragraph => paragraph.Marker is not null);

        /// <summary>The document with the marker of each item of a list set in its text (see <see cref="RichParagraph.WithMarkerInText"/>); what draws or writes the text calls it first.</summary>
        public RichDocument WithMarkersInText() => HasLists ? new RichDocument([.. Paragraphs.Select(paragraph => paragraph.WithMarkerInText())]) : this;

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
    /// characters of the languages of Windows as <c>\uN?</c>, or as <c>\'hh</c> bytes in the code page of the font, indents (<c>\li</c>, <c>\fi</c>), tab stops (<c>\tx</c>), the bullets and the
    /// numbers of lists (the text a Rich Text Box writes for each in <c>\pntext</c>, Word in <c>\listtext</c>) and pictures that are PNG or JPEG (<c>\pngblip</c>, <c>\jpegblip</c>). It skips what it
    /// does not know (the other kinds of picture, tables, fonts, styles) and never throws: RTF that is broken gives the text that could be read.
    /// </summary>
    public static class RtfParser
    {
        /// <param name="readPictures">False: the pictures are skipped (as the other things the parser does not know), which is much quicker for a text that holds some and is wanted only for its words.</param>
        public static RichDocument Parse(string? rtf, bool readPictures = true)
        {
            if (string.IsNullOrWhiteSpace(rtf))
                return RichDocument.Empty;

            // Not RTF at all: it is the text, as it is
            if (!rtf.AsSpan().TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
                return RichDocument.FromPlainText(rtf);

            try
            {
                return new Reader(rtf, readPictures).Read();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException or NotSupportedException)
            {
                return RichDocument.Empty;
            }
        }

        /// <summary>The characters of a code page are what <c>\'hh</c> stands for; the code pages other than the ones .NET has built in come with this provider.</summary>
        static RtfParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        /// <summary>
        /// The state RTF keeps per group: what the characters look like, and how many characters follow a <c>\uN</c> that stand for it for readers that do not know Unicode. <c>Marker</c>: the
        /// group is the number or the bullet of an item of a list, whose characters are the marker of the paragraph and not its text.
        /// </summary>
        private sealed record State(RichStyle Style, int Font, int SkipAfterUnicode, bool Skip, bool Marker = false);

        private sealed class Reader(string text, bool readPictures)
        {
            private int _position;
            private readonly Stack<State> _stack = new();
            private State _state = new(default, 0, 1, false);

            // The marker of the paragraph that is being read (the number or the bullet of a list item), and whether a font of symbols wrote it
            private readonly StringBuilder _marker = new();
            private bool _markerSymbol;

            private readonly List<RichParagraph> _paragraphs = [];
            private List<RichRun> _runs = [];
            private readonly StringBuilder _pending = new();
            private RichStyle _pendingStyle;
            private TextAlign _align = TextAlign.Left;
            private double _leftIndent, _firstIndent;
            private readonly List<double> _tabs = [];

            // A picture that is being read (its control words and its hex digits), and whether the paragraph mark that ends the paragraph the picture was in is yet to come
            private PictureCapture? _picture;
            private bool _afterPicture;

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
                            if (_picture is not null && _stack.Count == _picture.Depth)
                            {
                                if (char.IsAsciiHexDigit(c))
                                    _picture.Hex.Append(c); // the bytes of the picture
                                break;
                            }
                            if (_unicodeSkip > 0)
                            {
                                _unicodeSkip--; // the stand-in for a \uN
                                break;
                            }
                            if (_table is not null)
                                ReadTableCharacter(c);
                            else if (!_state.Skip)
                                Append(c.ToString()); // (the bytes of a picture that is skipped are the bulk of an RTF: they are not looked at)
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
                if (_picture is not null && _stack.Count == _picture.Depth)
                    EndPicture();

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
                    case "pict":
                        if (readPictures && !_state.Skip && _table is null)
                            _picture = new PictureCapture(_stack.Count);
                        _state = _state with { Skip = true };
                        return;
                    case "pntext" or "listtext":
                        // The number or the bullet of a list item, as the program that wrote the text drew it: what is in the group is the marker of the paragraph, not its text
                        if (!_state.Skip)
                            _state = _state with { Marker = true };
                        return;
                    case "stylesheet" or "info" or "object" or "header" or "footer" or "headerl" or "headerr" or "footerl" or "footerr" or "xmlnstbl" or "listtable" or "listoverridetable" or
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

                if (_picture is not null && _stack.Count == _picture.Depth)
                {
                    _picture.Control(word, parameter);
                    return;
                }

                if (_state.Skip)
                    return;

                // Word starts the group of a marker with \pard and \plain, as if it were a paragraph of its own: what it says of paragraphs is not the paragraph's
                if (_state.Marker && word is "pard" or "li" or "fi" or "tx" or "ql" or "qc" or "qr" or "qj" or "par" or "sect" or "page")
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
                        _leftIndent = _firstIndent = 0;
                        _tabs.Clear();
                        break;
                    case "li":
                        _leftIndent = Math.Max(0, (parameter ?? 0) / 20.0); // twips to points
                        break;
                    case "fi":
                        _firstIndent = (parameter ?? 0) / 20.0;
                        break;
                    case "tx":
                        if (parameter is { } stop && stop > 0 && _tabs.Count < 32)
                            _tabs.Add(stop / 20.0);
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

                if (_state.Marker)
                {
                    _marker.Append(value);
                    _markerSymbol |= CodePageOf(_state.Font) == 42; // whatever a font of symbols draws for a bullet is a bullet here
                    return;
                }

                _afterPicture = false;
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

                // The paragraph mark after a picture ends the paragraph the picture was the whole of: the picture is that paragraph already
                if (_afterPicture && keepEmpty && _runs.Count == 0)
                {
                    _afterPicture = false;
                    TakeMarker();
                    return;
                }

                _afterPicture = false;
                if (_runs.Count == 0 && !keepEmpty)
                {
                    TakeMarker();
                    return;
                }

                _paragraphs.Add(NewParagraph(_runs, TakeMarker()));
                _runs = [];
            }

            private RichParagraph NewParagraph(IReadOnlyList<RichRun> runs, string? marker = null) =>
                new(_align, runs) { LeftIndent = _leftIndent, FirstLineIndent = _firstIndent, TabStops = [.. _tabs.Order()], Marker = marker };

            /// <summary>The marker the paragraph that ends now has (null when it is not a list item), and the reader is ready for the next paragraph.</summary>
            private string? TakeMarker()
            {
                var symbol = _markerSymbol;
                var marker = _marker.ToString().Trim(); // (the tab that follows the number is not a part of it)
                _marker.Clear();
                _markerSymbol = false;

                if (marker.Length == 0)
                    return null;

                // The bullet that a Rich Text Box writes is a character of its Symbol font, which another font draws as a dot in the middle of the line
                return symbol || (marker.Length == 1 && marker[0] is MiddleDot or SymbolBullet or Bullet) ? Bullet.ToString() : marker;
            }

            // The characters that stand for a bullet: the middle dot, the bullet of the Symbol font (as the private use area of Unicode has it) and the bullet itself. They are numbers here:
            // the first two are characters that cannot be told apart in the code.
            private const char MiddleDot = (char)0x00B7;
            private const char SymbolBullet = (char)0xF0B7;
            private const char Bullet = (char)0x2022;

            /// <summary>The picture is done: when it is a kind that is read and its bytes are those of one, it is a paragraph of its own.</summary>
            private void EndPicture()
            {
                var capture = _picture!;
                _picture = null;
                if (capture.Kind <= 0 || capture.Hex.Length < 16 || capture.Hex.Length % 2 != 0)
                    return;

                byte[] data;
                try
                {
                    data = Convert.FromHexString(capture.Hex.ToString());
                }
                catch (FormatException)
                {
                    return;
                }

                if (!ImageHeader.TryRead(data, out var png, out var pixelWidth, out var pixelHeight) || png != (capture.Kind == 1))
                    return;

                // How wide it is shown: the goal the Rich Text Box writes (twips), else its size in hundredths of a millimetre, else its pixels at 96 to the inch
                var points = capture.GoalWidth > 0 ? capture.GoalWidth / 20.0 : capture.Width > 0 ? capture.Width * 72.0 / 2540.0 : pixelWidth * 0.75;
                points *= capture.ScaleX / 100.0;

                FlushRun();
                if (_runs.Count > 0)
                    EndParagraph(keepEmpty: false);

                _paragraphs.Add(NewParagraph([]) with { Picture = new RichPicture(data, png, pixelWidth, pixelHeight, Math.Clamp(points / RichDocument.ReferenceWidth, 0.05, 1.0)) });
                _afterPicture = true;
            }

            /// <summary>The control words and the digits of a picture while it is read.</summary>
            private sealed class PictureCapture(int depth)
            {
                /// <summary>How many groups are open round the picture's own.</summary>
                public int Depth { get; } = depth;

                /// <summary>1: PNG, 2: JPEG, -1: a kind that is not read (a metafile, a bitmap).</summary>
                public int Kind { get; private set; }

                public int Width { get; private set; }

                public int GoalWidth { get; private set; }

                public int ScaleX { get; private set; } = 100;

                public StringBuilder Hex { get; } = new();

                public void Control(string word, int? parameter)
                {
                    switch (word)
                    {
                        case "pngblip":
                            Kind = 1;
                            break;
                        case "jpegblip":
                            Kind = 2;
                            break;
                        case "wmetafile" or "emfblip" or "dibitmap" or "wbitmap" or "macpict" or "pmmetafile" or "bin":
                            Kind = -1;
                            break;
                        case "picw":
                            Width = parameter ?? 0;
                            break;
                        case "picwgoal":
                            GoalWidth = parameter ?? 0;
                            break;
                        case "picscalex":
                            ScaleX = parameter is > 0 ? parameter.Value : 100;
                            break;
                    }
                }
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
