using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLikerUI
{
    /// <summary>
    /// The AI button of the text box in the Scan manuscripts window: a language model that runs on this computer (<see cref="LlmCorrector"/>) corrects what the scan misread, word by word. It works on the
    /// text that is selected, or on all the text of the page; what it gives back is taken word by word (<see cref="TextFixer"/>) and put into the text box, which keeps the lines, the tabs, the pictures and the
    /// look of the words, and the changes can be taken back with Ctrl+Z.
    /// </summary>
    internal sealed partial class ManuscriptDialog
    {
        private ITextCorrector? _corrector;

        /// <summary>Whether the corrector (the language model, more than a gigabyte of memory) is held: only while it works (for the tests).</summary>
        internal bool HasCorrector => _corrector is not null;

        /// <summary>Stands in for the language model (a test, which has no model): a piece of the text and its language go in, the corrected piece comes out.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<string, ScanLanguage, CancellationToken, string>? FixerOverride { get; set; }

        /// <summary>
        /// Corrects the text with the AI (the button at the end of the buttons of the text box): the text that is selected, or all of it. The model is asked for (and downloaded) when it is not on this
        /// computer; it is let go of when the work is done, as it holds more than a gigabyte of memory.
        /// </summary>
        internal async Task FixTextAsync()
        {
            if (_busy || CurrentPage is null)
                return;

            // What is to be corrected and where it is: the selection, or all the text (the places of the changes are the places of the text box)
            var box = Editor.Box;
            var whole = box.Text;
            var start = box.SelectionLength > 0 ? Math.Min(box.SelectionStart, whole.Length) : 0;
            var text = box.SelectionLength > 0 ? whole.Substring(start, Math.Min(box.SelectionLength, whole.Length - start)) : whole;
            if (!text.Any(char.IsLetter))
            {
                SetStatus(ManuscriptStrings.StatusFixNoText);
                return;
            }

            var language = Language;
            var source = new CancellationTokenSource();
            _scan = source;
            SetBusy(true);
            StopButton.Enabled = true;
            try
            {
                // Said first: looking at a file of 1.8 GB (its SHA-256) takes some seconds, and the download of it minutes, the main window says how far it is (see ShowModelStatus)
                SetStatus(ManuscriptStrings.StatusFixPreparing);
                if (FixerOverride is null && _ensureFixAsync is not null && !await _ensureFixAsync())
                {
                    if (!IsDisposed)
                        SetStatus(ManuscriptStrings.StatusFixDeclined);
                    return;
                }

                if (IsDisposed)
                    return;

                SetStatus(ManuscriptStrings.StatusFixPreparing);
                var progress = new Progress<(int Done, int Total)>(step =>
                {
                    if (IsDisposed)
                        return;

                    SetStatus(string.Format(ManuscriptStrings.StatusFixingFormat, Math.Min(step.Done + 1, step.Total), step.Total));
                    _progress.Maximum = Math.Max(1, step.Total);
                    _progress.Value = Math.Min(step.Done, _progress.Maximum);
                });

                var edits = await Task.Run(() => TextFixer.Fix(text, CorrectorFor(language), progress, source.Token), source.Token);
                if (IsDisposed)
                    return;

                if (edits.Count == 0)
                {
                    SetStatus(ManuscriptStrings.StatusFixNothing);
                    return;
                }

                Editor.ApplyEdits(edits.Select(edit => edit with { Index = edit.Index + start }));
                SetStatus(string.Format(ManuscriptStrings.StatusFixedFormat, edits.Count));
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    SetStatus(ManuscriptStrings.StatusFixStopped);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or FileNotFoundException or UnauthorizedAccessException or InvalidOperationException
                or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or BadImageFormatException or OutOfMemoryException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(ManuscriptStrings.StatusFixFailedFormat, ex.Message));
            }
            finally
            {
                _scan = null;
                source.Dispose();
                ReleaseCorrector();
                if (!IsDisposed)
                {
                    SetBusy(false);
                    SaveWork();
                }
            }
        }

        /// <summary>The corrector for a language: the stand-in of a test, or the language model (made when it is first needed). Runs on a thread of its own.</summary>
        private ITextCorrector CorrectorFor(ScanLanguage language)
        {
            if (_corrector is not null)
                return _corrector;

            if (FixerOverride is { } standIn)
                return _corrector = new StandInCorrector(standIn, language);

            var folder = _models.ModelsFolder;
            return _corrector = new LlmCorrector(ToolModels.TextFixer.PathIn(folder), ToolModels.TextFixerTokenizer.PathIn(folder), language);
        }

        /// <summary>Lets go of the corrector (and the memory of the model).</summary>
        private void ReleaseCorrector()
        {
            (_corrector as IDisposable)?.Dispose();
            _corrector = null;
        }

        private sealed class StandInCorrector(Func<string, ScanLanguage, CancellationToken, string> correct, ScanLanguage language) : ITextCorrector
        {
            public string Correct(string text, CancellationToken cancel) => correct(text, language, cancel);
        }
    }
}
