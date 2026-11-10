using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Tests.Models;

/// <summary>The download of the models of the features that edit or move a photo (<see cref="ToolModels"/>), and what is written down about them.</summary>
public class ToolModelTests : IDisposable
{
    private readonly TempFolder _models = new();

    public void Dispose() => _models.Dispose();

    private static ToolModel Served(LocalFileServer server, string bytes, string? sha256 = null) => new()
    {
        Id = "served",
        Title = "Served model",
        FolderName = "served-model",
        File = new ModelFile("model.onnx", server.Url("model.onnx"), 1),
        Sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(bytes))).ToLowerInvariant(),
        Origin = "a test",
    };

    // ── what is written down ────────────────────────────────────────────────

    [Theory]
    [InlineData("depth")]
    [InlineData("colorizer")]
    public void TheModelsAreAtAFixedRevision_WithAHashAndASize(string which)
    {
        var model = which == "depth" ? ToolModels.Depth : ToolModels.Colorizer;

        Assert.StartsWith("https://huggingface.co/", model.File.Url);
        Assert.Matches("/resolve/[0-9a-f]{40}/", model.File.Url); // a commit, not "main": a later change of the repository can not reach the program
        Assert.Matches("^[0-9a-f]{64}$", model.Sha256);
        Assert.True(model.Megabytes > 0);
        Assert.EndsWith(".onnx", model.File.Name);
        Assert.False(string.IsNullOrWhiteSpace(model.Origin));
        Assert.Contains("Apache 2.0", model.Origin); // what the weights are licensed under, shown when the person is asked
    }

    [Fact]
    public void TheModels_HaveTheirOwnFolders_AndNames()
    {
        Assert.NotEqual(ToolModels.Depth.FolderName, ToolModels.Colorizer.FolderName);
        Assert.NotEqual(ToolModels.Depth.Id, ToolModels.Colorizer.Id);
        Assert.NotEqual(ToolModels.Depth.Sha256, ToolModels.Colorizer.Sha256);
    }

    [Fact]
    public void TheFilesAreWhereTheHubLooksForThem()
    {
        var hub = new EffectModels(_models.Path);

        Assert.False(hub.DepthDownloaded);
        Assert.False(hub.ColorizerDownloaded);

        Directory.CreateDirectory(Path.GetDirectoryName(ToolModels.Depth.PathIn(_models.Path))!);
        File.WriteAllText(ToolModels.Depth.PathIn(_models.Path), "x");

        Assert.True(hub.DepthDownloaded);
        Assert.False(hub.ColorizerDownloaded);
        Assert.Equal(Path.Combine(_models.Path, ToolModels.Depth.FolderName, ToolModels.Depth.File.Name), ToolModels.Depth.PathIn(_models.Path));
    }

    // ── the download ────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFile_IsDownloaded_AndChecked_AndKept()
    {
        using var server = new LocalFileServer("model-bytes");
        var model = Served(server, "model-bytes");
        var reports = new List<DownloadProgress>();

        await new EffectModels(_models.Path).EnsureDownloadedAsync(model, new Progress<DownloadProgress>(reports.Add), CancellationToken.None);
        await Task.Delay(50); // a Progress reports on the thread pool

        var path = model.PathIn(_models.Path);
        Assert.Equal("model-bytes", File.ReadAllText(path));
        Assert.True(model.IsPresentIn(_models.Path));
        Assert.False(File.Exists(path + ".part"));
        Assert.NotEmpty(reports);
        Assert.All(reports, report => Assert.Equal("Served model", report.Name));
        Assert.Equal("model-bytes".Length, reports.Max(report => report.Done));
    }

    [Fact]
    public async Task AFileThatIsNotTheOneThatWasTested_IsRefused_AndNothingIsLeft()
    {
        using var server = new LocalFileServer("something else");
        var model = Served(server, "model-bytes"); // the hash of other bytes

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EffectModels(_models.Path).EnsureDownloadedAsync(model, null, CancellationToken.None));

        Assert.Contains("SHA-256", error.Message);
        Assert.False(model.IsPresentIn(_models.Path));
        Assert.False(File.Exists(model.PathIn(_models.Path) + ".part"));
    }

    [Fact]
    public async Task TheHashOfTheDownload_IsComparedWithoutRegardToCase()
    {
        using var server = new LocalFileServer("model-bytes");
        var lower = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("model-bytes"))).ToLowerInvariant();

        await new EffectModels(_models.Path).EnsureDownloadedAsync(Served(server, "model-bytes", lower.ToUpperInvariant()), null, CancellationToken.None);

        Assert.Equal("model-bytes", File.ReadAllText(Served(server, "model-bytes").PathIn(_models.Path)));
    }

    [Fact]
    public async Task AFileThatIsThereAlready_AndIsTheOneThatWasTested_IsNotDownloadedAgain_NorChanged()
    {
        using var server = new LocalFileServer("new bytes");
        var model = Served(server, "new bytes");
        var path = model.PathIn(_models.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "new bytes");
        var written = File.GetLastWriteTimeUtc(path);

        await new EffectModels(_models.Path).EnsureDownloadedAsync(model, null, CancellationToken.None);

        Assert.Equal(0, server.Requests);
        Assert.Equal("new bytes", File.ReadAllText(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task AFileThatIsThereAlready_ButIsNotTheOneThatWasTested_IsDownloadedAgain()
    {
        using var server = new LocalFileServer("new bytes");
        var model = Served(server, "new bytes");
        var path = model.PathIn(_models.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "old by"); // cut short, damaged, or changed by something else

        await new EffectModels(_models.Path).EnsureDownloadedAsync(model, null, CancellationToken.None);

        Assert.Equal(1, server.Requests);
        Assert.Equal("new bytes", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task AFileThatIsNotTheOneThatWasTested_AndANewOneThatIsNotEither_LeavesNothingToUse()
    {
        using var server = new LocalFileServer("something else");
        var model = Served(server, "new bytes");
        var path = model.PathIn(_models.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "old bytes");

        await Assert.ThrowsAsync<InvalidDataException>(() => new EffectModels(_models.Path).EnsureDownloadedAsync(model, null, CancellationToken.None));

        Assert.False(model.IsPresentIn(_models.Path)); // the bad file that was there is not left to be used
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task WithNoHashToGoBy_AFileThatIsThere_IsKept()
    {
        using var server = new LocalFileServer("new bytes");
        var path = _models.File(Path.Combine("search-model", "model.onnx"));
        File.WriteAllText(path, "old bytes");

        await ModelFiles.DownloadAsync(server.Url("model.onnx"), path, "a model", null, CancellationToken.None);

        Assert.Equal(0, server.Requests);
        Assert.Equal("old bytes", File.ReadAllText(path));
    }

    [Fact]
    public void TheHashOfAFile_IsComparedWithoutRegardToCase_AndAFileThatCannotBeReadIsAnError()
    {
        var path = _models.File("file.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("model-bytes"));
        var lower = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("model-bytes"))).ToLowerInvariant();

        Assert.True(ModelFiles.HasSha256(path, lower));
        Assert.True(ModelFiles.HasSha256(path, lower.ToUpperInvariant()));
        Assert.False(ModelFiles.HasSha256(path, new string('0', 64)));
        Assert.ThrowsAny<IOException>(() => ModelFiles.HasSha256(_models.File("gone.bin"), lower));
    }

    [Fact]
    public async Task ADownloadThatFails_LeavesNoFile_AndTheNextTryWorks()
    {
        var model = new ToolModel
        {
            Id = "gone", Title = "Gone", FolderName = "gone", Sha256 = new string('0', 64), Origin = "a test",
            File = new ModelFile("model.onnx", "http://127.0.0.1:1/model.onnx", 1), // nothing listens there
        };

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => new EffectModels(_models.Path).EnsureDownloadedAsync(model, null, CancellationToken.None));

        Assert.False(model.IsPresentIn(_models.Path));

        using var server = new LocalFileServer("model-bytes");
        await new EffectModels(_models.Path).EnsureDownloadedAsync(Served(server, "model-bytes"), null, CancellationToken.None);
    }

    // ── a session that is let go of while it runs ───────────────────────────

    [Fact]
    public void TheGuard_FreesAtOnce_WhenNothingRuns_AndRefusesWhatComesAfter()
    {
        var freed = 0;
        var guard = new RunGuard(() => freed++, "model");

        guard.Enter();
        guard.Leave();
        Assert.Equal(0, freed);

        guard.Dispose();
        Assert.Equal(1, freed);
        guard.Dispose(); // twice is the same as once
        Assert.Equal(1, freed);
        Assert.Throws<ObjectDisposedException>(() => guard.Enter());
    }

    [Fact]
    public void TheGuard_WaitsForTheRunsThatAreGoingOn_AndTheLastOneFrees()
    {
        var freed = 0;
        var guard = new RunGuard(() => freed++, "model");

        guard.Enter();
        guard.Enter();
        guard.Dispose(); // while two runs are going on
        Assert.Equal(0, freed);
        Assert.Throws<ObjectDisposedException>(() => guard.Enter()); // no new one begins

        guard.Leave();
        Assert.Equal(0, freed); // one is still going on
        guard.Leave();
        Assert.Equal(1, freed);
    }

    [Fact]
    public async Task TheGuard_FreesOnce_WhenManyRunsEndAtTheSameTimeAsItIsDisposed()
    {
        for (var round = 0; round < 200; round++)
        {
            var freed = 0;
            var guard = new RunGuard(() => Interlocked.Increment(ref freed), "model");
            var runs = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try
                {
                    guard.Enter();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                Thread.SpinWait(200);
                guard.Leave();
            })).ToArray();

            guard.Dispose();
            await Task.WhenAll(runs);

            Assert.Equal(1, freed);
        }
    }

    [Fact]
    public async Task ADepthModelThatIsDisposedOfWhileItRuns_FinishesTheRun_AndDoesNotCrash()
    {
        if (!ToolModels.Depth.IsPresentIn(RealModels.Folder)) return;
        var estimator = new PhotoLiker.Effects.Depth.DepthEstimator(ToolModels.Depth.PathIn(RealModels.Folder), 2);
        var photo = new DecodedPhoto(320, 200, Enumerable.Range(0, 320 * 200 * 3).Select(i => (byte)(i % 251)).ToArray());

        var run = Task.Run(() => estimator.Estimate(photo));
        await Task.Delay(100); // the model is at work
        estimator.Dispose();   // the program closes: this used to free the session under the run (a crash of the whole process)

        var depth = await run; // the run that had begun finished, with its result
        Assert.True(depth.Width > 0 && depth.Values.Length == depth.Width * depth.Height);
        Assert.ThrowsAny<ObjectDisposedException>(() => estimator.Estimate(photo)); // and a new one is refused
    }

    /// <summary>Answers every request on a free local port with the same bytes, and counts them.</summary>
    private sealed class LocalFileServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly string _body;
        private int _requests;

        public LocalFileServer(string body)
        {
            _body = body;
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int Requests => Volatile.Read(ref _requests);

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

                Interlocked.Increment(ref _requests);
                var body = Encoding.ASCII.GetBytes(_body);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
