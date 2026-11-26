using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PhotoLiker.Drawing;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Cloud;
using PhotoLiker.PhotoBook;

namespace PhotoLikerUI
{
    /// <summary>
    /// Scanning with the kind of writing "AI service": the photo of a page is sent to the AI service that is picked in the AI box (ChatGPT, Gemini or Claude), which reads it and writes the text back
    /// (<see cref="CloudReader"/>). The text is put into the page like the text of a scan and the pictures of the page are looked for as they are after any scan. A page has no boxes on its photo
    /// (a service gives text, not places) unless the person turned on Lines on the photo: then the service is asked for a rectangle round each line too, which costs more, and the page has boxes as a
    /// scan on this computer gives. The person is asked once for each service, in each window, before a photo leaves the computer.
    /// </summary>
    internal sealed partial class ManuscriptDialog
    {
        private readonly HashSet<AiVendor> _photoSendAllowed = [];

        /// <summary>Asked before the photo of a page is sent to an AI service (it gets the name of the service); a test answers it.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<string, bool>? AskSendPhoto { get; set; }

        /// <summary>
        /// Whether the photos of the pages may be sent to the service: the person is asked the first time for each service in this window (a yes holds until the window is closed, so a book of fifty
        /// pages is not fifty questions), and a no is asked again the next time.
        /// </summary>
        private bool AllowSendingPhotos(AiVendor vendor)
        {
            if (_photoSendAllowed.Contains(vendor))
                return true;

            var name = AiVendors.Name(vendor);
            var yes = AskSendPhoto?.Invoke(name) ?? MessageBox.Show(this, string.Format(ManuscriptStrings.ConfirmSendPhotoFormat, name), ManuscriptStrings.DialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            if (yes)
                _photoSendAllowed.Add(vendor);

            return yes;
        }

        /// <summary>Reads the pages with the AI service that is picked, one after the other; a service that fails (a key that is not accepted, a limit) ends the scan, and the pages read stay read.</summary>
        private async Task ScanWithAiAsync(IReadOnlyList<int> indexes)
        {
            if (_ai.Active is not { } account)
            {
                SetStatus(ManuscriptStrings.StatusAiScanNoService);
                return;
            }

            var language = Language;
            var service = AiVendors.Name(account.Vendor);
            if (!AllowSendingPhotos(account.Vendor))
            {
                SetStatus(ManuscriptStrings.StatusAiScanDeclined);
                return;
            }

            var source = new CancellationTokenSource();
            _scan = source;
            SetBusy(true);
            var read = 0;
            var empty = 0;
            var pictureCount = 0;
            var working = 0;
            var lastReading = string.Empty; // what the service wrote for the page that was read last: the page may have had text before, which says nothing about this
            var places = AiPlacesCheck.Checked; // the lines are marked on the photo, and the service says what is in each box
            var finderMissing = false;
            var unplaced = 0; // the pages that were read, but not box by box
            var lastPlaced = false;
            LineMarker? marker = null;
            try
            {
                // The pictures are looked for too when the person wants them (and was not asked and said no already), as after any scan
                var findPictures = ScanPicturesCheck.Checked && !_picturesDeclined;
                if (findPictures && !await EnsurePictureModelsAsync())
                {
                    findPictures = false;
                    if (IsDisposed)
                        return;
                }

                // The finder of lines marks the lines on the photo: a small model of this computer, asked for once; without it the pages are read without boxes
                if (places && FindLinesOverride is null)
                {
                    if (_ensureFinderAsync is { } ensureFinder && !await ensureFinder())
                    {
                        places = false;
                        finderMissing = true;
                        if (IsDisposed)
                            return;
                    }
                    else
                    {
                        marker = new LineMarker(PhotoLiker.Effects.Models.ToolModels.OcrFinder.PathIn(_models.ModelsFolder), _models.Threads);
                    }
                }

                StopButton.Enabled = true;
                _progress.Maximum = Math.Max(1, indexes.Count);
                var chat = new CloudChat(account.Vendor, account.ApiKey, account.Model, CloudClient);
                foreach (var index in indexes)
                {
                    source.Token.ThrowIfCancellationRequested();
                    if (index >= _pages.Count)
                        break;

                    working = index + 1;
                    var page = _pages[index];
                    if (!File.Exists(page.File))
                    {
                        SetStatus(string.Format(ManuscriptStrings.PageMissingFormat, page.File));
                        continue;
                    }

                    SetStatus(string.Format(ManuscriptStrings.StatusAiReadingFormat, index + 1, service));
                    var context = UseContextCheck.Checked ? ManuscriptContext.Before(_pages, index) : null; // the pages before it, as they are now

                    var (jpeg, regions, pictures, width, height) = await Task.Run(() =>
                    {
                        var decoded = WicPhotoDecoder.DecodeFull(File.ReadAllBytes(page.File!));
                        var bitmap = new Bitmap32(decoded.Width, decoded.Height, decoded.Bgra);
                        IReadOnlyList<PhotoLiker.Drawing.Ocr.TextRegion> found = [];
                        if (places)
                            found = FindLinesOverride?.Invoke(bitmap, source.Token) ?? marker!.Find(bitmap, source.Token);
                        return (Jpeg: JpegForAi(bitmap, found), Regions: found, Pictures: findPictures ? TryFindPictures(bitmap, source.Token) : null, bitmap.Width, bitmap.Height);
                    }, source.Token);

                    if (IsDisposed)
                        return;

                    // With boxes on the photo the service says what is in each; a page on which no line was found is read as a whole
                    var reader = new CloudReader(chat, language, context);
                    MarkedReading? marked = null;
                    string text;
                    if (regions.Count > 0)
                    {
                        marked = await reader.ReadMarkedAsync(jpeg, regions.Count, source.Token);
                        text = marked.Text;
                    }
                    else
                    {
                        text = await reader.ReadAsync(jpeg, source.Token);
                    }

                    if (IsDisposed)
                        return;

                    lastPlaced = marked?.Lines is not null;
                    if (marked?.Lines is { } boxed)
                        ApplyMarked(index, boxed, regions, width, height, language, pictures);
                    else
                        ApplyAiText(index, text, width, height, language, pictures);

                    lastReading = text;
                    if (places && text.Length > 0 && !lastPlaced)
                        unplaced++;
                    pictureCount += pictures?.Cut.Count ?? 0;
                    read++;
                    if (text.Length == 0)
                        empty++;
                    _progress.Value = Math.Min(_progress.Maximum, read);
                }

                string done;
                if (indexes.Count == 1)
                {
                    var lineCount = ManuscriptText.LineSpans(lastReading).Count;
                    done = lastReading.Length == 0
                        ? string.Format(ManuscriptStrings.StatusAiReadNothingFormat, indexes[0] + 1, service)
                        : lastPlaced
                            ? string.Format(ManuscriptStrings.StatusAiReadPlacedFormat, indexes[0] + 1, service, lineCount)
                            : string.Format(places ? ManuscriptStrings.StatusAiReadNoPlacesFormat : ManuscriptStrings.StatusAiReadFormat, indexes[0] + 1, service, lineCount);
                }
                else
                {
                    done = string.Format(ManuscriptStrings.StatusAiScanAllDoneFormat, read, service, empty);
                    if (unplaced > 0)
                        done += " " + string.Format(ManuscriptStrings.StatusAiNoPlacesFormat, unplaced);
                }

                if (finderMissing)
                    done += " " + ManuscriptStrings.StatusAiPlacesNoFinder;

                if (pictureCount > 0)
                    done += " " + string.Format(ManuscriptStrings.StatusPicturesInTextFormat, pictureCount);
                SetStatus(done);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    SetStatus(ManuscriptStrings.StatusScanStopped);
            }
            catch (Exception ex) when (ex is AiServiceException or ArgumentException or IOException or InvalidDataException or NotSupportedException or FileNotFoundException
                or UnauthorizedAccessException or InvalidOperationException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(ManuscriptStrings.StatusAiScanFailedFormat, working, ex.Message));
            }
            finally
            {
                _scan = null;
                source.Dispose();
                marker?.Dispose();
                ReleasePictureFinder();
                if (!IsDisposed)
                {
                    SetBusy(false);
                    SaveWork();
                }
            }
        }

