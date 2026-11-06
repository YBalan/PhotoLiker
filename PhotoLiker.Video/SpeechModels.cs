using PhotoLiker.Search;
using Whisper.net.Ggml;

namespace PhotoLiker.Video
{
    /// <summary>A Whisper model that turns speech into text: how big it is, and how good against how slow (measured on a 6-core desktop processor: small is 3-4 times faster than the speech it reads, base 5-11 times).</summary>
    public sealed record SpeechModelInfo(string Id, string Title, GgmlType Type, int Megabytes, string Description);

    /// <summary>The speech models the program knows, where their files are kept, and getting them.</summary>
    public static class SpeechModels
    {
        public static SpeechModelInfo Tiny { get; } = new("tiny", "Whisper tiny", GgmlType.Tiny, 75, "The fastest and the roughest: for a clear voice and a quick look.");
        public static SpeechModelInfo Base { get; } = new("base", "Whisper base", GgmlType.Base, 142, "Fast, readable for a clear voice, makes many mistakes with children and with noise.");
        public static SpeechModelInfo Small { get; } = new("small", "Whisper small", GgmlType.Small, 465, "Slower, much better with speech that is not clear (Russian, Ukrainian): the one to use.");

        public static IReadOnlyList<SpeechModelInfo> All { get; } = [Tiny, Base, Small];

        public static SpeechModelInfo Default => Small;

        /// <summary>The model with this id; the default one for an id that is not known (a file written by a newer version, a typo).</summary>
        public static SpeechModelInfo Find(string? id) => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

        /// <summary>Where the file of a model is kept: in the models folder, next to the other models.</summary>
        public static string PathOf(string modelsFolder, SpeechModelInfo model) => Path.Combine(modelsFolder, "whisper", $"ggml-{model.Id}.bin");

        public static bool IsPresent(string modelsFolder, SpeechModelInfo model) =>
            File.Exists(PathOf(modelsFolder, model)) && new FileInfo(PathOf(modelsFolder, model)).Length > 1 << 20;

        /// <summary>
        /// Downloads the file of a model (from the place Whisper.net knows: the whisper.cpp models on Hugging Face) into the models folder, as a
        /// file with .part in its name until it is whole, so that a download that was stopped never looks like a model. Says how far it is as it goes.
        /// </summary>
        public static async Task DownloadAsync(string modelsFolder, SpeechModelInfo model, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            var path = PathOf(modelsFolder, model);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var partial = path + ".part";
            var total = (long)model.Megabytes << 20;

            try
            {
                await using var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(model.Type, QuantizationType.NoQuantization, cancellationToken);
                await using (var target = File.Create(partial))
                {
                    var buffer = new byte[1 << 20];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        done += read;
                        progress?.Report(new DownloadProgress(model.Title, done, Math.Max(total, done)));
                    }
                }
                File.Move(partial, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(partial); } catch (IOException) { /* it is only a part */ }
                throw;
            }
        }
    }
}
