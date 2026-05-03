using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Autofire.App.Services;

public sealed class ProfileFileDialogService : IProfileFileDialogService
{
    public async Task<string?> ImportProfileJsonAsync()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
        {
            return null;
        }

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return await reader.ReadToEndAsync();
    }

    public async Task<bool> ExportProfileJsonAsync(string suggestedFileName, string json)
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return false;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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

        if (file is null)
        {
            return false;
        }

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        await writer.WriteAsync(json);
        await writer.FlushAsync();
        return true;
    }

    private static IStorageProvider? GetStorageProvider()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.StorageProvider
            : null;
    }
}
