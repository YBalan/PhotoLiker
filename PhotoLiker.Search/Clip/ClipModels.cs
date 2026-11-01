namespace PhotoLiker.Search
{
    /// <summary>How a photo is made into the square the model looks at.</summary>
    public enum PhotoFit
    {
        /// <summary>The shortest side is scaled to the size and the centre square is cut out (OpenAI CLIP): the edges of a wide photo are lost.</summary>
        CentreCrop,

        /// <summary>The whole photo is squeezed into the square, whatever its shape (SigLIP): nothing is lost.</summary>
        Squash,
    }

    /// <summary>The kind of tokenizer a model's text half expects (every model has its own vocabulary).</summary>
    public enum TextTokens
    {
        /// <summary>OpenAI CLIP's byte-pair vocabulary (vocab.json and merges.txt).</summary>
        Clip,

        /// <summary>SigLIP 2's SentencePiece model (the Gemma vocabulary): the text is lower-cased, punctuation dropped, padded to 64.</summary>
        SigLip,

        /// <summary>XLM-RoBERTa's SentencePiece model, in a hundred languages: padded to 77.</summary>
        XlmRoberta,
    }

    /// <summary>A file of a model: its name in the model's folder, where it is downloaded from, and about how big it is.</summary>
    public sealed record ModelFile(string Name, string Url, int Megabytes);

    /// <summary>
    /// One way to match words with photos: a photo and a text are each made into a vector, and the closer the vectors the better the
    /// text describes the photo. The vectors of different models mean different things, so an index keeps a vector per photo and model
    /// (<see cref="IndexedPhoto.Clips"/>) and a search uses the vectors of the model it asks with.
    /// </summary>
    public sealed record ClipModelInfo
    {
        /// <summary>Short and stable: it is what the folder's settings file and the index file store.</summary>
        public required string Id { get; init; }

        /// <summary>The name shown to the user.</summary>
        public required string Title { get; init; }

        /// <summary>Who made the model (shown next to the name).</summary>
        public required string Vendor { get; init; }

        /// <summary>The languages a description can be written in, in the shortest form (country codes, EN, RU, UA), shown next to the name.</summary>
        public required string Languages { get; init; }

        /// <summary>The folder of the model's files below the models folder.</summary>
        public required string FolderName { get; init; }

        /// <summary>The numbers in a vector.</summary>
        public required int Dimensions { get; init; }

        /// <summary>The side of the square the model looks at, in pixels.</summary>
        public int ImageSize { get; init; } = 224;

        public required float[] Mean { get; init; }
        public required float[] Std { get; init; }
        public required PhotoFit Fit { get; init; }
        public required TextTokens Tokens { get; init; }

        public required ModelFile Vision { get; init; }
        public required ModelFile Text { get; init; }

        /// <summary>The files of the tokenizer: vocab.json and merges.txt for <see cref="TextTokens.Clip"/>, the one SentencePiece model otherwise.</summary>
        public required IReadOnlyList<ModelFile> Tokenizer { get; init; }

        // Cosines of different models do not live on the same scale (a good SigLIP match scores 0.16, a good CLIP match 0.31), but the
        // cutoffs of the searches and the precision slider are tuned for CLIP's. These put every model's cosines on that scale: a straight
        // line through the median of the matching pairs and the median of the other pairs (text), and through the median of near-duplicate
        // photos and the median of random photos (More like this), taken from 300 Flickr30k photos with their 1,500 captions and
        // matched to CLIP B/32. With them the standard cutoffs keep about the same photos per search on every model. They come from
        // that sample, not from a real library, so they are a starting point.
        public float TextScale { get; init; } = 1f;
        public float TextOffset { get; init; }
        public float SimilarScale { get; init; } = 1f;
        public float SimilarOffset { get; init; }

        public int InputLength => 3 * ImageSize * ImageSize;

        public IEnumerable<ModelFile> Files => [Vision, Text, .. Tokenizer];

        public int Megabytes => Files.Sum(f => f.Megabytes);

        /// <summary>A text-to-photo cosine on the scale of the search cutoffs.</summary>
        public float TextScore(float cosine) => TextScale * cosine + TextOffset;

        /// <summary>A photo-to-photo cosine on the scale of the "More like this" cutoff.</summary>
        public float SimilarScore(float cosine) => SimilarScale * cosine + SimilarOffset;
    }

    /// <summary>
    /// The models a folder can search with. The first is the one every folder starts with, and the one the first versions of the
    /// app used, so an index made then is an index of this model.
    /// </summary>
    public static class ClipModels
    {
        private const string XenovaClip = "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/";

        // The Immich project's ONNX exports of OpenCLIP models (the weights are Apache 2.0 and MIT licensed), at a fixed revision so
        // that a later change of the repository can not break the download
        private const string SigLip2Export = "https://huggingface.co/immich-app/ViT-B-16-SigLIP2__webli/resolve/5f3fc29104ebcf51e9d893af57604badd51a3ea6/";
        private const string SigLip2Tokenizer = "https://huggingface.co/google/siglip2-base-patch16-224/resolve/75de2d55ec2d0b4efc50b3e9ad70dba96a7b2fa2/tokenizer.model";
        private const string XlmRobertaExport = "https://huggingface.co/immich-app/XLM-Roberta-Base-ViT-B-32__laion5b_s13b_b90k/resolve/111f6ddca817bb6cd8343c3e3dc5ce39d9ba54b0/";

        private static readonly float[] ClipMean = [0.48145466f, 0.4578275f, 0.40821073f];
        private static readonly float[] ClipStd = [0.26862954f, 0.26130258f, 0.27577711f];
        private static readonly float[] Half = [0.5f, 0.5f, 0.5f];

        /// <summary>OpenAI CLIP ViT-B/32 (MIT licence, ONNX export by Xenova): the fastest, English only.</summary>
        public static ClipModelInfo Clip { get; } = new()
        {
            Id = "clip-vit-b32",
            Title = "CLIP B/32",
            Vendor = "OpenAI",
            Languages = "EN",
            FolderName = "clip-vit-b32",
            Dimensions = 512,
            Mean = ClipMean,
            Std = ClipStd,
            Fit = PhotoFit.CentreCrop,
            Tokens = TextTokens.Clip,
            Vision = new("vision_model.onnx", XenovaClip + "onnx/vision_model.onnx", 352),
            Text = new("text_model.onnx", XenovaClip + "onnx/text_model.onnx", 254),
            Tokenizer = [new("vocab.json", XenovaClip + "vocab.json", 1), new("merges.txt", XenovaClip + "merges.txt", 1)],
        };

        /// <summary>
        /// Google's SigLIP 2 Base/16 (Apache 2.0): the best of the three, English and a good deal of Russian, Ukrainian less so.
        /// A photo takes about four times as long as with CLIP B/32.
        /// </summary>
        public static ClipModelInfo SigLip2 { get; } = new()
        {
            Id = "siglip2-b16",
            Title = "SigLIP 2 B/16",
            Vendor = "Google",
            Languages = "EN, RU; UA weaker",
            FolderName = "siglip2-b16",
            Dimensions = 768,
            Mean = Half,
            Std = Half,
            Fit = PhotoFit.Squash,
            Tokens = TextTokens.SigLip,
            Vision = new("vision.onnx", SigLip2Export + "visual/model.onnx", 372),
            Text = new("text.onnx", SigLip2Export + "textual/model.onnx", 1129),
            Tokenizer = [new("tokenizer.model", SigLip2Tokenizer, 4)],
            TextScale = 0.782f,
            TextOffset = 0.189f,
            SimilarScale = 1.075f,
            SimilarOffset = -0.072f,
        };

        /// <summary>
        /// LAION's XLM-RoBERTa text half with a ViT-B/32 photo half (MIT licence): as fast as CLIP B/32, a little better in English,
        /// and it understands about a hundred languages, Ukrainian included.
        /// </summary>
        public static ClipModelInfo XlmRoberta { get; } = new()
        {
            Id = "xlmr-vit-b32",
            Title = "XLM-R B/32",
            Vendor = "LAION",
            Languages = "~100 languages",
            FolderName = "xlmr-vit-b32",
            Dimensions = 512,
            Mean = ClipMean,
            Std = ClipStd,
            Fit = PhotoFit.CentreCrop,
            Tokens = TextTokens.XlmRoberta,
            Vision = new("vision.onnx", XlmRobertaExport + "visual/model.onnx", 352),
            Text = new("text.onnx", XlmRobertaExport + "textual/model.onnx", 1113),
            Tokenizer = [new("tokenizer.model", XlmRobertaExport + "textual/sentencepiece.bpe.model", 5)],
            TextScale = 0.683f,
            TextOffset = 0.138f,
            SimilarScale = 0.938f,
            SimilarOffset = 0.073f,
        };

        /// <summary>Every model, the default first.</summary>
        public static IReadOnlyList<ClipModelInfo> All { get; } = [Clip, SigLip2, XlmRoberta];

        public static ClipModelInfo Default => Clip;

        /// <summary>The model with this id (case does not matter); the default one if there is none, or the id is empty or from a newer version.</summary>
        public static ClipModelInfo Find(string? id) =>
            All.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }
}
