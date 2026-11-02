namespace Databy.Reports.Services;

public interface IFileDialogService
{
    Task<string?> PickExportPathAsync(string suggestedFileName, CancellationToken cancellationToken = default);
}