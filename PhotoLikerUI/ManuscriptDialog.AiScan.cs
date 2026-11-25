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
    /// (<see cref="CloudReader"/>). The text is put into the page like the text of a scan, the pictures of the page are looked for as they are after any scan, but the page has no boxes on its
    /// photo: a service gives text, not places. The person is asked once for each service, in each window, before a photo leaves the computer.
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

                    var (jpeg, pictures, width, height) = await Task.Run(() =>
                    {
                        var decoded = WicPhotoDecoder.DecodeFull(File.ReadAllBytes(page.File!));
                        var bitmap = new Bitmap32(decoded.Width, decoded.Height, decoded.Bgra);
                        return (Jpeg: JpegForAi(bitmap), Pictures: findPictures ? TryFindPictures(bitmap, source.Token) : null, bitmap.Width, bitmap.Height);
                    }, source.Token);

                    if (IsDisposed)
                        return;

                    var text = await new CloudReader(chat, language, context).ReadAsync(jpeg, source.Token);
                    if (IsDisposed)
                        return;

                    ApplyAiText(index, text, width, height, language, pictures);
                    lastReading = text;
                    pictureCount += pictures?.Cut.Count ?? 0;
                    read++;
                    if (text.Length == 0)
                        empty++;
                    _progress.Value = Math.Min(_progress.Maximum, read);
                }

                string done;
                if (indexes.Count == 1)
                {
                    done = lastReading.Length > 0
                        ? string.Format(ManuscriptStrings.StatusAiReadFormat, indexes[0] + 1, service, ManuscriptText.LineSpans(lastReading).Count)
                        : string.Format(ManuscriptStrings.StatusAiReadNothingFormat, indexes[0] + 1, service);
                }
                else
                {
                    done = string.Format(ManuscriptStrings.StatusAiScanAllDoneFormat, read, service, empty);
                }

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
        /// boxes (a service says what is written, not where), so the lines it had are gone; a reading that found no text leaves the text of the page as it is.
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

        /// <summary>The photo as the bytes of a JPEG file for the service: no more than <see cref="CloudReader.MaximumSide"/> on a side, on a white ground. Runs on a thread of its own.</summary>
        internal static byte[] JpegForAi(Bitmap32 photo)
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
            }

            var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            using var stream = new MemoryStream();
            fitted.Save(stream, encoder, parameters);
            return stream.ToArray();
        }
    }
}
