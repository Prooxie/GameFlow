using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;

namespace Autofire.App.Services;

/// <summary>
/// Default <see cref="IProfileFileDialogService"/>. Wraps Avalonia's
/// <see cref="IStorageProvider"/> so view-models can open import / export
/// dialogs without referencing Avalonia directly.
///
/// <para>
/// Every action is logged at Information so that support requests like
/// "I clicked Import and nothing happened" can be reconstructed from the
/// rolling log file.
/// </para>
/// </summary>
public sealed class ProfileFileDialogService : IProfileFileDialogService
{
    private readonly ILogger<ProfileFileDialogService> logger;

    /// <summary>
    /// Constructs the service.
    /// </summary>
    public ProfileFileDialogService(ILogger<ProfileFileDialogService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> ImportProfileJsonAsync()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            logger.LogWarning("Import dialog requested but no storage provider is available (no main window?).");
            return null;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Import profile",
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON profile")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Import file picker threw.");
            return null;
        }

        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
        {
            logger.LogInformation("Import cancelled by the user.");
            return null;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var json = await reader.ReadToEndAsync();

            logger.LogInformation(
                "Imported profile JSON from {Path} ({ByteCount} bytes).",
                file.Path,
                json.Length);
            return json;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read selected profile file {Path}.", file.Path);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExportProfileJsonAsync(string suggestedFileName, string json)
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            logger.LogWarning("Export dialog requested but no storage provider is available (no main window?).");
            return false;
        }

        IStorageFile? file;
        try
        {
            file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export profile",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "json",
                FileTypeChoices =
                [
                    new FilePickerFileType("JSON profile")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Export file picker threw.");
            return false;
        }

        if (file is null)
        {
            logger.LogInformation("Export cancelled by the user.");
            return false;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            await writer.WriteAsync(json);
            await writer.FlushAsync();

            logger.LogInformation(
                "Exported profile JSON to {Path} ({ByteCount} bytes).",
                file.Path,
                json.Length);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to write profile file {Path}.", file.Path);
            return false;
        }
    }

    /// <summary>
    /// Returns the storage provider attached to the main window, or
    /// <see langword="null"/> if the application is not running with a
    /// classic-desktop lifetime (e.g. headless test runs).
    /// </summary>
    private static IStorageProvider? GetStorageProvider()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.StorageProvider
            : null;
    }
}
