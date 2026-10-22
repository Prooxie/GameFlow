using GameFlow.Infrastructure.Configuration;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Configuration;

/// <summary>Tests for <see cref="AtomicFile"/>.</summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "gameflow-atomic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string FilePath => Path.Combine(directory, "settings.json");

    [Fact]
    public void WriteAllTextMissingFolderCreatesFolderAndFile()
    {
        AtomicFile.WriteAllText(FilePath, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", File.ReadAllText(FilePath));
    }

    [Fact]
    public void WriteAllTextExistingFileReplacesContents()
    {
        AtomicFile.WriteAllText(FilePath, "old contents that are longer than the new ones");

        AtomicFile.WriteAllText(FilePath, "new");

        Assert.Equal("new", File.ReadAllText(FilePath));
    }

    /// <summary>
    /// The temporary file must not stay. A store that lists its folder would see it.
    /// </summary>
    [Fact]
    public void WriteAllTextAnyFileLeavesOnlyTheTarget()
    {
        AtomicFile.WriteAllText(FilePath, "one");

        AtomicFile.WriteAllText(FilePath, "two");

        Assert.Equal([FilePath], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task WriteAllTextAsyncExistingFileReplacesContentsAsync()
    {
        AtomicFile.WriteAllText(FilePath, "old");

        await AtomicFile.WriteAllTextAsync(FilePath, "new", TestContext.Current.CancellationToken);

        Assert.Equal("new", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A reader can hold the file open for a moment, for example a slot rebuild
    /// that reads a profile. The write then waits and tries again.
    /// </summary>
    [Fact]
    public async Task WriteAllTextAsyncReaderHoldsFileBrieflyStillReplacesAsync()
    {
        AtomicFile.WriteAllText(FilePath, "old");
        using var reader = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        // Always release the file, including when the test cancels the delay.
        var release = Task.Delay(60, TestContext.Current.CancellationToken).ContinueWith(
            _ => reader.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        await AtomicFile.WriteAllTextAsync(FilePath, "new", TestContext.Current.CancellationToken);
        await release;

        Assert.Equal("new", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WriteAllTextEmptyPathThrowsArgumentException()
    {
        void Write() => AtomicFile.WriteAllText(string.Empty, "x");

        _ = Assert.ThrowsAny<ArgumentException>(Write);
    }
}
