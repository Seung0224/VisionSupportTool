using System.IO;
using System.Net;
using System.Net.Http;
using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The model is fetched once, on first use, into the user's profile. A half-written file that
/// counted as present would fail later as a cryptic ONNX load error, so completeness is by exact
/// size and a broken download leaves nothing behind.
/// </summary>
public class ModelStoreTests
{
    private static readonly Uri Source = new("https://models.invalid/onnx/");

    // The second file is larger than the copy buffer, so a read loop that stops early shows up.
    private static readonly ModelFile[] Files = { new("a.onnx", 10), new("a.onnx_data", 300_000) };

    [Fact]
    public void It_is_complete_only_when_every_file_has_its_expected_size()
    {
        using var dir = new TempDir();
        var store = new ModelStore(dir.Path, Source, Files);

        Assert.False(store.IsComplete());

        File.WriteAllBytes(Path.Combine(dir.Path, "a.onnx"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir.Path, "a.onnx_data"), new byte[299_999]);
        Assert.False(store.IsComplete());

        File.WriteAllBytes(Path.Combine(dir.Path, "a.onnx_data"), new byte[300_000]);
        Assert.True(store.IsComplete());
    }

    [Fact]
    public async Task Downloading_fetches_only_what_is_missing_and_reports_completion()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "a.onnx"), new byte[10]);
        byte[] data = Pattern(300_000);
        var server = new FakeServer(new() { ["a.onnx"] = new byte[10], ["a.onnx_data"] = data });
        var store = new ModelStore(dir.Path, Source, Files);
        var reports = new List<double>();

        await store.DownloadAsync(new HttpClient(server), new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(new[] { "a.onnx_data" }, server.Requested);
        Assert.True(store.IsComplete());
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dir.Path, "a.onnx_data")));
        Assert.Equal(1.0, reports.Last(), 6);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.part"));
    }

    [Fact]
    public async Task A_file_the_server_does_not_have_fails_the_download_and_leaves_no_part_file()
    {
        using var dir = new TempDir();
        var server = new FakeServer(new() { ["a.onnx"] = new byte[10] });
        var store = new ModelStore(dir.Path, Source, Files);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => store.DownloadAsync(new HttpClient(server), null, CancellationToken.None));

        Assert.False(store.IsComplete());
        Assert.Empty(Directory.GetFiles(dir.Path, "*.part"));
    }

    [Fact]
    public async Task A_file_of_the_wrong_size_is_rejected_rather_than_kept()
    {
        using var dir = new TempDir();
        var server = new FakeServer(new() { ["a.onnx"] = new byte[10], ["a.onnx_data"] = new byte[1_000] });
        var store = new ModelStore(dir.Path, Source, Files);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.DownloadAsync(new HttpClient(server), null, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(dir.Path, "a.onnx_data")));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.part"));
    }

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)(i * 31);
        return bytes;
    }

    /// <summary>Reports on the calling thread. <see cref="Progress{T}"/> posts to the thread pool
    /// when there is no synchronisation context, and its last report can land after the assert.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class FakeServer(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public List<string> Requested { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string name = request.RequestUri!.Segments.Last();
            Requested.Add(name);

            return Task.FromResult(files.TryGetValue(name, out byte[]? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
