namespace PhotoLiker.Search
{
    /// <summary>How many threads the indexing uses. The photos are read, decoded and analyzed side by side.</summary>
    /// <param name="Decoders">Threads that decode photos.</param>
    /// <param name="FaceWorkers">Photos whose faces are looked for at the same time.</param>
    /// <param name="Clip">CPU threads of the photo model while faces are looked for at the same time.</param>
    /// <param name="Detector">CPU threads of the face detector, shared by the face workers.</param>
    /// <param name="Embedder">CPU threads of the face recognizer, shared by the face workers.</param>
    public sealed record ModelThreads(int Decoders, int FaceWorkers, int Clip, int Detector, int Embedder)
    {

        /// <summary>
        /// CPU threads of the photo model when it has the processor to itself (a run that only makes the vectors of
        /// another model; the faces are kept): the face threads are not busy then.
        /// </summary>
        public int ClipAlone { get; init; } = Math.Max(2, Environment.ProcessorCount / 3);

        /// <summary>A split that keeps the machine busy but leaves some of it to whatever else is running.</summary>
        public static ModelThreads ForThisMachine()
        {
            var cores = Environment.ProcessorCount;
            return new ModelThreads(
                Decoders: Math.Max(2, cores / 6),
                FaceWorkers: Math.Max(2, cores / 4),
                Clip: Math.Max(2, cores / 6),
                Detector: Math.Max(2, cores / 3),
                Embedder: Math.Max(2, cores / 3));
        }
    }

    /// <summary>
    /// Downloads the models and creates their ONNX sessions the first time they are needed. One hub serves every folder;
    /// loading a session takes a second or two, so the ones for searching are kept until the app closes (or until
    /// <see cref="ReleaseTextEncodersExcept"/> lets go of the ones of a model that is not in use any more: the text half of
    /// a model is up to 1.1 GB in memory).
    /// </summary>
    public sealed class ModelHub : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ClipTextEncoder> _textEncoders = [];

        private FaceDetector? _detector;
        private FaceEmbedder? _embedder;

        /// <param name="modelsFolder">Where the model files are (and are downloaded to); the default is %LocalAppData%\PhotoLiker\models.</param>
        /// <param name="threads">How many threads to use; the default leaves part of the processor to the rest of the computer.</param>
        public ModelHub(string? modelsFolder = null, ModelThreads? threads = null)
        {
            ModelsFolder = modelsFolder ?? SearchPaths.ModelsFolder;
            Threads = threads ?? ModelThreads.ForThisMachine();
        }

        public string ModelsFolder { get; }

        public ModelThreads Threads { get; }

        /// <summary>Whether the files of the model that matches words with photos are on this computer.</summary>
        public bool ClipDownloaded(ClipModelInfo model) => ModelFiles.ClipPresent(ModelsFolder, model);

        public bool FacesDownloaded => ModelFiles.FacesPresent(ModelsFolder);

        public bool IsDownloaded(ClipModelInfo model) => ClipDownloaded(model) && FacesDownloaded;

        /// <summary>About how many megabytes have to be downloaded to index with <paramref name="model"/> (0: everything is here).</summary>
        public int MissingMegabytes(ClipModelInfo model) => ModelFiles.MissingMegabytes(ModelsFolder, model, faces: true);

        /// <param name="model">The model that matches words with photos, if it is needed.</param>
        /// <param name="faces">Whether the face models are needed.</param>
        public Task EnsureDownloadedAsync(ClipModelInfo? model, bool faces, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken) =>
            ModelFiles.EnsureAsync(ModelsFolder, model, faces, progress, cancellationToken);

        // Every model is an ONNX Runtime session: first make sure that the runtime can start at all, see NativeRuntime

        /// <summary>
        /// The photo half of a model, for one indexing run: it is the caller's, who disposes it when the run is over (it is not kept,
        /// so that its memory is free again afterwards).
        /// </summary>
        public ClipImageEncoder CreateImageEncoder(ClipModelInfo model, int threads)
        {
            lock (_gate)
            {
                NativeRuntime.EnsureOnnxRuntimeCanLoad();
                return new ClipImageEncoder(ModelFiles.PathOf(ModelsFolder, model, model.Vision), threads, model);
            }
        }

        /// <summary>The text half of a model, kept for the next search.</summary>
        public ClipTextEncoder TextEncoder(ClipModelInfo model)
        {
            lock (_gate)
            {
                NativeRuntime.EnsureOnnxRuntimeCanLoad();
                if (!_textEncoders.TryGetValue(model.Id, out var encoder))
                {
                    string PathOf(ModelFile file) => ModelFiles.PathOf(ModelsFolder, model, file);
                    ITextTokenizer tokenizer = model.Tokens switch
                    {
                        TextTokens.SigLip => new SigLipTokenizer(PathOf(model.Tokenizer[0])),
                        TextTokens.XlmRoberta => new XlmRobertaTokenizer(PathOf(model.Tokenizer[0])),
                        _ => new ClipTokenizer(PathOf(model.Tokenizer[0]), PathOf(model.Tokenizer[1])),
                    };
                    _textEncoders[model.Id] = encoder = new ClipTextEncoder(PathOf(model.Text), tokenizer);
                }
                return encoder;
            }
        }

        /// <summary>Lets go of the text halves of all models but this one, e.g. when the user chose another model.</summary>
        public void ReleaseTextEncodersExcept(ClipModelInfo keep)
        {
            lock (_gate)
            {
                foreach (var id in _textEncoders.Keys.Where(id => id != keep.Id).ToList())
                {
                    _textEncoders[id].Dispose();
                    _textEncoders.Remove(id);
                }
            }
        }

        public FaceDetector Detector
        {
            get
            {
                lock (_gate)
                {
                    NativeRuntime.EnsureOnnxRuntimeCanLoad();
                    return _detector ??= new FaceDetector(ModelFiles.FaceDetectorPath(ModelsFolder), Threads.Detector);
                }
            }
        }

        public FaceEmbedder Embedder
        {
            get
            {
                lock (_gate)
                {
                    NativeRuntime.EnsureOnnxRuntimeCanLoad();
                    return _embedder ??= new FaceEmbedder(ModelFiles.FaceEmbedderPath(ModelsFolder), Threads.Embedder);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var encoder in _textEncoders.Values)
                    encoder.Dispose();
                _textEncoders.Clear();
                _detector?.Dispose();
                _embedder?.Dispose();
                _detector = null;
                _embedder = null;
            }
        }
    }
}
