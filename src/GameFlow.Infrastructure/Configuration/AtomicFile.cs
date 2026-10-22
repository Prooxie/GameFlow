namespace GameFlow.Infrastructure.Configuration;

/// <summary>
/// Writes a file so that a reader sees the old contents or the new contents,
/// never a part of them.
/// </summary>
/// <remarks>
/// <para>
/// The text goes to a temporary file in the same folder. Then a move replaces
/// the target in one step. A crash or a full disk during the write leaves the
/// old file intact. A plain <see cref="File.WriteAllText(string, string)"/>
/// empties the file first, so a crash at the wrong moment leaves an empty or
/// half-written settings file, and the next start cannot read it.
/// </para>
/// <para>
/// The temporary name is unique per write, so two writers of one file cannot
/// use the same temporary file. When a reader holds the target open, the move
/// fails with a sharing error. The write then waits a short time and tries
/// again, up to <see cref="MoveAttempts"/> times.
/// </para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>Number of move attempts when a reader holds the target open.</summary>
    private const int MoveAttempts = 10;

    /// <summary>Wait between two move attempts.</summary>
    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>Writes <paramref name="contents"/> to <paramref name="path"/> as UTF-8. Creates the folder when it is missing.</summary>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    /// <exception cref="IOException">The target stayed locked for all attempts.</exception>
    public static void WriteAllText(string path, string contents)
    {
        var temp = PrepareTemp(path);
        try
        {
            File.WriteAllText(temp, contents);
            for (var attempt = 1; !TryMove(temp, path, attempt); attempt++)
            {
                Thread.Sleep(MoveRetryDelay);
            }
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>Asynchronous form of <see cref="WriteAllText"/>.</summary>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    /// <exception cref="IOException">The target stayed locked for all attempts.</exception>
    public static async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var temp = PrepareTemp(path);
        try
        {
            await File.WriteAllTextAsync(temp, contents, cancellationToken).ConfigureAwait(false);
            for (var attempt = 1; !TryMove(temp, path, attempt); attempt++)
            {
                await Task.Delay(MoveRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    private static string PrepareTemp(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        return $"{path}.{Guid.NewGuid():N}.tmp";
    }

    /// <summary>
    /// Moves the temporary file over the target. False on a sharing error while
    /// attempts remain. The last attempt lets the error go to the caller.
    /// </summary>
    private static bool TryMove(string temp, string path, int attempt)
    {
        try
        {
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < MoveAttempts)
        {
            return false;
        }
    }

    /// <summary>Removes the temporary file after a failed write. A successful move leaves nothing to remove.</summary>
    private static void DeleteQuietly(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless; the next write uses a new name.
        }
    }
}
