namespace PhotoLiker.Search
{
    /// <summary>How many threads the indexing uses. The photos are read, decoded and analyzed side by side.</summary>
    /// <param name="Decoders">Threads that decode photos.</param>
    /// <param name="FaceWorkers">Photos whose faces are looked for at the same time.</param>
    /// <param name="Clip">CPU threads of the CLIP model.</param>
    /// <param name="Detector">CPU threads of the face detector, shared by the face workers.</param>
    /// <param name="Embedder">CPU threads of the face recognizer, shared by the face workers.</param>
    public sealed record ModelThreads(int Decoders, int FaceWorkers, int Clip, int Detector, int Embedder)
    {
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
    /// loading a session takes a second or two, so it is kept until the app closes.
    /// </summary>
    public sealed class ModelHub : IDisposable
    {
        private readonly object _gate = new();

        private ClipImageEncoder? _imageEncoder;
        private ClipTextEncoder? _textEncoder;
        private FaceDetector? _detector;
        private FaceEmbedder? _embedder;

        /// <param name="modelsFolder">Where the model files are (and are downloaded to); the default is %LocalAppData%\PhotoLiker\models.</param>
        /// <param name="threads">How many threads to use; the default leaves part of the processor to the rest of the computer.</param>
        public ModelHub(string? modelsFolder = null, ModelThreads? threads = null)
        {
            Paths = ModelFiles.PathsIn(modelsFolder ?? SearchPaths.ModelsFolder);
            Threads = threads ?? ModelThreads.ForThisMachine();
        }

        public ModelPaths Paths { get; }

        public ModelThreads Threads { get; }

        public bool ClipDownloaded => ModelFiles.ClipPresent(Paths);

        public bool IsDownloaded => ClipDownloaded && ModelFiles.FacesPresent(Paths);

        public Task EnsureDownloadedAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken) =>
            ModelFiles.EnsureAsync(Paths, progress, cancellationToken);

        // Every model is an ONNX Runtime session: first make sure that the runtime can start at all, see NativeRuntime
        public ClipImageEncoder ImageEncoder
        {
            get
            {
                lock (_gate)
                {
                    NativeRuntime.EnsureOnnxRuntimeCanLoad();
                    return _imageEncoder ??= new ClipImageEncoder(Paths.ClipVision, Threads.Clip);
                }
            }
        }

        public ClipTextEncoder TextEncoder
        {
            get
            {
                lock (_gate)
                {
                    NativeRuntime.EnsureOnnxRuntimeCanLoad();
                    return _textEncoder ??= new ClipTextEncoder(Paths.ClipText, new ClipTokenizer(Paths.ClipVocab, Paths.ClipMerges));
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
                    return _detector ??= new FaceDetector(Paths.FaceDetector, Threads.Detector);
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
                    return _embedder ??= new FaceEmbedder(Paths.FaceEmbedder, Threads.Embedder);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _imageEncoder?.Dispose();
                _textEncoder?.Dispose();
                _detector?.Dispose();
                _embedder?.Dispose();
                _imageEncoder = null;
                _textEncoder = null;
                _detector = null;
                _embedder = null;
            }
        }
    }
}
