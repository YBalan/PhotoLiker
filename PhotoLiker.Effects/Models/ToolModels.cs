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

    /// <summary>The models of the features that edit or move a photo: the depth of a picture (the parallax effect), its colours (Colorize), its subject (the Telegram stickers), its text (Scan text in the Drawing window) and its handwriting (Scan manuscripts).</summary>
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

        /// <summary>
        /// IS-Net "general use" (Apache 2.0, the DIS project of Xuebin Qin and others): which parts of a photo are its subject, 1024 by 1024, for the Telegram stickers. The ONNX file is the one
        /// the rembg project publishes (a release of its repository, at a fixed tag). U2-Net is a third of the size in time but leaves the shadow of a car on the road in the cutout.
        /// </summary>
        public static ToolModel Cutout { get; } = new()
        {
            Id = "isnet-general-use",
            Title = "IS-Net general use",
            FolderName = "isnet-general-use",
            File = new ModelFile(
                "isnet-general-use.onnx",
                "https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx",
                179),
            Sha256 = "60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a",
            Origin = "IS-Net general use (DIS) by Xuebin Qin and others, Apache 2.0, as exported by the rembg project",
        };

        /// <summary>
        /// PP-OCRv3 text detection (Apache 2.0, PaddleOCR by Baidu): where the lines of text in a picture are, for Scan text in the Drawing window. Small (2.4 MB) and quick; the PP-OCRv5 server model of the same
        /// repository is 88 MB, takes four times as long and splits a line into its words. ONNX export of the monkt/paddleocr-onnx repository, at a fixed revision. The reader is <see cref="OcrReader"/>.
        /// </summary>
        public static ToolModel OcrFinder { get; } = new()
        {
            Id = "ppocr-v3-det",
            Title = "PP-OCR text finder",
            FolderName = "ppocr-v3-det",
            File = new ModelFile(
                "ppocr-v3-det.onnx",
                "https://huggingface.co/monkt/paddleocr-onnx/resolve/7b02d0a30a07ba2b92ad1ff5a8941ae2c633de65/detection/v3/det.onnx",
                3),
            Sha256 = "ee40e80071ba3a320d4efda75f3e22047a7d049e9bf7bcaaf9daea23fc21b935",
            Origin = "PP-OCRv3 text detection by PaddleOCR (Baidu), Apache 2.0",
        };

        /// <summary>
        /// PP-OCRv5 East Slavic text recognition (Apache 2.0, PaddleOCR by Baidu): reads a line of text in Ukrainian, Russian, Belarusian, Bulgarian or English, for Scan text in the Drawing window.
        /// 3 by 48 by any width in, one chance for each of 519 characters for each slice of the line out; its dictionary is in PhotoLiker.Drawing. ONNX export of the monkt/paddleocr-onnx repository.
        /// </summary>
        public static ToolModel OcrReader { get; } = new()
        {
            Id = "ppocr-v5-eslav-rec",
            Title = "PP-OCR text reader (Ukrainian, Russian, English)",
            FolderName = "ppocr-v5-eslav-rec",
            File = new ModelFile(
                "ppocr-v5-eslav-rec.onnx",
                "https://huggingface.co/monkt/paddleocr-onnx/resolve/7b02d0a30a07ba2b92ad1ff5a8941ae2c633de65/languages/eslav/rec.onnx",
                8),
            Sha256 = "dc6bf0e855247decce214ba6dae5bc135fa0ad725a5918a7fcfb59fad6c9cdee",
            Origin = "PP-OCRv5 East Slavic text recognition by PaddleOCR (Baidu), Apache 2.0",
        };

        /// <summary>
        /// The Ukrainian handwriting network of the Polyscriptor project (Apache 2.0, Achim Rabus, University of Freiburg; trained on the MultiHTR data of the Transkribus models): a CNN, a bidirectional LSTM
        /// and CTC, 5 million weights, 4.8% character errors on its own test pages of the 19th and 20th century. It is published as a PyTorch checkpoint, which this program turns into an ONNX file on
        /// this computer (<c>PhotoLiker.Manuscripts</c>: the checkpoint is read without running anything of it); the SHA-256 is the one of the file at this revision.
        /// </summary>
        public static ToolModel HandwritingUkrainian { get; } = new()
        {
            Id = "crnn-ctc-ukrainian",
            Title = "Ukrainian handwriting model (CRNN-CTC)",
            FolderName = "crnn-ctc-ukrainian",
            File = new ModelFile(
                "best_model.pt",
                "https://huggingface.co/achimrabus/crnn-ctc-ukrainian/resolve/f889a20feee96e6b85e807e70e8a9a0b8d3d8b42/best_model.pt",
                43),
            Sha256 = "6921f1a37d1399ed736ffa288688c7c4b20b27aaf1fce96ad01dea37aef8a292",
            Origin = "Ukrainian handwriting model (CRNN-CTC) by Achim Rabus, University of Freiburg (Polyscriptor), Apache 2.0",
        };

        /// <summary>The same network trained on Russian handwriting and print of the 18th to 20th century (6.9% character errors on its own test pages), at its revision.</summary>
        public static ToolModel HandwritingRussian { get; } = new()
        {
            Id = "crnn-ctc-russian",
            Title = "Russian handwriting model (CRNN-CTC)",
            FolderName = "crnn-ctc-russian",
            File = new ModelFile(
                "best_model.pt",
                "https://huggingface.co/achimrabus/crnn-ctc-russian/resolve/ebb5fc91eb44f40f049bafd74bc95f7c0e2b1ee8/best_model.pt",
                43),
            Sha256 = "74482bd0eb8e33b25a197d0452559b888809dd0ae4e014274301a513f553dd32",
            Origin = "Russian handwriting model (CRNN-CTC) by Achim Rabus, University of Freiburg (Polyscriptor), Apache 2.0",
        };

        /// <summary>
        /// Qwen2.5 1.5B Instruct (Apache 2.0, Alibaba Cloud): a small chat model that corrects the text of a scan in Scan manuscripts (the AI button of the text box). The weights are the 4 bit ones (the ONNX
        /// export of the onnx-community repository, which is what transformers.js runs): 1.8 GB, about 6 words a second on a processor. Qwen2.5 3B and 7B know Ukrainian better but the 3B is not under a free licence and
        /// the 7B is five times the size. Gemma 3 1B (half the size) was no better on Ukrainian and says more than it was asked to. It is read with <see cref="TextFixerTokenizer"/>, which is in the same folder.
        /// </summary>
        public static ToolModel TextFixer { get; } = new()
        {
            Id = "qwen2.5-1.5b-instruct-q4",
            Title = "Qwen2.5 1.5B Instruct (AI text corrector)",
            FolderName = "qwen2.5-1.5b-instruct",
            File = new ModelFile(
                "model_q4.onnx",
                "https://huggingface.co/onnx-community/Qwen2.5-1.5B-Instruct/resolve/d54957e3af1be04546436e874a60fe386041b244/onnx/model_q4.onnx",
                1788),
            Sha256 = "70c24509f760fed9a8f50391165c46a87660197028a42c4547763f1c173ddea6",
            Origin = "Qwen2.5 1.5B Instruct by Alibaba Cloud (the Qwen team), Apache 2.0, as exported to ONNX by the onnx-community repository",
        };

        /// <summary>The words the text corrector knows (its <c>tokenizer.json</c>), at the same revision, in the folder of <see cref="TextFixer"/>.</summary>
        public static ToolModel TextFixerTokenizer { get; } = new()
        {
            Id = "qwen2.5-1.5b-instruct-tokenizer",
            Title = "Qwen2.5 tokenizer (AI text corrector)",
            FolderName = "qwen2.5-1.5b-instruct",
            File = new ModelFile(
                "tokenizer.json",
                "https://huggingface.co/onnx-community/Qwen2.5-1.5B-Instruct/resolve/d54957e3af1be04546436e874a60fe386041b244/tokenizer.json",
                7),
            Sha256 = "a8506e7111b80c6d8635951a02eab0f4e1a8e4e5772da83846579e97b16f61bf",
            Origin = "Qwen2.5 tokenizer by Alibaba Cloud (the Qwen team), Apache 2.0",
        };
    }
}
