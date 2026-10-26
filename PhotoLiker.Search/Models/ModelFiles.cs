using System.IO.Compression;

namespace PhotoLiker.Search
{
    /// <summary>How much of a model download is done.</summary>
    public sealed record DownloadProgress(string Name, long Done, long Total);

    /// <summary>Where the model files are on disk once they have been downloaded.</summary>
    public sealed record ModelPaths(
        string ClipVision, string ClipText, string ClipVocab, string ClipMerges,
        string FaceDetector, string FaceEmbedder);

    /// <summary>
    /// The models are not shipped with the app (together they are about 650 MB), they are downloaded the first time
    /// a folder is indexed:
    /// CLIP ViT-B/32 (OpenAI, MIT licence, ONNX export by Xenova) for text search, and the "buffalo_l" pack of InsightFace
    /// (SCRFD face detector and ArcFace recognizer; free for non-commercial use only) for faces.
    /// </summary>
    public static class ModelFiles
    {
        private const string ClipRepository = "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/";
        private const string FacePackUrl = "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip";

        private const string ClipFolderName = "clip-vit-b32";
        private const string FaceFolderName = "insightface-buffalo_l";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(90) };

        public static ModelPaths PathsIn(string modelsFolder) => new(
            Path.Combine(modelsFolder, ClipFolderName, "vision_model.onnx"),
            Path.Combine(modelsFolder, ClipFolderName, "text_model.onnx"),
            Path.Combine(modelsFolder, ClipFolderName, "vocab.json"),
            Path.Combine(modelsFolder, ClipFolderName, "merges.txt"),
            Path.Combine(modelsFolder, FaceFolderName, "det_10g.onnx"),
            Path.Combine(modelsFolder, FaceFolderName, "w600k_r50.onnx"));

        public static bool ClipPresent(ModelPaths paths) =>
            File.Exists(paths.ClipVision) && File.Exists(paths.ClipText) && File.Exists(paths.ClipVocab) && File.Exists(paths.ClipMerges);

        public static bool FacesPresent(ModelPaths paths) => File.Exists(paths.FaceDetector) && File.Exists(paths.FaceEmbedder);

        /// <summary>Downloads whatever of the models is missing.</summary>
        public static async Task EnsureAsync(ModelPaths paths, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            if (!ClipPresent(paths))
            {
                await DownloadAsync(ClipRepository + "onnx/vision_model.onnx", paths.ClipVision, progress, cancellationToken);
                await DownloadAsync(ClipRepository + "onnx/text_model.onnx", paths.ClipText, progress, cancellationToken);
                await DownloadAsync(ClipRepository + "vocab.json", paths.ClipVocab, progress, cancellationToken);
                await DownloadAsync(ClipRepository + "merges.txt", paths.ClipMerges, progress, cancellationToken);
            }

            if (!FacesPresent(paths))
                await DownloadFacePackAsync(paths, progress, cancellationToken);
        }

        private static async Task DownloadFacePackAsync(ModelPaths paths, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            var folder = Path.GetDirectoryName(paths.FaceDetector)!;
            var zip = Path.Combine(folder, "buffalo_l.zip");
            await DownloadAsync(FacePackUrl, zip, progress, cancellationToken);

            try
            {
                using var archive = ZipFile.OpenRead(zip);
                Extract(archive, "det_10g.onnx", paths.FaceDetector);
                Extract(archive, "w600k_r50.onnx", paths.FaceEmbedder);
            }
            finally
            {
                File.Delete(zip); // the pack also holds models that are not used
            }
        }

        private static void Extract(ZipArchive archive, string name, string destination)
        {
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"The face model pack has no '{name}'.");

            var partial = destination + ".part";
            entry.ExtractToFile(partial, overwrite: true);
            File.Move(partial, destination, overwrite: true);
        }

        private static async Task DownloadAsync(string url, string destination, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            if (File.Exists(destination))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var partial = destination + ".part";
            var name = Path.GetFileName(destination);

            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(partial);

                var buffer = new byte[1 << 20];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    progress?.Report(new DownloadProgress(name, done, total));
                }

                if (total > 0 && done != total)
                    throw new IOException($"Download of {name} ended early ({done} of {total} bytes).");
            }

            File.Move(partial, destination, overwrite: true);
        }
    }
}
