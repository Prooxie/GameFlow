using GameFlow.Infrastructure.Updates;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Updates;

public sealed class UpdateDownloadPolicyTests
{
    [Theory]
    [InlineData("GameFlow-v1.0.7-win-x64.zip", true)]
    [InlineData("../outside.zip", false)]
    [InlineData("..\\outside.zip", false)]
    [InlineData("C:\\outside.zip", false)]
    [InlineData("asset.zip:stream", false)]
    [InlineData("bad\0.zip", false)]
    public void RequiresPortableAssetFilename(string name, bool expected) =>
        Assert.Equal(expected, UpdateDownloadPolicy.IsSafeFileName(name));

    [Theory]
    [InlineData("https://github.com/Prooxie/GameFlow/releases", true)]
    [InlineData("http://github.com/Prooxie/GameFlow/releases", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("https://user:password@example.com/asset.zip", false)]
    public void RequiresHttpsWithoutCredentials(string url, bool expected) =>
        Assert.Equal(expected, UpdateDownloadPolicy.IsHttps(new Uri(url)));
}
