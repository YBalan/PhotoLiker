using PhotoLiker.Search;

namespace PhotoLiker.Effects.Models
{
    /// <summary>
    /// A model that is one file, downloaded as it is the first time its feature is used, and checked against the SHA-256 it was tested with
    /// (the files come from repositories that are not the model's authors', at a fixed revision, so that neither a change of the repository nor
    /// a damaged download can reach the program).
    /// </summary>
    public sealed record ToolModel
    {
        /// <summary>Short and stable.</summary>
        public required string Id { get; init; }

        /// <summary>The name shown to the user.</summary>
        public required string Title { get; init; }

        /// <summary>The folder of the file below the models folder.</summary>
        public required string FolderName { get; init; }

        public required ModelFile File { get; init; }

        /// <summary>The SHA-256 of the file, in lower case hex.</summary>
        public required string Sha256 { get; init; }

        /// <summary>Where the weights come from, and under which licence, for the About box and the README.</summary>
        public required string Origin { get; init; }

        public int Megabytes => File.Megabytes;

        /// <summary>Where the file of the model is (or is to be) below the models folder.</summary>
        public string PathIn(string modelsFolder) => System.IO.Path.Combine(modelsFolder, FolderName, File.Name);

        /// <summary>Whether the file of the model is below the models folder.</summary>
        public bool IsPresentIn(string modelsFolder) => System.IO.File.Exists(PathIn(modelsFolder));
    }

    /// <summary>The models of the features that edit or move a photo: the depth of a picture (the parallax effect) and its colours (Colorize).</summary>
    public static class ToolModels
    {
        /// <summary>
        /// Depth Anything V2 Small (Apache 2.0, 25 million weights): one number per pixel for how near the thing in it is. The float32 file is the one
        /// used, although it is four times the size of the 8 bit one: on a processor it runs twice as fast as that (0.5 s against 1.2 s for a photo).
        /// ONNX export of the onnx-community repository (transformers.js).
        /// </summary>
        public static ToolModel Depth { get; } = new()
        {
            Id = "depth-anything-v2-small",
            Title = "Depth Anything V2 Small",
            FolderName = "depth-anything-v2-small",
            File = new ModelFile(
                "model.onnx",
                "https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/4472b7362082ad9968fee890ca0f1e5aca36b93d/onnx/model.onnx",
                99),
            Sha256 = "afb6a5c28f3b6bf1618c6e43f02073ef9dfdc70e937502d51603e57b0a1df10c",
            Origin = "Depth Anything V2 Small by the University of Hong Kong and TikTok, Apache 2.0",
        };

        /// <summary>
        /// DDColor tiny (Apache 2.0): the colours of a black and white photo, from its lightness only, 512 by 512. The weights are float16 with float32
        /// input and output; export by the edgetools repository from the authors' <c>ddcolor_paper_tiny</c> checkpoint.
        /// </summary>
        public static ToolModel Colorizer { get; } = new()
        {
            Id = "ddcolor-tiny",
            Title = "DDColor tiny",
            FolderName = "ddcolor-tiny",
            File = new ModelFile(
                "ddcolor-tiny-fp16.onnx",
                "https://huggingface.co/edgetools/ddcolor/resolve/4755ae9f1f7a35a9e7693b96c2a88f3432cb6ab0/ddcolor-tiny-fp16.onnx",
                135),
            Sha256 = "2653da00dc15e54a45e5200b61dbf82ee9ceaf56b02bb9b9657569ac775e82e6",
            Origin = "DDColor tiny (paper) by the DAMO Academy (Alibaba), Apache 2.0",
        };
    }
}
