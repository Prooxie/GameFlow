using System.Net;
using System.Security.Cryptography;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Updates;

public sealed class UpdateIntegrityTests
{
    [Fact]
    public async Task MatchingDigestCommitsTheArchive()
    {
        byte[] bytes = [1, 2, 3, 4];
        var installer = Create();
        using var client = new HttpClient(new AssetHandler(bytes));
        var path = await installer.DownloadAssetAsync(Update(Convert.ToHexString(SHA256.HashData(bytes))), null, TestContext.Current.CancellationToken, client);
        try { Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)); }
        finally { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }
    }

    [Fact]
    public async Task MismatchedDigestRejectsTheArchive()
    {
        using var client = new HttpClient(new AssetHandler([1, 2, 3, 4]));
        await Assert.ThrowsAsync<InvalidDataException>(() => Create().DownloadAssetAsync(Update(new string('0', 64)), null, TestContext.Current.CancellationToken, client));
    }

    [Fact]
    public async Task MissingDigestRejectsBeforeDownloading()
    {
        var handler = new AssetHandler([1, 2]);
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Create().DownloadAssetAsync(Update(null), null, TestContext.Current.CancellationToken, client));
        Assert.Equal(0, handler.Requests);
    }

    private static DefaultUpdateInstaller Create() => new(Options.Create(new AppRuntimeOptions()), NullLogger<DefaultUpdateInstaller>.Instance);
    private static UpdateInfo Update(string? hash) => new(new Version(1, 0, 8), "v1.0.8", "Test", new Uri("https://example.com/release"), new Uri("https://example.com/update.zip"), "GameFlow.zip", hash);
    private sealed class AssetHandler(byte[] data) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
    }
}
