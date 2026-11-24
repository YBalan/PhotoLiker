using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PhotoLiker.Drawing;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.PhotoBook;
using TextLine = PhotoLiker.Drawing.Ocr.TextLine;

namespace PhotoLikerUI
{
    /// <summary>
    /// The pictures of the pages in the Scan manuscripts window: finding them (the model that cuts out the Telegram stickers draws a contour round each, see <see cref="PictureFinder"/>), cutting them
    /// out along those outlines, and putting them into the text of the page, in the Rich Text Box, where they are between its lines on the page (see <see cref="PicturePlacement"/>). They are
    /// text there: they can be deleted or moved like it, and the book has them where they stand. The outlines are kept too, to show where each picture is on the photo.
    /// </summary>
    internal sealed partial class ManuscriptDialog
    {
        /// <summary>A picture goes into the text no larger than this many pixels on its long side (it is kept with the text, in the file of the folder).</summary>
        internal const int PictureMaximumSide = 1000;

        /// <summary>A picture that was found: its outline on the photo, and the picture cut out along it as the bytes of a PNG file (the background see-through).</summary>
        private sealed record FoundPicture(PagePicture Outline, byte[] Png);

        /// <summary>What a search of a page gave: the outlines of the pictures, the pictures cut out, and the size of the photo the outlines are in the pixels of.</summary>
        private sealed record PictureSearch(IReadOnlyList<PagePicture> Outlines, IReadOnlyList<FoundPicture> Cut, int Width, int Height);

        /// <summary>Whether the person said no to the download of the models that find pictures: a scan does not ask again while the window is open (the Find pictures button does).</summary>
        private bool _picturesDeclined;

        /// <summary>Stands in for the finder of pictures (a test, which has no models): the page goes in, the pictures come out.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<Bitmap32, CancellationToken, IReadOnlyList<PagePicture>>? PicturesOverride { get; set; }

        /// <summary>
        /// Makes sure the models that find the pictures are on this computer, asking for them when they are not; false when the person said no (that is remembered, see
        /// <see cref="_picturesDeclined"/>).
        /// </summary>
        private async Task<bool> EnsurePictureModelsAsync()
        {
            if (PicturesOverride is not null || _ensurePicturesAsync is null)
                return true;

            if (await _ensurePicturesAsync())
                return true;

            _picturesDeclined = true;
            return false;
        }

        // ── Finding ─────────────────────────────────────────────────────────

