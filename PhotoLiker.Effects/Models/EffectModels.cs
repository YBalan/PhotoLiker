using PhotoLiker.Core;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Models
{
    /// <summary>
    /// The models of the effects (<see cref="ToolModels"/>): where their files are, downloading them (the first time a feature needs one), and the ONNX sessions made from them. One object serves the
    /// whole program: the depth model is made when it is first asked for and kept (a session is safe to use from several threads at once, and the slideshow and the video ask it from many); the
    /// colorizing model is made for one job at a time, by whoever runs it, and let go of afterwards, since it takes 270 MB of memory while it is open.
    /// </summary>
    public sealed class EffectModels : IDisposable
    {
        private readonly object _gate = new();
        private DepthEstimator? _depth;

        /// <param name="modelsFolder">Where the model files are, and are downloaded to; the default is %LocalAppData%\PhotoLiker\models, where the models of the search are.</param>
        /// <param name="threads">How many CPU threads a model uses; the default is a third of the processor, so that the rest of the program keeps its share.</param>
        public EffectModels(string? modelsFolder = null, int? threads = null)
        {
            ModelsFolder = modelsFolder ?? Path.Combine(AppPaths.DataFolder, "models");
            Threads = threads ?? Math.Max(2, Environment.ProcessorCount / 3);
        }

        public string ModelsFolder { get; }

        public int Threads { get; }

        public bool DepthDownloaded => ToolModels.Depth.IsPresentIn(ModelsFolder);

        public bool ColorizerDownloaded => ToolModels.Colorizer.IsPresentIn(ModelsFolder);

        /// <summary>Downloads the file of <paramref name="model"/> if it is not there, and checks it against its SHA-256 (see <see cref="ToolModel"/>).</summary>
        public Task EnsureDownloadedAsync(ToolModel model, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken) =>
            ModelFiles.DownloadAsync(model.File.Url, model.PathIn(ModelsFolder), model.Title, progress, cancellationToken, model.Sha256);

        /// <summary>The model that tells how near things in a picture are, made when it is first asked for and kept. The file must be there.</summary>
        /// <exception cref="FileNotFoundException">The file of the model is not there.</exception>
        public DepthEstimator Depth
        {
            get
            {
                lock (_gate)
                {
                    NativeRuntime.EnsureOnnxRuntimeCanLoad();
                    return _depth ??= new DepthEstimator(ToolModels.Depth.PathIn(ModelsFolder), Threads);
                }
            }
        }

        /// <summary>The model that gives colours to a black and white photo, for one job: it is the caller's, who disposes it when the job is over. The file must be there.</summary>
        /// <param name="threads">How many CPU threads it uses; 0 for the default of this object.</param>
        /// <exception cref="FileNotFoundException">The file of the model is not there.</exception>
        public Colorizer CreateColorizer(int threads = 0)
        {
            lock (_gate)
            {
                NativeRuntime.EnsureOnnxRuntimeCanLoad();
                return new Colorizer(ToolModels.Colorizer.PathIn(ModelsFolder), threads > 0 ? threads : Threads);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _depth?.Dispose();
                _depth = null;
            }
        }
    }
}
