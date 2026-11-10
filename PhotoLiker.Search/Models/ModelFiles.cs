using System.IO.Compression;

namespace PhotoLiker.Search
{
    /// <summary>How much of a model download is done.</summary>
    public sealed record DownloadProgress(string Name, long Done, long Total);

    /// <summary>
    /// The models are not shipped with the app, they are downloaded the first time they are needed (a folder is indexed): the files of
    /// the model that matches words with photos (<see cref="ClipModels"/>, 0.6 to 1.5 GB each, only the ones that are chosen), and the
    /// "buffalo_l" pack of InsightFace (SCRFD face detector and ArcFace recognizer, about 190 MB once unpacked; free for non-commercial
    /// use only) for faces.
    /// </summary>
    public static class ModelFiles
    {
        private const string FacePackUrl = "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip";
        public const int FacePackMegabytes = 281; // the pack is bigger than the two models that are taken out of it

        private const string FaceFolderName = "insightface-buffalo_l";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(90) };

        public static string PathOf(string modelsFolder, ClipModelInfo model, ModelFile file) =>
            Path.Combine(modelsFolder, model.FolderName, file.Name);

        public static string FaceDetectorPath(string modelsFolder) => Path.Combine(modelsFolder, FaceFolderName, "det_10g.onnx");

        public static string FaceEmbedderPath(string modelsFolder) => Path.Combine(modelsFolder, FaceFolderName, "w600k_r50.onnx");

        public static bool ClipPresent(string modelsFolder, ClipModelInfo model) =>
            model.Files.All(file => File.Exists(PathOf(modelsFolder, model, file)));

        public static bool FacesPresent(string modelsFolder) =>
            File.Exists(FaceDetectorPath(modelsFolder)) && File.Exists(FaceEmbedderPath(modelsFolder));

        /// <summary>About how much has to be downloaded for these, in megabytes (0: nothing is missing).</summary>
        public static int MissingMegabytes(string modelsFolder, ClipModelInfo? model, bool faces) =>
            (model is not null && !ClipPresent(modelsFolder, model) ? model.Megabytes : 0)
            + (faces && !FacesPresent(modelsFolder) ? FacePackMegabytes : 0);

        /// <summary>Downloads whatever is missing of the files of <paramref name="model"/> (if given) and of the face models (if wanted).</summary>
        public static async Task EnsureAsync(string modelsFolder, ClipModelInfo? model, bool faces, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            if (model is not null)
            {
                foreach (var file in model.Files)
                    await DownloadAsync(file.Url, PathOf(modelsFolder, model, file), $"{model.Title} {file.Name}", progress, cancellationToken);
            }

            if (faces && !FacesPresent(modelsFolder))
                await DownloadFacePackAsync(modelsFolder, progress, cancellationToken);
        }

        private static async Task DownloadFacePackAsync(string modelsFolder, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            var folder = Path.GetDirectoryName(FaceDetectorPath(modelsFolder))!;
            var zip = Path.Combine(folder, "buffalo_l.zip");
            await DownloadAsync(FacePackUrl, zip, "buffalo_l.zip", progress, cancellationToken);

            try
            {
                using var archive = ZipFile.OpenRead(zip);
                Extract(archive, "det_10g.onnx", FaceDetectorPath(modelsFolder));
                Extract(archive, "w600k_r50.onnx", FaceEmbedderPath(modelsFolder));
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

        /// <summary>
        /// Downloads a file to <paramref name="destination"/> unless it is there already: to a file of the same name with ".part" added first, so that what is there is whole or not at all. It is the
        /// downloader of every model of the program, those of the search and those of the effects (which have their own list of files, in PhotoLiker.Effects). A file that is there is kept as it is
        /// when there is no hash to go by; with one it is looked at first, and a file that is not the one that was tested (cut short, damaged, changed by something else) is deleted and downloaded again.
        /// </summary>
        /// <param name="name">What the progress says is being downloaded.</param>
        /// <param name="sha256">The SHA-256 the file must have, in hex; one that has another is deleted and an <see cref="InvalidDataException"/> thrown. Null: not checked.</param>
        public static async Task DownloadAsync(
            string url, string destination, string name, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken, string? sha256 = null)
        {
            if (File.Exists(destination))
            {
                if (sha256 is null || await Task.Run(() => HasSha256(destination, sha256), cancellationToken))
                    return;

                File.Delete(destination); // not the file that was tested: it is not used, it is got again
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var partial = destination + ".part";

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

            if (sha256 is not null && !HasSha256(partial, sha256))
            {
                File.Delete(partial);
                throw new InvalidDataException($"The downloaded {name} is not the file that was tested (its SHA-256 differs).");
            }

            File.Move(partial, destination, overwrite: true);
        }

        /// <summary>Whether the file has this SHA-256 (hex, in either case). Reads all of it: about 0.2 s for 100 MB.</summary>
        /// <exception cref="IOException">The file can not be read.</exception>
        public static bool HasSha256(string file, string expected)
        {
            using var stream = File.OpenRead(file);
            return string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
