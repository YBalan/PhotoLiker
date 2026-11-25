using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Cloud;
using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLikerUI
{
    /// <summary>
    /// The AI button of the text box in the Scan manuscripts window: a language model that runs on this computer (<see cref="LlmCorrector"/>), or an AI service over the Internet the person gave a key
    /// for (<see cref="CloudCorrector"/>, see <see cref="AiServicesDialog"/>), corrects what the scan misread, word by word. It works on the
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
        /// Corrects the text with the AI (the button at the end of the buttons of the text box): the text that is selected, or all of it. The AI is the one picked in the AI box: an AI service
        /// that has a key (ChatGPT, Gemini, Claude; the text of the page is sent to it, one piece at a time), or the language model on this computer, which is asked for (and downloaded) when it
        /// is not here, and let go of when the work is done, as it holds more than a gigabyte of memory.
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

            // Who corrects: the AI service that is picked (its key is looked at now, so a change made later does not change this run), or the language model on this computer (which a test's stand-in replaces)
            var account = FixerOverride is null ? _ai.Active : null;

            // The end of the text of the pages before this one, when the person wants the service to go by it (it is sent too)
            var context = account is not null && UseContextCheck.Checked ? ManuscriptContext.Before(_pages, _current) : null;
            var source = new CancellationTokenSource();
            _scan = source;
            SetBusy(true);
            StopButton.Enabled = true;
            try
            {
                if (account is not null)
                {
                    SetStatus(string.Format(ManuscriptStrings.StatusFixAskingFormat, AiVendors.Name(account.Vendor)));
                }
                else
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
                }

                var progress = new Progress<(int Done, int Total)>(step =>
                {
                    if (IsDisposed)
                        return;

                    SetStatus(string.Format(ManuscriptStrings.StatusFixingFormat, Math.Min(step.Done + 1, step.Total), step.Total));
                    _progress.Maximum = Math.Max(1, step.Total);
                    _progress.Value = Math.Min(step.Done, _progress.Maximum);
                });

                var edits = await Task.Run(() => TextFixer.Fix(text, CorrectorFor(language, account, context), progress, source.Token), source.Token);
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
                or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or BadImageFormatException or OutOfMemoryException
                or AiServiceException or ArgumentException)
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

        /// <summary>The corrector for a language: the stand-in of a test, the AI service that is picked, or the language model (made when it is first needed). Runs on a thread of its own.</summary>
        private ITextCorrector CorrectorFor(ScanLanguage language, AiAccount? account, string? context)
        {
            if (_corrector is not null)
                return _corrector;

            if (FixerOverride is { } standIn)
                return _corrector = new StandInCorrector(standIn, language);

            if (account is not null)
                return _corrector = new CloudCorrector(new CloudChat(account.Vendor, account.ApiKey, account.Model, CloudClient), language, context);

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