        /// <summary>The Find pictures button: looks for the pictures on the page that is open, asking for the models if they are not on this computer, and puts them into its text.</summary>
        internal async Task FindPicturesAsync()
        {
            if (_busy || CurrentPage is not { } page || !File.Exists(page.File))
                return;

            FlushText();
            _picturesDeclined = false; // asked for by name: the person is asked again
            var index = _current;
            var source = new CancellationTokenSource();
            _scan = source;
            SetBusy(true);
            StopButton.Enabled = true;
            try
            {
                if (!await EnsurePictureModelsAsync())
                {
                    if (!IsDisposed)
                        SetStatus(ManuscriptStrings.StatusPicturesDeclined);
                    return;
                }

                if (IsDisposed)
                    return;

                SetStatus(string.Format(ManuscriptStrings.StatusFindingPicturesFormat, index + 1));
                var found = await Task.Run(() => FindOnDisk(page.File!, source.Token), source.Token);
                if (IsDisposed)
                    return;

                ApplyPictures(index, found);
                SetStatus(found.Cut.Count == 0
                    ? string.Format(ManuscriptStrings.StatusNoPicturesFormat, index + 1)
                    : string.Format(ManuscriptStrings.StatusPicturesFoundFormat, index + 1, found.Cut.Count));
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    SetStatus(ManuscriptStrings.StatusScanStopped);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or FileNotFoundException or UnauthorizedAccessException or InvalidOperationException
                or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or BadImageFormatException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(ManuscriptStrings.StatusPicturesFailedFormat, ex.Message));
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

        /// <summary>The pictures on the photo in a file. Runs on a thread of its own.</summary>
        private PictureSearch FindOnDisk(string file, CancellationToken cancel)
        {
            var decoded = WicPhotoDecoder.DecodeFull(File.ReadAllBytes(file));
            var bitmap = new Bitmap32(decoded.Width, decoded.Height, decoded.Bgra);
            return CutOut(bitmap, RunPictureFinder(bitmap, cancel));
        }

        /// <summary>
        /// The pictures on a page that has just been decoded for a scan, cut out; null when the search failed: a failure of that search does not take the text of the page with it. Runs on a thread of
        /// its own.
        /// </summary>
        private PictureSearch? TryFindPictures(Bitmap32 bitmap, CancellationToken cancel)
        {
            try
            {
                return CutOut(bitmap, RunPictureFinder(bitmap, cancel));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or BadImageFormatException)
            {
                return null;
            }
        }

        private IReadOnlyList<PagePicture> RunPictureFinder(Bitmap32 bitmap, CancellationToken cancel)
        {
            if (PicturesOverride is { } standIn)
                return standIn(bitmap, cancel);

            if (_pictureFinder is null)
            {
                SetStatusFromAnyThread(ManuscriptStrings.StatusPreparingPictureModel);
                _pictureFinder = new PictureFinder(ToolModels.Cutout.PathIn(_models.ModelsFolder), ToolModels.OcrFinder.PathIn(_models.ModelsFolder), _models.Threads);
            }

            return _pictureFinder.Find(bitmap, cancel);
        }

        /// <summary>Each picture cut out of the photo along its outline, as a PNG; one that cannot be cut out (it is too small) is not a picture of the text.</summary>
        private static PictureSearch CutOut(Bitmap32 photo, IReadOnlyList<PagePicture> outlines)
        {
            var cut = new List<FoundPicture>();
            foreach (var outline in outlines)
            {
                if (PictureCutter.Cut(photo, outline.Outline, PictureMaximumSide) is { } picture)
                    cut.Add(new FoundPicture(outline, PngBytes(picture)));
            }

            return new PictureSearch(outlines, cut, photo.Width, photo.Height);
        }

        /// <summary>Gives the memory of the model back (the model of the stickers takes about a gigabyte while it works).</summary>
        private void ReleasePictureFinder()
        {
            _pictureFinder?.Dispose();
            _pictureFinder = null;
        }

        // ── In the text ─────────────────────────────────────────────────────

        /// <summary>Keeps the pictures that were found on a page, puts them into its text, and shows their outlines when it is the page that is open.</summary>
        private void ApplyPictures(int index, PictureSearch found)
        {
            var page = _pages[index];
            page.Pictures = [.. found.Outlines.Select(StoredPicture.From)];
            page.Width = found.Width;
            page.Height = found.Height;

            var rows = RowsOf(page);
            page.Rtf = RtfWith(RtfParser.Parse(page.Rtf), found, rows, found.Width);
            UpdatePageItem(index);

            if (index == _current)
            {
                ShowText(page);
                View.Pictures = [.. found.Outlines.Select(picture => picture.Outline)];
            }

            ScheduleSave();
        }

        /// <summary>The text (as RTF) with the pictures put in it, where they are among the rows; null for a text that is nothing and has no picture.</summary>
        private static string? RtfWith(RichDocument text, PictureSearch found, IReadOnlyList<LayoutRow> rows, int photoWidth)
        {
            // From the top of the page to the bottom (those side by side, from the left): the order they come in the text when no row is between them
            var pictures = found.Cut
                .OrderBy(picture => picture.Outline.Centre.Y).ThenBy(picture => picture.Outline.Centre.X)
                .Select(picture => (
                    Picture: RichParagraph.OfPicture(RichPicture.FromFile(picture.Png, PicturePlacement.ShareOf(picture.Outline, photoWidth))!, PicturePlacement.AlignOf(picture.Outline, photoWidth)),
                    Row: PicturePlacement.RowBefore(picture.Outline, rows)))
                .ToList();

            var document = PicturePlacement.Insert(text, pictures, rows.Count);
            return document.Paragraphs.Count == 0 || (document.IsEmpty && !document.HasPictures) ? null : RtfWriter.Write(document);
        }

        /// <summary>The rows of the text of a page, from the lines its scan found (none when it has not been scanned).</summary>
        private static IReadOnlyList<LayoutRow> RowsOf(ManuscriptPage page) =>
            ManuscriptLayout.Rows([.. (page.Lines ?? []).Select(line => line.ToLine()).OfType<TextLine>()], page.Width ?? 0);

        /// <summary>The outlines of the pictures of a page, unless the photo is no longer the shape it was when they were found.</summary>
        private static IReadOnlyList<PointF[]> PicturesOf(ManuscriptPage page, int shownWidth, int shownHeight)
        {
            if (page.Pictures is not { Count: > 0 } || page.Width is not > 0 || page.Height is not > 0)
                return [];

            var photoAspect = (double)page.Width.Value / page.Height.Value;
            var shownAspect = (double)shownWidth / shownHeight;
            if (Math.Abs(photoAspect - shownAspect) / photoAspect > 0.02)
                return [];

            return [.. page.Pictures.Select(picture => picture.ToPicture()).OfType<PagePicture>().Select(picture => picture.Outline)];
        }

        /// <summary>A picture as the bytes of a PNG file (its see-through background is kept).</summary>
        private static byte[] PngBytes(Bitmap32 picture)
        {
            using var bitmap = new Bitmap(picture.Width, picture.Height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, picture.Width, picture.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(picture.Bgra, 0, data.Scan0, picture.Bgra.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }
}
