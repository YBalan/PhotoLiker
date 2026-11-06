using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PhotoLiker.Search.Tests;

/// <summary>
/// Which model files an indexing run asks to download, and downloads. An index brought from another computer is complete, so there is
/// nothing to index, but searching by words still needs the model's files: 'Update index' has to get them. Runs on an empty models
/// folder, so nothing here needs the real models (the accepted download is served by a local socket, not the internet).
/// </summary>
public class ModelDownloadTests : IDisposable
{
    private static readonly string[] Extensions = [".jpg"];
    private static readonly ClipModelInfo Model = ClipModels.Default;

    private readonly TempFolder _photos = new();
    private readonly TempFolder _modelsFolder = new();
    private readonly ModelHub _models;
    private readonly FolderIndex _search;

    public ModelDownloadTests()
    {
        _models = new ModelHub(_modelsFolder.Path);
        _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);
    }

    public void Dispose()
    {
        _search.Dispose();
        _models.Dispose();
        _photos.Dispose();
        _modelsFolder.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    /// <summary>A photo on disk that the index knows completely: vector, faces and date, so that a run has nothing to index.</summary>
    private void AddCompletePhoto(string name = "red.jpg")
    {
        File.WriteAllBytes(_photos.File(name), IndexingJobTests.SolidJpeg(0, 0, 230));
        var info = new FileInfo(_photos.File(name));
        _search.Index.Save([new IndexedPhoto(name, info.Length, info.LastWriteTimeUtc.Ticks)
        {
            Clips = Vectors.Clips(Model, 1),
            FaceVersion = IndexingJob.FaceVersion,
            Date = new PhotoDate(new DateTime(2009, 9, 1), DateSource.Exif, PhotoDates.Version),
        }]);
    }

    private Task<IndexSummary> Run(Func<ModelDownload, bool>? confirm, bool forSearch, ModelHub? models = null) =>
        _search.IndexAsync(models ?? _models, excludedFolder: null, Extensions, progress: null, CancellationToken.None, confirm, forSearch);

    [Fact]
    public async Task ACompleteIndex_WithoutTheModelFiles_AsksForTheSearchModelOnly()
    {
        AddCompletePhoto();
        var asked = new List<ModelDownload>();

        var summary = await Run(download => { asked.Add(download); return false; }, forSearch: true);

        var request = Assert.Single(asked);
        Assert.Equal(Model, request.Clip);
        Assert.False(request.Faces);                 // nothing is left to analyze, so the face models are not needed
        Assert.False(request.WithIndexing);          // the question must not promise to index the folder
        Assert.Equal(Model.Megabytes, request.Megabytes);
        Assert.True(summary.DownloadDeclined);
        Assert.False(summary.Cancelled);
        Assert.False(summary.ModelsDownloaded);
        Assert.False(_models.ClipDownloaded(Model));  // declined: nothing was fetched
    }

    [Fact]
    public async Task WithoutAskingForSearch_ACompleteIndexNeedsNothing_SoDateJobsStayOffline()
    {
        AddCompletePhoto();
        var asked = 0;

        var summary = await Run(_ => { asked++; return true; }, forSearch: false);

        Assert.Equal(0, asked);
        Assert.False(summary.DownloadDeclined);
        Assert.False(summary.ModelsDownloaded);
        Assert.False(_models.ClipDownloaded(Model));
    }

    [Fact]
    public async Task AFolderWithNothingIndexed_HasNoVectorsToSearch_SoTheSearchModelIsNotAsked()
    {
        var asked = 0;

        var summary = await Run(_ => { asked++; return true; }, forSearch: true); // an empty folder

        Assert.Equal(0, asked);
        Assert.False(summary.ModelsDownloaded);
    }

    [Fact]
    public async Task APhotoToIndex_AsksForTheModelAndTheFaceModels_AndDecliningIndexesNothing()
    {
        File.WriteAllBytes(_photos.File("new.jpg"), IndexingJobTests.SolidJpeg(0, 200, 20));
        var asked = new List<ModelDownload>();

        var summary = await Run(download => { asked.Add(download); return false; }, forSearch: true);

        var request = Assert.Single(asked);
        Assert.Equal(Model, request.Clip);
        Assert.True(request.Faces);
        Assert.True(request.WithIndexing);
        Assert.Equal(Model.Megabytes + ModelFiles.FacePackMegabytes, request.Megabytes);
        Assert.True(summary.DownloadDeclined);
        Assert.Equal(0, summary.Indexed);
        Assert.Equal(0, _search.Index.PhotoCount);
    }

    [Fact]
    public async Task WhenTheFilesAreHere_NothingIsAskedOrDownloaded()
    {
        AddCompletePhoto();
        foreach (var file in Model.Files)
        {
            var path = ModelFiles.PathOf(_models.ModelsFolder, Model, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "model");
        }
        var asked = 0;

        var summary = await Run(_ => { asked++; return true; }, forSearch: true);

        Assert.Equal(0, asked);
        Assert.False(summary.ModelsDownloaded);
        Assert.False(summary.DownloadDeclined);
    }

    [Fact]
    public async Task AcceptingTheQuestion_DownloadsTheSearchModelFiles_AndNothingIsIndexed()
    {
        using var server = new LocalFileServer("model-bytes");
        var served = Model with
        {
            Vision = new ModelFile("vision.onnx", server.Url("vision.onnx"), 1),
            Text = new ModelFile("text.onnx", server.Url("text.onnx"), 1),
            Tokenizer = [new ModelFile("vocab.json", server.Url("vocab.json"), 1)],
        };
        AddCompletePhoto();
        _search.ClipModel = served; // same id as the default model, so the photo's vector counts and there is nothing to index
        var asked = new List<ModelDownload>();

        var summary = await Run(download => { asked.Add(download); return true; }, forSearch: true);

        Assert.Single(asked);
        Assert.True(summary.ModelsDownloaded);
        Assert.False(summary.DownloadDeclined);
        Assert.Equal(0, summary.Indexed + summary.Failed);
        Assert.True(_models.ClipDownloaded(served));
        Assert.False(_models.FacesDownloaded);        // no photo needed faces
        Assert.All(served.Files, file => Assert.Equal("model-bytes", File.ReadAllText(ModelFiles.PathOf(_models.ModelsFolder, served, file))));
    }

    /// <summary>Answers every request on a free local port with the same bytes.</summary>
    private sealed class LocalFileServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly string _body;

        public LocalFileServer(string body)
        {
            _body = body;
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public string Url(string name) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/{name}";

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or InvalidOperationException)
            {
                // the test is over
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0)
                        return;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var body = Encoding.ASCII.GetBytes(_body);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
