using System.Net.Http;
using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The models of the features that edit or move a photo (<see cref="ToolModels"/>: the depth of a picture for the Parallax effect, the colours for Colorize) are not in the release; the first time
    /// a feature needs its model the person is asked whether to download it (what it is, how big, where it comes from), and it is downloaded to the models folder with its progress in the status bar.
    /// </summary>
    public partial class MainForm
    {
        private readonly EffectModels _effects = new();                         // the models of the depth and the colours (the search has its own, _models)
        private readonly object _toolModelGate = new();                    // guards the three below: never held while waiting for anything
        private readonly Dictionary<string, Task<bool>> _toolModelDownloads = [];
        private readonly HashSet<string> _toolModelsDeclined = [];
        private readonly HashSet<string> _toolModelsVerified = [];         // "id|size|time" of the files that were found to be the ones that were tested
        private readonly CancellationTokenSource _toolModelStop = new();

        /// <summary>Asked before a model is downloaded. A test answers it.</summary>
        protected virtual bool ConfirmToolModelDownload(string feature, ToolModel model) =>
            MessageBox.Show(this, string.Format(ToolModelStrings.DownloadQuestionFormat, feature, model.Title, model.Megabytes, model.Origin),
                ToolModelStrings.DownloadTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;

        /// <summary>
        /// True when the model of a feature is there, after asking and downloading it if it was not. <paramref name="askAgain"/> asks again even when the person has said no before during this run
        /// (a button they pressed themselves), which the start of a slideshow does not: it does without the model.
        /// </summary>
        private Task<bool> EnsureToolModelAsync(ToolModel model, string feature, bool askAgain)
        {
            // Registered before anything is asked: the question is a message box, which lets the window go on working, and whoever asks for the same model meanwhile (a second click, the start of
            // a slideshow) waits for this one's answer instead of asking again and downloading the same file to the same place
            TaskCompletionSource<bool> mine;
            lock (_toolModelGate)
            {
                if (_toolModelDownloads.TryGetValue(model.Id, out var running))
                    return running;

                mine = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _toolModelDownloads[model.Id] = mine.Task;
            }

            _ = EnsureToolModelCoreAsync(model, feature, askAgain, mine);
            return mine.Task;
        }

        private async Task EnsureToolModelCoreAsync(ToolModel model, string feature, bool askAgain, TaskCompletionSource<bool> answer)
        {
            try
            {
                if (model.IsPresentIn(_effects.ModelsFolder) && await IsTestedFileAsync(model))
                {
                    answer.SetResult(true);
                    return;
                }

                // Not there, or there but not the file that was tested (cut short, damaged, changed by something else): either way it is asked for and downloaded
                bool declinedBefore;
                lock (_toolModelGate)
                    declinedBefore = _toolModelsDeclined.Contains(model.Id);
                if (declinedBefore && !askAgain)
                {
                    answer.SetResult(false);
                    return;
                }

                answer.SetResult(await DownloadToolModelAsync(model, feature));
            }
            catch (Exception ex)
            {
                answer.TrySetException(ex);
            }
            finally
            {
                lock (_toolModelGate)
                    _toolModelDownloads.Remove(model.Id);
            }
        }

        /// <summary>
        /// Whether the file of the model that is on this computer is the one that was tested (its SHA-256, which takes about 0.2 s for 100 MB, off the window's thread). Looked at once for each file in a
        /// run (the same size and time as when it was found good), so the check costs nothing after the first.
        /// </summary>
        private async Task<bool> IsTestedFileAsync(ToolModel model)
        {
            var path = model.PathIn(_effects.ModelsFolder);
            try
            {
                var info = new FileInfo(path);
                var key = $"{model.Id}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                lock (_toolModelGate)
                {
                    if (_toolModelsVerified.Contains(key))
                        return true;
                }

                if (!await Task.Run(() => ModelFiles.HasSha256(path, model.Sha256)))
                    return false;

                lock (_toolModelGate)
                    _toolModelsVerified.Add(key);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false; // a file that can not be read is no good to anybody
            }
        }

        /// <summary>A video whose photos move by their depth: the frames are drawn here (see <see cref="ParallaxVideoPictures"/>) and given to ffmpeg, which encodes them with the audio as for any video. The cover does not move.</summary>
        private Task GenerateParallaxVideoAsync(VideoGenerationOptions options, VideoSequence sequence, Action<string> appendOutput, Action<int> reportProgress)
        {
            var still = options.CoverPath is null ? new HashSet<string>() : new HashSet<string>([options.CoverPath]);
            return VideoFrames.GenerateAsync(
                sequence, options.OutputPath, options.WidthPixels, options.HeightPixels, options.VideoCodec, options.Crf, options.AudioPath,
                new ParallaxVideoPictures(_effects, appendOutput), still, appendOutput, reportProgress);
        }

        private async Task<bool> DownloadToolModelAsync(ToolModel model, string feature)
        {
            if (!ConfirmToolModelDownload(feature, model))
            {
                lock (_toolModelGate)
                    _toolModelsDeclined.Add(model.Id);
                SetStatus(string.Format(ToolModelStrings.StatusDeclinedFormat, model.Title));
                return false;
            }

            lock (_toolModelGate)
                _toolModelsDeclined.Remove(model.Id);
            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    if (!IsDisposed)
                        SetStatus(string.Format(ToolModelStrings.StatusDownloadingFormat, model.Title, p.Done / 1_000_000, Math.Max(p.Total, model.Megabytes * 1_000_000L) / 1_000_000));
                });
                await _effects.EnsureDownloadedAsync(model, progress, _toolModelStop.Token);
                if (!IsDisposed)
                    SetStatus(string.Format(ToolModelStrings.StatusReadyFormat, model.Title));
                return true;
            }
            catch (OperationCanceledException)
            {
                return false; // the program is closing
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Whatever went wrong (the network, the disk, a file that is not the one that was tested, something not thought of): this is called from handlers that have nobody to catch an exception
                if (!IsDisposed)
                    SetStatus(string.Format(ToolModelStrings.StatusFailedFormat, model.Title, ex.Message));
                return false;
            }
        }
    }
}
