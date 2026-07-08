namespace GameFlow.App.Services;

public interface IProfileFileDialogService
{
    Task<string?> ImportProfileJsonAsync();
    Task<bool> ExportProfileJsonAsync(string suggestedFileName, string json);
}