        /// <summary>
        /// Takes what the service read into the page: the text as a document (a line to a paragraph, a blank paragraph for a blank line), where the page came from, and when. A page read this way has no
        /// boxes (a service says what is written, not where), so the lines it had are gone; a reading that found no text leaves the text of the page as it is. (A page that was read box by box is
        /// <see cref="ApplyMarked"/>.)
        /// </summary>
        private void ApplyAiText(int index, string text, int width, int height, ScanLanguage language, PictureSearch? pictures)
        {
            var page = _pages[index];
            page.Kind = ScanKind.AiService.ToString();
            page.Language = language.ToString();
            page.ScannedUtc = DateTime.UtcNow;
            page.Width = width;
            page.Height = height;
            page.Lines = null;

            IReadOnlyList<LayoutRow> rows = [];
            RichDocument document;
            if (text.Length == 0)
            {
                document = RtfParser.Parse(page.Rtf);
            }
            else
            {
                page.Scanned = text;
                document = ManuscriptText.DocumentOf(text);
                rows = ManuscriptLayout.EvenRows(text, height);
            }

            if (pictures is not null)
            {
                page.Pictures = [.. pictures.Outlines.Select(StoredPicture.From)];
                page.Rtf = RtfWith(document, pictures, rows, width);
            }
            else if (text.Length > 0)
            {
                page.Rtf = RtfWriter.Write(document);
            }

            UpdatePageItem(index);
            if (index != _current)
                return;

            ShowText(page);
            View.Lines = [];
            if (pictures is not null)
                View.Pictures = [.. pictures.Outlines.Select(picture => picture.Outline)];
            ShowLinesInEditor();
        }

        /// <summary>
        /// Takes what the service read box by box into the page as a scan on this computer is taken in: each box with the text the service wrote for it is a line, so the text is laid out like the page
        /// (rows, tabs, indents, blank lines) and the page has its boxes. A service does not say how sure it is, so a line is orange when the service marked a word of it with [?], green otherwise.
        /// </summary>
        private void ApplyMarked(int index, IReadOnlyList<MarkedLine> marked, IReadOnlyList<PhotoLiker.Drawing.Ocr.TextRegion> regions, int width, int height, ScanLanguage language, PictureSearch? pictures)
        {
            var lines = marked.Select(line => new PhotoLiker.Drawing.Ocr.TextLine(line.Text, line.Text.Contains("[?]", StringComparison.Ordinal) ? 0.6f : 0.9f, regions[line.Box - 1])).ToList();
            ApplyScan(index, new PhotoLiker.Drawing.Ocr.ScanResult(lines), width, height, ScanKind.AiService, language, pictures);
        }

        /// <summary>Stands in for the finder of lines that marks the lines on the photo (a test, which has no model): the boxes of the lines of a page, in reading order.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<Bitmap32, CancellationToken, IReadOnlyList<PhotoLiker.Drawing.Ocr.TextRegion>>? FindLinesOverride { get; set; }

        /// <summary>
        /// The photo as the bytes of a JPEG file for the service: no more than <see cref="CloudReader.MaximumSide"/> on a side, on a white ground, and with the lines marked when they are given: a red
        /// box round each, with its number in white on red at its left. Runs on a thread of its own.
        /// </summary>
        internal static byte[] JpegForAi(Bitmap32 photo, IReadOnlyList<PhotoLiker.Drawing.Ocr.TextRegion>? marks = null)
        {
            var scale = Math.Min(1.0, (double)CloudReader.MaximumSide / Math.Max(photo.Width, photo.Height));
            var width = Math.Max(1, (int)Math.Round(photo.Width * scale));
            var height = Math.Max(1, (int)Math.Round(photo.Height * scale));

            using var full = new Bitmap(photo.Width, photo.Height, PixelFormat.Format32bppArgb);
            var data = full.LockBits(new Rectangle(0, 0, photo.Width, photo.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(photo.Bgra, 0, data.Scan0, photo.Bgra.Length);
            }
            finally
            {
                full.UnlockBits(data);
            }

            using var fitted = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(fitted))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(full, 0, 0, width, height);
                if (marks is { Count: > 0 })
                    DrawMarks(graphics, marks, (float)scale, width, height);
            }

            var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            using var stream = new MemoryStream();
            fitted.Save(stream, encoder, parameters);
            return stream.ToArray();
        }

        /// <summary>
        /// Draws the numbered boxes on the photo that goes to the service: a thin red outline round each line, and its number (from 1) in white on red at the left of the box, at the height of its top, or
        /// inside the photo when the box starts at its edge. Red stands out on paper, ruled or squared; the sizes follow the size of the photo so that they look the same on a small one and a large one.
        /// </summary>
        private static void DrawMarks(Graphics graphics, IReadOnlyList<PhotoLiker.Drawing.Ocr.TextRegion> marks, float scale, int width, int height)
        {
            var longSide = Math.Max(width, height);
            var red = Color.FromArgb(230, 25, 25);
            using var pen = new Pen(red, Math.Max(2f, longSide / 600f)) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            using var font = new Font("Arial", Math.Clamp(longSide / 50f, 13f, 32f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var back = new SolidBrush(red);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            for (var i = 0; i < marks.Count; i++)
            {
                var corners = marks[i].Corners.Select(corner => new PointF(corner.X * scale, corner.Y * scale)).ToArray();
                graphics.DrawPolygon(pen, corners);

                var label = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var size = graphics.MeasureString(label, font);
                var tag = new RectangleF(Math.Max(0f, corners[0].X - size.Width), Math.Clamp(corners[0].Y, 0f, Math.Max(0f, height - size.Height)), size.Width, size.Height);
                graphics.FillRectangle(back, tag);
                graphics.DrawString(label, font, Brushes.White, tag.Location);
            }
        }
    }
}
